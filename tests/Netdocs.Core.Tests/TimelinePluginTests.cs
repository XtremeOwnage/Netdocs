using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class TimelinePluginTests
{
    private sealed class FakeContext : IPluginContext
    {
        public SiteConfig Config { get; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = new Dictionary<string, object?>();
        public List<string> InlineScripts { get; } = new();
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) => InlineScripts.Add(javascript);
        public void AddAsset(string sourcePath, string destRelative) { }
    }

    private static string Run(string markdown, out TimelinePlugin plugin, FakeContext? ctx = null)
    {
        plugin = new TimelinePlugin();
        plugin.Configure(ctx ?? new FakeContext());
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        var page = new Page { SourcePath = "x.md", RelativePath = "x.md", RawMarkdown = markdown };
        return plugin.ProcessAsync(page, markdown, site, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Run(string markdown) => Run(markdown, out _);

    private static JsonElement ExtractSpec(string html)
    {
        var m = Regex.Match(html, "<script type=\"application/json\" class=\"nd-timeline-spec\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(m.Success, "spec <script> tag not found in: " + html);
        return JsonDocument.Parse(m.Groups[1].Value).RootElement;
    }

    private const string Communications = """
        ```timeline
        title: Communications
        inputs:
          - name: start
            label: Start Date
            default: 01/01/2027
        outputs:
          - name: first
            label: First Email
            type: all
            expr: start + 1
          - name: second
            label: Second Email
            type: weekdays
            expr: first + 1
        ```
        """;

    [Fact]
    public void TimelineFence_BecomesInteractiveForm()
    {
        var result = Run(Communications);

        Assert.Contains("<div class=\"nd-timeline\">", result);
        Assert.Contains("type=\"date\"", result);
        Assert.Contains("data-timeline-var=\"start\"", result);
        Assert.Contains("value=\"2027-01-01\"", result);
        Assert.Contains("<script type=\"application/json\" class=\"nd-timeline-spec\">", result);
        Assert.DoesNotContain("```timeline", result);
        // No build-time mermaid diagram at all now - the client renders it after reading the spec.
        Assert.DoesNotContain("```mermaid", result);
        Assert.DoesNotContain("gantt", result);
        Assert.Contains("<div class=\"nd-timeline__dates\"", result);
        Assert.Contains("<div class=\"nd-timeline__exclusions\"", result);
    }

    [Fact]
    public void Options_DefaultToDocumentedValues()
    {
        var spec = ExtractSpec(Run(Communications));
        Assert.True(spec.GetProperty("showSummary").GetBoolean());
        Assert.Equal("dddd MMM dd, yyyy", spec.GetProperty("displayDateFormat").GetString());
        Assert.False(spec.GetProperty("editExclusions").GetBoolean());
    }

    [Fact]
    public void Options_AreRespectedWhenSpecified()
    {
        // Direct top-level fields, not a nested `options` object - siblings of `title`/
        // `date_format`/etc, all snake_case (matching `date_format`); the JSON spec handed to
        // the client stays camelCase, which is just normal JSON convention.
        var md = """
            ```timeline
            show_summary: false
            display_date_format: "yyyy-MM-dd"
            edit_exclusions: true
            inputs:
              - name: start
                default: 01/01/2027
            ```
            """;
        var spec = ExtractSpec(Run(md));
        Assert.False(spec.GetProperty("showSummary").GetBoolean());
        Assert.Equal("yyyy-MM-dd", spec.GetProperty("displayDateFormat").GetString());
        Assert.True(spec.GetProperty("editExclusions").GetBoolean());
    }

    [Fact]
    public void DisplayDateFormat_IsDistinctFromParsingDateFormat()
    {
        // Both are legitimately named "date_format" one level apart - `date_format` parses
        // *authored* dates (default, exclusions); `display_date_format` only formats already-
        // resolved dates for display (the summary table, the exclusions list). Mixing them up
        // would parse the wrong thing or display raw ISO instead of the requested format.
        var md = """
            ```timeline
            date_format: YYYY-MM-DD
            display_date_format: "MM/dd/yyyy"
            inputs:
              - name: start
                default: 2027-06-15
            ```
            """;
        var result = Run(md);
        var spec = ExtractSpec(result);

        Assert.Contains("value=\"2027-06-15\"", result); // date_format parsed the authored default
        Assert.Equal("MM/dd/yyyy", spec.GetProperty("displayDateFormat").GetString());
    }

    [Fact]
    public void MultipleInputs_AllRenderAsSeparateFieldsAndCanBeReferencedIndependently()
    {
        // Two unrelated tracks anchored on two different inputs, plus an output chaining off
        // whichever one it actually names - nothing here assumes exactly one input exists.
        var md = """
            ```timeline
            inputs:
              - name: engStart
                label: Engineering Start
                default: 01/01/2027
              - name: marketingStart
                label: Marketing Start
                default: 03/01/2027
            outputs:
              - name: engMilestone
                expr: engStart + 5
              - name: marketingMilestone
                expr: marketingStart + 5
            ```
            """;
        var result = Run(md);
        var spec = ExtractSpec(result);

        Assert.Contains("data-timeline-var=\"engStart\"", result);
        Assert.Contains("data-timeline-var=\"marketingStart\"", result);
        Assert.Contains("value=\"2027-01-01\"", result);
        Assert.Contains("value=\"2027-03-01\"", result);

        var inputs = spec.GetProperty("inputs");
        Assert.Equal(2, inputs.GetArrayLength());

        var outputs = spec.GetProperty("outputs");
        Assert.Equal("engStart", outputs[0].GetProperty("ref").GetString());
        Assert.Equal("marketingStart", outputs[1].GetProperty("ref").GetString());
    }

    [Fact]
    public void EditableFalseInput_RendersStaticTextInsteadOfAPicker()
    {
        var md = """
            ```timeline
            inputs:
              - name: contractDate
                label: Contract Signed
                default: 12/01/2026
                editable: false
              - name: start
                label: Project Start
                default: 01/01/2027
            outputs:
              - name: kickoff
                expr: contractDate + 30
            ```
            """;
        var result = Run(md);

        // No picker for the non-editable one - a plain span carrying the value instead.
        Assert.DoesNotContain("type=\"date\" data-timeline-var=\"contractDate\"", result);
        Assert.Contains("<span class=\"nd-timeline__static-value\" data-timeline-var=\"contractDate\" data-iso-value=\"2026-12-01\">", result);
        Assert.Contains("nd-timeline__field--static", result);
        Assert.Contains("<span class=\"nd-timeline__field-label\">Contract Signed</span>", result);

        // The editable one is unaffected - still a real picker.
        Assert.Contains("<input id=", result);
        Assert.Contains("type=\"date\" data-timeline-var=\"start\"", result);

        // Still a full graph root: duration/expr both still work, just via a spec entry with no
        // corresponding <input> - the JSON spec doesn't need to say "editable" at all, since the
        // HTML structure itself (span vs input) is what the client evaluator branches on.
        var spec = ExtractSpec(result);
        Assert.Equal(2, spec.GetProperty("inputs").GetArrayLength());
        Assert.Equal("contractDate", spec.GetProperty("outputs")[0].GetProperty("ref").GetString());
    }

    [Fact]
    public void EditableDefaultsToTrue()
    {
        var result = Run(Communications);
        Assert.DoesNotContain("nd-timeline__field--static", result);
        Assert.Contains("<input id=", result);
    }

    [Fact]
    public void EvaluatorJs_ReadsBothInputAndStaticSpanValuesUniformly()
    {
        // "value" in el is true only for real form elements, so a plain <span> (editable: false)
        // falls through to its data-iso-value attribute - and the change-listener wiring is
        // scoped to actual <input> elements, since a static span never fires input/change.
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("\"value\" in el ? el.value : el.getAttribute(\"data-iso-value\")", js);
        Assert.Contains("block.querySelectorAll(\"input[data-timeline-var]\").forEach(function (input) {", js);
    }

    [Fact]
    public void EvaluatorJs_RendersAnAlwaysVisibleDatesTable()
    {
        // The axis alone becomes unreadable once tickInterval coarsens to weeks/months on a
        // longer project, so exact start/end dates need a fallback that isn't diagram-reading.
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("renderDatesTable", js);
        Assert.Contains("nd-timeline__dates", js);
        Assert.Contains("formatDisplayDate", js);
        // Populated synchronously, not gated behind the lazy Mermaid import.
        Assert.True(js.IndexOf("renderDatesTable(block", StringComparison.Ordinal)
            < js.IndexOf("loadMermaid().then", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluatorJs_SummaryTableIsSortedChronologically_DiagramRowOrderIsNot()
    {
        // Declaration order (inputs, then outputs) groups each track's bars together, which
        // reads fine for the diagram - so only the summary table gets a chronologically-sorted
        // *copy*; the diagram keeps receiving the original, unsorted `events` array.
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("var summaryEvents = events.slice().sort(function (a, b) {", js);
        Assert.Contains("renderDatesTable(block.querySelector(\".nd-timeline__dates\"), summaryEvents, spec.displayDateFormat);", js);
        Assert.Contains("var src = buildMermaidSource(events);", js);
    }

    [Fact]
    public void EvaluatorJs_RendersExclusionsUiAndUsesMutableClientState()
    {
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("renderExclusions", js);
        Assert.Contains("nd-timeline__exclusion-remove", js);
        Assert.Contains("nd-timeline__exclusion-add-btn", js);
        // Exclusions are mutable (add/remove) client state, seeded once from the spec at bind
        // time - not recreated fresh from spec.exclusions on every compute() call.
        Assert.Contains("block.__ndTimelineExclusions = new Set(block.__ndTimelineSpec.exclusions);", js);
        Assert.Contains("var exclusionSet = block.__ndTimelineExclusions;", js);
    }

    [Fact]
    public void EvaluatorJs_AddingAnExclusion_RequiresADeliberateAction()
    {
        // type="date" fires `change` the instant a complete date is entered - binding
        // addExclusion() to that meant every entry silently self-submitted before the reader
        // ever reached the Add button. Only a button click or Enter should submit it now.
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.DoesNotContain("addInput.addEventListener(\"change\", addExclusion)", js);
        Assert.Contains(".addEventListener(\"click\", addExclusion)", js);
        Assert.Contains("evt.key === \"Enter\"", js);
    }

    [Fact]
    public void EvaluatorJs_ExclusionPickerDefault_IsLiterallyLastValueEntered()
    {
        // No native way to hint a date picker's opening date separate from its actual value, so
        // this is necessarily also what the field shows as pre-filled. It's a literal "whatever
        // was last typed/selected" (block.__ndTimelineExclusionDraft, persisted because it's
        // simply never cleared after a successful add) - not a derived "latest date currently
        // excluded", which would disagree with this once dates aren't added in order.
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("block.__ndTimelineExclusionDraft = null;", js); // bind(): unresolved until first render
        Assert.Contains("if (block.__ndTimelineExclusionDraft === null) {", js);
        Assert.Contains("firstInput.value", js);
        Assert.Contains("block.__ndTimelineExclusionDraft = value; // don't clear it", js); // addExclusion(): persists as-is
    }

    [Fact]
    public void EvaluatorJs_RejectsDuplicateExclusionWithNativeValidation()
    {
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("block.__ndTimelineExclusions.has(value)", js);
        Assert.Contains("setCustomValidity(\"That date is already excluded.\")", js);
        Assert.Contains("reportValidity()", js);
    }

    [Fact]
    public void Configure_RegistersSingleClientSideEvaluator()
    {
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);

        Assert.Single(ctx.InlineScripts);
        var js = ctx.InlineScripts[0];
        Assert.Contains("daysFromCivil", js);
        Assert.Contains("loadMermaid", js);
        Assert.Contains("nd-timeline-spec", js);
    }

    [Fact]
    public void Spec_DescribesTheFullInputOutputGraph()
    {
        var result = Run(Communications);
        var spec = ExtractSpec(result);

        Assert.Equal("Communications", spec.GetProperty("title").GetString());

        var inputs = spec.GetProperty("inputs");
        Assert.Equal(1, inputs.GetArrayLength());
        Assert.Equal("start", inputs[0].GetProperty("name").GetString());
        Assert.Equal("Start Date", inputs[0].GetProperty("label").GetString());
        Assert.Equal("all", inputs[0].GetProperty("type").GetString());
        Assert.Equal(0, inputs[0].GetProperty("duration").GetInt32());

        var outputs = spec.GetProperty("outputs");
        Assert.Equal(2, outputs.GetArrayLength());

        Assert.Equal("first", outputs[0].GetProperty("name").GetString());
        Assert.Equal("First Email", outputs[0].GetProperty("label").GetString());
        Assert.Equal("all", outputs[0].GetProperty("type").GetString());
        Assert.Equal("start", outputs[0].GetProperty("ref").GetString());
        Assert.Equal(1, outputs[0].GetProperty("direction").GetInt32());
        Assert.Equal(1, outputs[0].GetProperty("count").GetInt32());
        Assert.Equal(0, outputs[0].GetProperty("duration").GetInt32());

        Assert.Equal("second", outputs[1].GetProperty("name").GetString());
        Assert.Equal("weekdays", outputs[1].GetProperty("type").GetString());
        Assert.Equal("first", outputs[1].GetProperty("ref").GetString());
    }

    [Fact]
    public void Duration_ParsesOnBothInputsAndOutputs()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                type: weekdays
                duration: 3
                default: 01/01/2027
            outputs:
              - name: sprint
                duration: 10
                expr: start + 1
            ```
            """;
        var spec = ExtractSpec(Run(md));

        var input = spec.GetProperty("inputs")[0];
        Assert.Equal("weekdays", input.GetProperty("type").GetString());
        Assert.Equal(3, input.GetProperty("duration").GetInt32());

        var output = spec.GetProperty("outputs")[0];
        Assert.Equal(10, output.GetProperty("duration").GetInt32());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void InvalidDuration_FallsBackToZero(string rawDuration)
    {
        var md = $$"""
            ```timeline
            inputs:
              - name: start
                duration: {{rawDuration}}
                default: 01/01/2027
            ```
            """;
        var spec = ExtractSpec(Run(md));
        Assert.Equal(0, spec.GetProperty("inputs")[0].GetProperty("duration").GetInt32());
    }

    [Fact]
    public void OmittedDuration_DefaultsToZero()
    {
        var spec = ExtractSpec(Run(Communications));
        Assert.Equal(0, spec.GetProperty("inputs")[0].GetProperty("duration").GetInt32());
        Assert.Equal(0, spec.GetProperty("outputs")[0].GetProperty("duration").GetInt32());
    }

    [Fact]
    public void ClientEvaluator_ChainsOffReferencedItemsEndDate_NotItsStart()
    {
        // The actual date math lives in BinderJs and is verified end-to-end in the browser
        // (see conversation record) - this just locks in that the generated evaluator computes
        // and keys off an "end" date per item (start + duration) rather than a single point,
        // so a regression collapsing it back to start-only doesn't slip through silently.
        var ctx = new FakeContext();
        Run(Communications, out _, ctx);
        var js = ctx.InlineScripts[0];

        Assert.Contains("endIso", js);
        Assert.Contains("var refEnd = ends[out.ref];", js);
        // No emitted `section` line (distinct from the word "section" appearing in comments).
        Assert.DoesNotContain("lines.push(\"    section", js);
    }

    [Fact]
    public void NegativeOffset_SetsDirectionToMinusOne()
    {
        var md = """
            ```timeline
            inputs:
              - name: due
                default: 06/15/2027
            outputs:
              - name: reminder
                expr: due - 3
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var output = spec.GetProperty("outputs")[0];
        Assert.Equal(-1, output.GetProperty("direction").GetInt32());
        Assert.Equal(3, output.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ZeroOffset_ExprWithoutOperator_HasCountZero()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: same
                expr: start
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var output = spec.GetProperty("outputs")[0];
        Assert.Equal(1, output.GetProperty("direction").GetInt32());
        Assert.Equal(0, output.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Exclusions_AppearAsSortedDeduplicatedIsoArray()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            exclusions:
              - 12/25/2027
              - { from: 12/24/2027, to: 12/26/2027 }
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var exclusions = spec.GetProperty("exclusions").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(new[] { "2027-12-24", "2027-12-25", "2027-12-26" }, exclusions);
    }

    [Fact]
    public void CustomDateFormat_IsoStyle_ProducesIsoInputValue()
    {
        var md = """
            ```timeline
            date_format: YYYY-MM-DD
            inputs:
              - name: start
                default: 2027-06-15
            ```
            """;
        var result = Run(md);
        Assert.Contains("value=\"2027-06-15\"", result);
    }

    [Fact]
    public void DuplicateInputName_SecondIsSkipped()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
              - name: start
                default: 02/01/2027
            ```
            """;
        var spec = ExtractSpec(Run(md));
        Assert.Equal(1, spec.GetProperty("inputs").GetArrayLength());
    }

    [Fact]
    public void OutputName_CollidingWithInput_IsSkipped()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: start
                expr: start + 1
            ```
            """;
        var spec = ExtractSpec(Run(md));
        Assert.Equal(0, spec.GetProperty("outputs").GetArrayLength());
    }

    [Fact]
    public void InvalidExpr_IsDroppedWithWarning()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: bad
                expr: "not an expr!"
              - name: ok
                expr: start + 1
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var names = spec.GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "ok" }, names);
    }

    [Fact]
    public void ExprReferencingUnknownName_IsDropped()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: bad
                expr: nope + 1
              - name: ok
                expr: start + 1
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var names = spec.GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "ok" }, names);
    }

    [Fact]
    public void ForwardReference_IsRejected()
    {
        // "first" references "second", declared *after* it - sequential resolution only.
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: first
                expr: second + 1
              - name: second
                expr: start + 1
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var names = spec.GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "second" }, names);
    }

    [Fact]
    public void InvalidInputName_IsSkipped()
    {
        var md = """
            ```timeline
            inputs:
              - name: "not a var"
                default: 01/01/2027
              - name: ok
                default: 01/02/2027
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var names = spec.GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "ok" }, names);
    }

    [Fact]
    public void UnparseableDefault_InputIsSkipped()
    {
        var md = """
            ```timeline
            inputs:
              - name: bad
                default: not-a-date
              - name: ok
                default: 01/01/2027
            ```
            """;
        var spec = ExtractSpec(Run(md));
        var names = spec.GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "ok" }, names);
    }

    [Fact]
    public void MissingInputs_RendersError()
    {
        var md = """
            ```timeline
            title: Empty
            ```
            """;
        var result = Run(md);
        Assert.Contains("nd-timeline--error", result);
    }

    [Fact]
    public void AllInputsInvalid_RendersError()
    {
        var md = """
            ```timeline
            inputs:
              - name: bad
                default: not-a-date
            ```
            """;
        var result = Run(md);
        Assert.Contains("nd-timeline--error", result);
    }

    [Fact]
    public void InvalidDateFormat_RendersError()
    {
        var md = """
            ```timeline
            date_format: DD/DD
            inputs:
              - name: start
                default: 01/01/2027
            ```
            """;
        var result = Run(md);
        Assert.Contains("nd-timeline--error", result);
    }

    [Fact]
    public void MarkdownWithoutTimelineFence_IsUnchanged()
    {
        var md = "Just a normal paragraph mentioning a timeline.";
        Assert.Equal(md, Run(md));
    }

    [Fact]
    public void TimelineFence_NestedInLargerFence_IsLeftAsSource()
    {
        var md = "````markdown\n" + Communications + "\n````";
        var result = Run(md);

        Assert.DoesNotContain("nd-timeline", result);
        Assert.Contains("```timeline", result);
        Assert.Contains("title: Communications", result);
    }

    [Fact]
    public void LabelWithHtmlSensitiveCharacters_IsEscapedInForm()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                label: "Kickoff <b>&</b>"
                default: 01/01/2027
            ```
            """;
        var result = Run(md);

        Assert.DoesNotContain("<b>", result);
        Assert.Contains("Kickoff &lt;b&gt;&amp;&lt;/b&gt;", result);
    }

    /// <summary>
    /// The offset regex bounds the value to digits but not to a magnitude. An offset too large
    /// for an int must be reported and skipped like any other malformed field — it previously
    /// threw OverflowException, so one typo in one page failed the entire build.
    /// </summary>
    [Fact]
    public void OutputWithOutOfRangeOffset_IsSkippedWithoutThrowing()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: later
                expr: start + 99999999999999
            ```
            """;

        var result = Run(md);
        var spec = ExtractSpec(result);

        Assert.Single(spec.GetProperty("inputs").EnumerateArray());
        Assert.Empty(spec.GetProperty("outputs").EnumerateArray());
    }

    /// <summary>
    /// The client evaluator stops stepping after 100000 iterations, so a larger offset would be
    /// clamped there and rendered as if it were the real date. Rejecting it at build time is the
    /// difference between a warning and a silently wrong timeline.
    /// </summary>
    [Theory]
    [InlineData("100001")]
    [InlineData("2000000000")]
    public void OutputWithOffsetBeyondTheEvaluatorBudget_IsSkipped(string offset)
    {
        var md = $"""
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: later
                expr: start + {offset}
            ```
            """;

        Assert.Empty(ExtractSpec(Run(md)).GetProperty("outputs").EnumerateArray());
    }

    [Fact]
    public void OutputWithLargeButValidOffset_IsKept()
    {
        var md = """
            ```timeline
            inputs:
              - name: start
                default: 01/01/2027
            outputs:
              - name: later
                expr: start + 3650
            ```
            """;

        var spec = ExtractSpec(Run(md));
        var output = spec.GetProperty("outputs").EnumerateArray().Single();

        Assert.Equal(3650, output.GetProperty("count").GetInt32());
    }

    /// <summary>
    /// The info word is compared case-insensitively, so the cheap pre-scan must be too — an
    /// uppercase fence used to fall through and render as a plain code block.
    /// </summary>
    [Theory]
    [InlineData("TIMELINE")]
    [InlineData("Timeline")]
    public void FenceInfoWordIsCaseInsensitive(string infoWord)
    {
        var md = $"""
            ```{infoWord}
            inputs:
              - name: start
                default: 01/01/2027
            ```
            """;

        Assert.Contains("nd-timeline", Run(md));
    }
}
