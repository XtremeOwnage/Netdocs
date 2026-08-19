using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Configuration;

namespace Netdocs.Plugins;

/// <summary>
/// Turns a fenced <c>```timeline</c> block written in YAML into an interactive project
/// timeline: a date picker per named anchor date (unless it declares <c>editable: false</c>,
/// in which case it's a fixed graph root shown as plain text instead), plus a chain of named,
/// offset dates - calendar or business days, with optional exclusion dates for holidays/
/// blackouts - rendered as a Mermaid <c>gantt</c> diagram that recomputes live when a date is
/// changed. Every input and output has a <c>duration</c> (default 0, rendering as a
/// point-in-time milestone; nonzero renders as a task bar), and an <c>expr</c> that references
/// an earlier name chains off that name's computed <em>end</em> date (start + duration) rather
/// than its start - "the next task begins when this one finishes".
/// <para>
/// Like <see cref="CalculatorPlugin"/>, all of the date math happens client-side: the plugin
/// validates the YAML and emits a small HTML form (one <c>&lt;input type="date"&gt;</c> per
/// declared input) plus a JSON spec of the output graph, and a site-wide vanilla-JS evaluator
/// re-derives every output date and re-renders the diagram (via a lazily-imported Mermaid)
/// whenever an input changes. There is no build-time date math and no server round-trip.
/// </para>
/// Runs as a Markdown preprocessor (order 15, alongside <see cref="CalculatorPlugin"/>) so it
/// resolves after snippets/abbreviations have expanded any includes and before table-reader.
/// </summary>
public sealed class TimelinePlugin : IPlugin, IMarkdownPreprocessor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private ILogger? _log;

    public string Name => "timeline";
    public int Order => 15;

    private static readonly Regex ValidName = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // "<ref>" or "<ref> +/- N" - the offset unit is driven by the output's own `type`.
    private static readonly Regex ExprPattern = new(
        @"^\s*(?<ref>[A-Za-z_][A-Za-z0-9_]*)\s*(?:(?<op>[+-])\s*(?<n>\d+)\s*)?$",
        RegexOptions.Compiled);

    public void Configure(IPluginContext ctx)
    {
        _log = ctx.Logger;
        // Registered once for the whole site, mirroring CalculatorPlugin's evaluator: it
        // subscribes to Material's `document$` so it re-binds on instant navigation, and never
        // leaks into a page's rendered markdown/examples.
        ctx.AddInlineScript(BinderJs);
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct) =>
        Task.FromResult(FencedBlocks.Rewrite(markdown, "timeline", body => RenderBlock(body, page)));

    private string RenderBlock(string body, Page page)
    {
        object? tree;
        try
        {
            tree = YamlTree.Parse(body);
        }
        catch (Exception ex)
        {
            _log?.LogWarning("timeline: could not parse timeline block in {Page}: {Message}", page.RelativePath, ex.Message);
            return ErrorBox("Invalid timeline definition (YAML parse error).");
        }

        if (tree is not IReadOnlyDictionary<string, object?> map)
            return ErrorBox("A timeline block must be a YAML mapping with `inputs` and `outputs`.");

        var title = Str(map, "title");

        // Direct top-level fields, not a nested `options` object - `title`/`date_format`/
        // `exclusions`/`show_summary`/`display_date_format`/`edit_exclusions`/`inputs`/`outputs`
        // are all siblings, so a reader doesn't have to learn "some settings are nested, some
        // aren't" as a second rule. YAML keys are snake_case throughout (matching `date_format`
        // and the MkDocs/pymdownx ecosystem conventions this project otherwise mirrors); the
        // JSON spec handed to the client evaluator is a separate wire format and stays camelCase,
        // which is just normal JSON convention - the two aren't the same surface and don't need
        // to match.
        var showSummary = ReadBool(map, "show_summary", true);
        var displayDateFormat = Str(map, "display_date_format") ?? "dddd MMM dd, yyyy";
        var editExclusions = ReadBool(map, "edit_exclusions", false);

        DateFmt fmt;
        try
        {
            fmt = DateFmt.Parse(Str(map, "date_format") ?? "MM/DD/YYYY");
        }
        catch (Exception ex)
        {
            _log?.LogWarning("timeline: invalid date_format in {Page}: {Message}", page.RelativePath, ex.Message);
            return ErrorBox("Invalid `date_format`.");
        }

        var exclusions = ReadExclusions(map, fmt, page);
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var inputFields = new List<(string Name, string Label, string DefaultIso, bool Editable)>();
        var inputSpecs = new List<InputSpec>();
        foreach (var input in ReadInputs(map))
        {
            if (!declaredNames.Add(input.Name))
            {
                _log?.LogWarning("timeline: duplicate name '{Name}' in {Page}; skipping", input.Name, page.RelativePath);
                continue;
            }
            if (!fmt.TryParse(input.Default, out var date))
            {
                _log?.LogWarning("timeline: input '{Name}' has an unparseable default '{Default}' in {Page}; skipping",
                    input.Name, input.Default, page.RelativePath);
                continue;
            }
            var iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var inputWeekdaysOnly = input.Type.Equals("weekdays", StringComparison.OrdinalIgnoreCase);
            inputFields.Add((input.Name, input.Label, iso, input.Editable));
            inputSpecs.Add(new InputSpec(input.Name, input.Label, inputWeekdaysOnly ? "weekdays" : "all", input.Duration));
        }

        var outputSpecs = new List<OutputSpec>();
        foreach (var output in ReadOutputs(map))
        {
            if (declaredNames.Contains(output.Name))
            {
                _log?.LogWarning("timeline: duplicate name '{Name}' in {Page}; skipping", output.Name, page.RelativePath);
                continue;
            }

            var exprMatch = ExprPattern.Match(output.Expr);
            if (!exprMatch.Success)
            {
                _log?.LogWarning("timeline: output '{Name}' has an invalid expr '{Expr}' in {Page}; skipping",
                    output.Name, output.Expr, page.RelativePath);
                continue;
            }

            var refName = exprMatch.Groups["ref"].Value;
            if (!declaredNames.Contains(refName))
            {
                _log?.LogWarning(
                    "timeline: output '{Name}' references unknown/not-yet-defined name '{Ref}' in {Page}; skipping",
                    output.Name, refName, page.RelativePath);
                continue;
            }

            var direction = exprMatch.Groups["op"].Value == "-" ? -1 : 1;
            // The regex bounds the offset to digits but not to a magnitude, so a typo like
            // `start + 99999999999999` would otherwise throw OverflowException and fail the whole
            // build -- every other malformed field here degrades to a warning, so this does too.
            var offset = exprMatch.Groups["n"];
            var count = 0;
            if (offset.Success && !int.TryParse(offset.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                _log?.LogWarning("timeline: output '{Name}' has an out-of-range offset '{Offset}' in {Page}; skipping",
                    output.Name, offset.Value, page.RelativePath);
                continue;
            }
            var weekdaysOnly = output.Type.Equals("weekdays", StringComparison.OrdinalIgnoreCase);

            declaredNames.Add(output.Name);
            outputSpecs.Add(new OutputSpec(output.Name, output.Label, weekdaysOnly ? "weekdays" : "all", refName, direction, count, output.Duration));
        }

        if (inputFields.Count == 0)
            return ErrorBox("A timeline block needs at least one resolvable input.");

        var spec = new TimelineSpec(
            title,
            exclusions.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList(),
            inputSpecs,
            outputSpecs,
            showSummary,
            displayDateFormat,
            editExclusions);

        return RenderForm(title, inputFields, spec);
    }

    // --- reading ---------------------------------------------------------

    private sealed record InputDef(string Name, string Label, string Type, int Duration, string Default, bool Editable);
    private sealed record OutputDef(string Name, string Label, string Type, int Duration, string Expr);

    private List<InputDef> ReadInputs(IReadOnlyDictionary<string, object?> map)
    {
        var list = new List<InputDef>();
        if (!map.TryGetValue("inputs", out var raw) || raw is not IEnumerable<object?> items) return list;

        foreach (var item in items)
        {
            if (item is not IReadOnlyDictionary<string, object?> im) continue;
            var name = Str(im, "name");
            if (string.IsNullOrWhiteSpace(name) || !ValidName.IsMatch(name))
            {
                _log?.LogWarning("timeline: input name '{Name}' is not a valid identifier; skipping", name);
                continue;
            }
            var def = Str(im, "default");
            if (string.IsNullOrWhiteSpace(def))
            {
                _log?.LogWarning("timeline: input '{Name}' has no `default`; skipping", name);
                continue;
            }
            list.Add(new InputDef(
                name,
                Str(im, "label") is { Length: > 0 } l ? l : name,
                Str(im, "type") is { Length: > 0 } t ? t : "all",
                ReadDuration(im, name, "input"),
                def,
                ReadBool(im, "editable", true)));
        }
        return list;
    }

    private List<OutputDef> ReadOutputs(IReadOnlyDictionary<string, object?> map)
    {
        var list = new List<OutputDef>();
        if (!map.TryGetValue("outputs", out var raw) || raw is not IEnumerable<object?> items) return list;

        foreach (var item in items)
        {
            if (item is not IReadOnlyDictionary<string, object?> om) continue;
            var name = Str(om, "name");
            if (string.IsNullOrWhiteSpace(name) || !ValidName.IsMatch(name))
            {
                _log?.LogWarning("timeline: output name '{Name}' is not a valid identifier; skipping", name);
                continue;
            }
            var expr = Str(om, "expr");
            if (string.IsNullOrWhiteSpace(expr))
            {
                _log?.LogWarning("timeline: output '{Name}' has no `expr`; skipping", name);
                continue;
            }
            list.Add(new OutputDef(
                name,
                Str(om, "label") is { Length: > 0 } l ? l : name,
                Str(om, "type") is { Length: > 0 } t ? t : "all",
                ReadDuration(om, name, "output"),
                expr));
        }
        return list;
    }

    /// <summary>Parses `duration` (whole days, non-negative; default/fallback 0).</summary>
    private int ReadDuration(IReadOnlyDictionary<string, object?> map, string ownerName, string kind)
    {
        var raw = Str(map, "duration");
        if (raw is null) return 0;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) || duration < 0)
        {
            _log?.LogWarning("timeline: {Kind} '{Name}' has an invalid duration '{Duration}'; using 0", kind, ownerName, raw);
            return 0;
        }
        return duration;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out var v) || v is null) return fallback;
        return v switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var r) => r,
            _ => fallback,
        };
    }

    private HashSet<DateOnly> ReadExclusions(IReadOnlyDictionary<string, object?> map, DateFmt fmt, Page page)
    {
        var set = new HashSet<DateOnly>();
        if (!map.TryGetValue("exclusions", out var raw) || raw is not IEnumerable<object?> items) return set;

        foreach (var item in items)
        {
            switch (item)
            {
                case IReadOnlyDictionary<string, object?> range:
                    var fromS = Str(range, "from");
                    var toS = Str(range, "to");
                    if (fromS is null || toS is null || !fmt.TryParse(fromS, out var from) || !fmt.TryParse(toS, out var to))
                    {
                        _log?.LogWarning("timeline: invalid exclusion range in {Page}; skipping", page.RelativePath);
                        continue;
                    }
                    if (to < from) (from, to) = (to, from);
                    var span = to.DayNumber - from.DayNumber + 1;
                    if (span > 366)
                    {
                        _log?.LogWarning("timeline: exclusion range {From}..{To} in {Page} exceeds 366 days; capping",
                            from, to, page.RelativePath);
                        span = 366;
                    }
                    for (var n = 0; n < span; n++) set.Add(from.AddDays(n));
                    break;

                case { } scalar:
                    var s = ScalarString(scalar);
                    if (fmt.TryParse(s, out var d)) set.Add(d);
                    else _log?.LogWarning("timeline: could not parse exclusion date '{Value}' in {Page}", s, page.RelativePath);
                    break;
            }
        }
        return set;
    }

    // --- rendering ---------------------------------------------------------

    private sealed record InputSpec(string Name, string Label, string Type, int Duration);
    private sealed record OutputSpec(string Name, string Label, string Type, string Ref, int Direction, int Count, int Duration);
    private sealed record TimelineSpec(
        string? Title,
        IReadOnlyList<string> Exclusions,
        IReadOnlyList<InputSpec> Inputs,
        IReadOnlyList<OutputSpec> Outputs,
        bool ShowSummary,
        string DisplayDateFormat,
        bool EditExclusions);

    private static string RenderForm(string? title, IReadOnlyList<(string Name, string Label, string DefaultIso, bool Editable)> inputFields, TimelineSpec spec)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"nd-timeline\">");
        if (!string.IsNullOrEmpty(title))
            sb.Append("<div class=\"nd-timeline__title\">").Append(Esc(title)).Append("</div>");

        sb.Append("<div class=\"nd-timeline__inputs\">");
        foreach (var field in inputFields)
        {
            if (field.Editable)
            {
                var id = "ndt-" + Guid.NewGuid().ToString("N")[..8];
                sb.Append("<div class=\"nd-timeline__field\">");
                sb.Append("<label for=\"").Append(id).Append("\">").Append(Esc(field.Label)).Append("</label>");
                sb.Append("<input id=\"").Append(id).Append("\" type=\"date\" data-timeline-var=\"").Append(field.Name).Append('"');
                sb.Append(" value=\"").Append(field.DefaultIso).Append("\">");
                sb.Append("</div>");
            }
            else
            {
                // No picker: `editable: false` is still a full graph root (duration works,
                // outputs can reference it via expr) - it just can't be moved by the reader.
                // Plain text rather than a disabled <input type="date">, deliberately: a disabled
                // date input still LOOKS like a control (misleading), only ever displays in the
                // browser's own locale format (unlike everything else here, which honours
                // `display_date_format`), and HTML's `readonly` attribute is known to not
                // reliably block the calendar popup on this input type across browsers anyway.
                // The placeholder text below is the raw ISO date; JS reformats it via
                // `display_date_format` on first compute() (nothing here is build-time-rendered).
                sb.Append("<div class=\"nd-timeline__field nd-timeline__field--static\">");
                sb.Append("<span class=\"nd-timeline__field-label\">").Append(Esc(field.Label)).Append("</span>");
                sb.Append("<span class=\"nd-timeline__static-value\" data-timeline-var=\"").Append(field.Name)
                  .Append("\" data-iso-value=\"").Append(field.DefaultIso).Append("\">")
                  .Append(field.DefaultIso)
                  .Append("</span>");
                sb.Append("</div>");
            }
        }
        sb.Append("</div>");

        // Populated client-side (compute() calls renderExclusions()): a read-only list when
        // there are exclusions but `edit_exclusions` is off, or an editable add/remove UI when
        // it's on. Left empty (no heading, nothing shown) when there's nothing to say -
        // no exclusions and editing isn't enabled.
        sb.Append("<div class=\"nd-timeline__exclusions\"></div>");

        sb.Append("<div class=\"nd-timeline__diagram\" aria-live=\"polite\"></div>");
        // Populated client-side alongside the diagram. Exists as an always-visible fallback for
        // exact dates once the axis granularity (weeks/months on a longer project) makes them
        // hard to read off the chart - and works on touch/print/screen readers, unlike a tooltip.
        sb.Append("<div class=\"nd-timeline__dates\" aria-live=\"polite\"></div>");
        sb.Append("<script type=\"application/json\" class=\"nd-timeline-spec\">")
          .Append(JsonSerializer.Serialize(spec, JsonOptions))
          .Append("</script>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string ErrorBox(string message) =>
        "<div class=\"nd-timeline nd-timeline--error\">" + Esc(message) + "</div>";

    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string? Str(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) && v is not null ? ScalarString(v) : null;

    private static string ScalarString(object v) => v switch
    {
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    /// <summary>
    /// A tiny, date-only format mini-language using JS/moment-style tokens (<c>YYYY</c>,
    /// <c>YY</c>, <c>MM</c>, <c>M</c>, <c>DD</c>, <c>D</c>, case-insensitive) rather than .NET's
    /// custom date format strings, so authors don't have to know that .NET's lowercase
    /// <c>mm</c> means minutes. Any other character is treated as a literal separator. Used
    /// only to parse `default`/`exclusions` values authored in the YAML - everything computed
    /// afterwards (client-side) works in plain ISO <c>yyyy-MM-dd</c>, which is what
    /// <c>&lt;input type="date"&gt;</c> natively speaks.
    /// </summary>
    private sealed class DateFmt
    {
        private readonly Regex _regex;

        private DateFmt(Regex regex) => _regex = regex;

        public static DateFmt Parse(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("date_format cannot be empty");

            var pattern = new StringBuilder("^");
            var seen = new HashSet<char>();
            var i = 0;
            while (i < format.Length)
            {
                var c = char.ToUpperInvariant(format[i]);
                if (c is 'Y' or 'M' or 'D')
                {
                    var run = 1;
                    while (i + run < format.Length && char.ToUpperInvariant(format[i + run]) == c) run++;
                    if (!seen.Add(c))
                        throw new ArgumentException($"date_format repeats token '{c}'");
                    pattern.Append(c switch
                    {
                        'Y' => run >= 4 ? "(?<Y>\\d{4})" : "(?<Y>\\d{1,2})",
                        'M' => "(?<M>\\d{1,2})",
                        _ => "(?<D>\\d{1,2})",
                    });
                    i += run;
                }
                else
                {
                    pattern.Append(Regex.Escape(format[i].ToString()));
                    i++;
                }
            }
            if (!seen.Contains('Y') || !seen.Contains('M') || !seen.Contains('D'))
                throw new ArgumentException("date_format must contain year, month and day tokens");

            pattern.Append('$');
            return new DateFmt(new Regex(pattern.ToString()));
        }

        public bool TryParse(string? value, out DateOnly date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var m = _regex.Match(value.Trim());
            if (!m.Success) return false;

            try
            {
                var year = int.Parse(m.Groups["Y"].Value, CultureInfo.InvariantCulture);
                if (year < 100) year += 2000;
                var month = int.Parse(m.Groups["M"].Value, CultureInfo.InvariantCulture);
                var day = int.Parse(m.Groups["D"].Value, CultureInfo.InvariantCulture);
                date = new DateOnly(year, month, day);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }

    // Client-side evaluator, registered once for the whole site (mirrors CalculatorPlugin's
    // EvaluatorJs). Re-derives every output date from the current input values and rebuilds +
    // re-renders the Mermaid gantt diagram on every change - no server round-trip. Date
    // arithmetic is done via a pure day-count (civil calendar) algorithm rather than JS `Date`,
    // which deliberately avoids that type's well-known timezone-shift footguns (a `Date`
    // constructed from a bare "YYYY-MM-DD" string parses as UTC midnight, but its getters read
    // back in local time - `<input type="date">`'s `.value` is always plain ISO with no
    // timezone at all, and this keeps it that way end to end).
    private const string BinderJs = """
        (function () {
          function daysFromCivil(y, m, d) {
            y -= m <= 2 ? 1 : 0;
            var era = Math.floor((y >= 0 ? y : y - 399) / 400);
            var yoe = y - era * 400;
            var doy = Math.floor((153 * (m + (m > 2 ? -3 : 9)) + 2) / 5) + d - 1;
            var doe = yoe * 365 + Math.floor(yoe / 4) - Math.floor(yoe / 100) + doy;
            return era * 146097 + doe - 719468;
          }
          function civilFromDays(z) {
            z += 719468;
            var era = Math.floor((z >= 0 ? z : z - 146096) / 146097);
            var doe = z - era * 146097;
            var yoe = Math.floor((doe - Math.floor(doe / 1460) + Math.floor(doe / 36524) - Math.floor(doe / 146096)) / 365);
            var y = yoe + era * 400;
            var doy = doe - (365 * yoe + Math.floor(yoe / 4) - Math.floor(yoe / 100));
            var mp = Math.floor((5 * doy + 2) / 153);
            var d = doy - Math.floor((153 * mp + 2) / 5) + 1;
            var m = mp + (mp < 10 ? 3 : -9);
            y += m <= 2 ? 1 : 0;
            return [y, m, d];
          }
          function isoFromDays(days) {
            var c = civilFromDays(days);
            return String(c[0]).padStart(4, "0") + "-" + String(c[1]).padStart(2, "0") + "-" + String(c[2]).padStart(2, "0");
          }
          function daysFromIso(iso) {
            var p = iso.split("-").map(Number);
            return daysFromCivil(p[0], p[1], p[2]);
          }
          function dayOfWeek(days) {
            // 1970-01-01 (days=0) was a Thursday; 0=Sunday..6=Saturday, matching Date#getDay().
            return ((days % 7) + 7 + 4) % 7;
          }
          function stepOffset(fromIso, direction, count, weekdaysOnly, exclusionSet) {
            var days = daysFromIso(fromIso);
            var remaining = count;
            var guard = 0;
            while (remaining > 0 && guard++ < 100000) {
              days += direction;
              if (weekdaysOnly) {
                var dow = dayOfWeek(days);
                if (dow === 0 || dow === 6) continue;
              }
              if (exclusionSet.has(isoFromDays(days))) continue;
              remaining--;
            }
            return isoFromDays(days);
          }
          function pickTickInterval(spanDays) {
            if (spanDays <= 14) return "1day";
            if (spanDays <= 90) return "1week";
            if (spanDays <= 730) return "1month";
            return "3month";
          }
          function sanitizeLine(s) {
            return String(s).replace(/[\r\n]+/g, " ").replace(/\s+/g, " ").trim()
              .replace(/`/g, "'").replace(/"/g, "'").replace(/:/g, "-").replace(/,/g, ";");
          }
          function buildMermaidSource(events) {
            var lines = [];
            lines.push("%%{init: {'gantt': {'fontSize': 16, 'sectionFontSize': 14}}}%%");
            lines.push("gantt");
            lines.push("    dateFormat YYYY-MM-DD");
            lines.push("    axisFormat %b %d");
            var span = 0;
            if (events.length > 0) {
              var allDays = [];
              events.forEach(function (e) {
                allDays.push(daysFromIso(e.startIso), daysFromIso(e.endIso));
              });
              span = Math.max.apply(null, allDays) - Math.min.apply(null, allDays);
            }
            lines.push("    tickInterval " + pickTickInterval(span));
            // No `section` line: with only ever one (unnamed) section, Mermaid still centres its
            // label across the full height of the chart - which for a title reused as the section
            // name just means the title floating alone in the middle of the diagram, duplicating
            // the static heading already shown above the diagram. Omitting the section directive
            // entirely is valid Mermaid gantt syntax (confirmed via mermaid.parse()) and leaves
            // that label blank instead.
            events.forEach(function (e) {
              var label = sanitizeLine(e.label);
              if (e.startIso === e.endIso) {
                // Zero duration: a point-in-time milestone (diamond marker).
                lines.push("    " + label + " : milestone, " + e.name + ", " + e.startIso + ", 0d");
              } else {
                // Real duration: a task bar. Passed as an explicit end date rather than a `Nd`
                // day-count, since the *visual* span must reach the duration's true end date -
                // which, once weekday/exclusion skipping is involved, can cover more calendar
                // days than the nominal duration (e.g. a 10-weekday sprint spans ~14 calendar
                // days) - and Mermaid's own `Nd` duration has no such awareness.
                lines.push("    " + label + " : " + e.name + ", " + e.startIso + ", " + e.endIso);
              }
            });
            return lines.join("\n");
          }

          var WEEKDAY_FULL = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
          var WEEKDAY_ABBR = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
          var MONTH_FULL = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
          var MONTH_ABBR = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
          function pad(n, len) {
            var s = String(n);
            while (s.length < len) s = "0" + s;
            return s;
          }
          // A small .NET-custom-date-format-style token language (`display_date_format`), since
          // it's display-only - no relation to the moment-style tokens `date_format` uses to
          // *parse* authored dates. Runs of the same letter are one token; case/repeat-count
          // follow .NET conventions: d/dd = day-of-month (optionally zero-padded), ddd/dddd =
          // weekday name (abbreviated/full); same pattern for M (month) and y (year). Anything
          // else, including an unrecognised letter run, passes through literally.
          function formatDisplayDate(iso, pattern) {
            var p = iso.split("-").map(Number);
            var year = p[0], month = p[1], day = p[2];
            var dow = dayOfWeek(daysFromIso(iso));
            var out = "", i = 0;
            while (i < pattern.length) {
              var c = pattern[i];
              if (/[a-zA-Z]/.test(c)) {
                var run = 1;
                while (i + run < pattern.length && pattern[i + run] === c) run++;
                if (c === "y") out += run >= 4 ? pad(year, 4) : pad(year % 100, 2);
                else if (c === "M") out += run >= 4 ? MONTH_FULL[month - 1] : run === 3 ? MONTH_ABBR[month - 1] : pad(month, run);
                else if (c === "d") out += run >= 4 ? WEEKDAY_FULL[dow] : run === 3 ? WEEKDAY_ABBR[dow] : pad(day, run);
                else out += pattern.substr(i, run);
                i += run;
              } else {
                out += c;
                i++;
              }
            }
            return out;
          }
          function escapeHtml(s) {
            return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
          }
          // Always-visible fallback for exact dates - the axis alone becomes hard to read once
          // tickInterval coarsens to weeks/months on a longer project. Rendered synchronously
          // (unlike the diagram, which waits on the lazy Mermaid import) so this shows immediately.
          function renderDatesTable(container, events, displayDateFormat) {
            var rows = events.map(function (e) {
              var range = e.startIso === e.endIso
                ? formatDisplayDate(e.startIso, displayDateFormat)
                : formatDisplayDate(e.startIso, displayDateFormat) + " – " + formatDisplayDate(e.endIso, displayDateFormat);
              return "<tr><th scope=\"row\">" + escapeHtml(e.label) + "</th><td>" + escapeHtml(range) + "</td></tr>";
            }).join("");
            container.innerHTML = rows ? "<table class=\"nd-timeline__dates-table\"><tbody>" + rows + "</tbody></table>" : "";
          }
          // `edit_exclusions` gates the add/remove controls; a read-only list still shows
          // whenever there *are* exclusions (nothing shows only when both empty and not editable).
          // Exclusions are mutable client state (block.__ndTimelineExclusions, seeded from the
          // spec once at bind time) since the reader can add/remove them here.
          function renderExclusions(block, spec) {
            var container = block.querySelector(".nd-timeline__exclusions");
            // Re-sorted from the (unordered) Set on every render, so a newly added date always
            // lands in chronological order rather than appended at the end. Plain .sort() is
            // exactly a chronological sort here: every element is a fixed-width, zero-padded
            // "yyyy-MM-dd" string, so lexicographic string order and date order coincide.
            var excl = Array.from(block.__ndTimelineExclusions).sort();
            if (!spec.editExclusions && excl.length === 0) {
              container.innerHTML = "";
              return;
            }
            var html = "<div class=\"nd-timeline__exclusions-title\">Excluded Dates</div>";
            html += "<ul class=\"nd-timeline__exclusions-list\">";
            excl.forEach(function (iso) {
              html += "<li>" + escapeHtml(formatDisplayDate(iso, spec.displayDateFormat));
              if (spec.editExclusions) {
                html += " <button type=\"button\" class=\"nd-timeline__exclusion-remove\" data-exclusion=\""
                  + escapeHtml(iso) + "\" aria-label=\"Remove excluded date " + escapeHtml(iso) + "\">&times;</button>";
              }
              html += "</li>";
            });
            if (excl.length === 0) html += "<li class=\"nd-timeline__exclusions-empty\">None</li>";
            html += "</ul>";
            if (spec.editExclusions) {
              // What the picker opens to - and, since a native date input has no way to hint an
              // opening date separate from its actual value, what it also visibly shows as
              // already filled in. This is literally "whatever was last typed/selected here" -
              // block.__ndTimelineExclusionDraft just isn't cleared after a successful add, so it
              // stays put across the rebuild (the DOM input itself doesn't survive - the whole
              // block is rebuilt on every compute() - so persisting the *value* is the only way
              // to keep it). Resolved to the project's first input date - not today's date, which
              // is usually irrelevant to whatever period is actually being planned - only once,
              // the first time this render ever runs.
              if (block.__ndTimelineExclusionDraft === null) {
                var firstInput = block.querySelector("[data-timeline-var]");
                // The first input may be editable: false (a <span>, no .value property at all)
                // rather than an <input> - same "value" in el fallback as compute()'s read loop.
                block.__ndTimelineExclusionDraft = firstInput ? ("value" in firstInput ? firstInput.value : firstInput.getAttribute("data-iso-value")) : "";
              }
              html += "<div class=\"nd-timeline__exclusions-add\">"
                + "<input type=\"date\" class=\"nd-timeline__exclusion-input\" aria-label=\"Add excluded date\""
                + " value=\"" + escapeHtml(block.__ndTimelineExclusionDraft) + "\">"
                + "<button type=\"button\" class=\"nd-timeline__exclusion-add-btn\">Add</button>"
                + "</div>";
            }
            container.innerHTML = html;

            if (!spec.editExclusions) return;
            container.querySelectorAll(".nd-timeline__exclusion-remove").forEach(function (btn) {
              btn.addEventListener("click", function () {
                block.__ndTimelineExclusions.delete(btn.getAttribute("data-exclusion"));
                compute(block);
              });
            });
            var addInput = container.querySelector(".nd-timeline__exclusion-input");
            var addExclusion = function () {
              var value = addInput.value;
              if (!value) return;
              // Set.add() is already idempotent (no way to get a literal duplicate in the
              // underlying data), but silently no-op-ing on a repeat entry reads as broken -
              // surface it via the input's own native validation bubble instead.
              if (block.__ndTimelineExclusions.has(value)) {
                addInput.setCustomValidity("That date is already excluded.");
                addInput.reportValidity();
                return;
              }
              addInput.setCustomValidity("");
              block.__ndTimelineExclusions.add(value);
              block.__ndTimelineExclusionDraft = value; // don't clear it - see the comment above.
              compute(block); // rebuilds this list sorted - see the .sort() above.
            };
            // Deliberate actions only (button click, or Enter to submit like a search box) -
            // not the input's own `change` event, which (for type="date") fires the instant a
            // complete date is entered, before the reader ever reaches the Add button. Binding
            // to it meant every entry silently self-submitted while still mid-edit, tearing down
            // and rebuilding this whole list (via compute() -> renderExclusions()) out from
            // under the very input the reader was still interacting with.
            container.querySelector(".nd-timeline__exclusion-add-btn").addEventListener("click", addExclusion);
            addInput.addEventListener("keydown", function (evt) {
              if (evt.key === "Enter") { evt.preventDefault(); addExclusion(); }
            });
            addInput.addEventListener("input", function () { addInput.setCustomValidity(""); });
          }

          var mermaidPromise = null;
          function loadMermaid() {
            if (!mermaidPromise) {
              mermaidPromise = import("https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs").then(function (m) {
                m.default.initialize({ startOnLoad: false });
                return m.default;
              });
            }
            return mermaidPromise;
          }
          var renderSeq = 0;

          function compute(block) {
            var spec = block.__ndTimelineSpec;
            renderExclusions(block, spec);
            // `editable: false` inputs render as a <span data-iso-value> instead of an
            // <input type="date"> (see RenderForm), so its display text - unlike a native date
            // input, which only ever shows the browser's own locale format - honours
            // `display_date_format` like everything else here. Refreshed every compute(), not
            // just once, in case display_date_format-dependent state ever needs to resync.
            block.querySelectorAll(".nd-timeline__static-value").forEach(function (el) {
              el.textContent = formatDisplayDate(el.getAttribute("data-iso-value"), spec.displayDateFormat);
            });
            // Mutable: `edit_exclusions` lets the reader add/remove from this set, so it's
            // not simply `new Set(spec.exclusions)` - see bind() for where it's seeded.
            var exclusionSet = block.__ndTimelineExclusions;
            // Every input/output has a start and an end (start stepped forward by its own
            // `duration`, honouring its own weekday/exclusion rules - the same stepOffset used
            // for `expr` offsets). A later `expr` chains off the referenced item's *end*, not its
            // start, so "next task starts after this one finishes" is the natural way to write it.
            var ends = {};
            var events = [];

            // Reads both <input type="date"> (editable) and <span data-iso-value> (editable:
            // false) uniformly: "value" in el is true only for real form elements, so a plain
            // span falls through to its data-iso-value attribute instead.
            var values = {};
            block.querySelectorAll("[data-timeline-var]").forEach(function (el) {
              values[el.getAttribute("data-timeline-var")] = "value" in el ? el.value : el.getAttribute("data-iso-value");
            });

            spec.inputs.forEach(function (inp) {
              var start = values[inp.name];
              if (!start) return;
              var end = stepOffset(start, 1, inp.duration, inp.type === "weekdays", exclusionSet);
              ends[inp.name] = end;
              events.push({ name: inp.name, label: inp.label, startIso: start, endIso: end });
            });
            spec.outputs.forEach(function (out) {
              var refEnd = ends[out.ref];
              if (!refEnd) return;
              var start = stepOffset(refEnd, out.direction, out.count, out.type === "weekdays", exclusionSet);
              var end = stepOffset(start, 1, out.duration, out.type === "weekdays", exclusionSet);
              ends[out.name] = end;
              events.push({ name: out.name, label: out.label, startIso: start, endIso: end });
            });

            if (spec.showSummary) {
              // A copy, sorted chronologically by start date - inputs-then-outputs declaration
              // order (which `events` is in) reads fine for the diagram's row order, grouping
              // each track's bars together, but a reader scanning the summary for "what's next"
              // wants it in actual date order regardless of which input/output declared it.
              // Stable sort (guaranteed since ES2019) keeps same-start-date ties in declaration
              // order rather than shuffling them.
              var summaryEvents = events.slice().sort(function (a, b) {
                return a.startIso < b.startIso ? -1 : a.startIso > b.startIso ? 1 : 0;
              });
              renderDatesTable(block.querySelector(".nd-timeline__dates"), summaryEvents, spec.displayDateFormat);
            } else {
              block.querySelector(".nd-timeline__dates").innerHTML = "";
            }

            var gen = (block.__ndTimelineGen = (block.__ndTimelineGen || 0) + 1);
            var src = buildMermaidSource(events);
            var diagram = block.querySelector(".nd-timeline__diagram");
            loadMermaid().then(function (mermaid) {
              if (block.__ndTimelineGen !== gen) return; // superseded by a newer edit
              var id = "nd-timeline-" + renderSeq++;
              return mermaid.render(id, src).then(function (result) {
                if (block.__ndTimelineGen !== gen) return;
                diagram.innerHTML = result.svg;
              });
            }).catch(function () {
              if (block.__ndTimelineGen === gen) diagram.textContent = "Could not render diagram.";
            });
          }

          function bind(block) {
            if (block.__ndTimelineBound) return;
            var specEl = block.querySelector("script.nd-timeline-spec");
            if (!specEl) return;
            try { block.__ndTimelineSpec = JSON.parse(specEl.textContent); } catch (e) { return; }
            block.__ndTimelineExclusions = new Set(block.__ndTimelineSpec.exclusions);
            block.__ndTimelineExclusionDraft = null; // resolved to the first input's date on first render
            block.__ndTimelineBound = true;

            // Scoped to actual <input> elements: an editable: false field's <span> has nothing
            // to listen for (it never changes on its own), unlike the read loop above, which
            // has to handle both kinds.
            block.querySelectorAll("input[data-timeline-var]").forEach(function (input) {
              input.addEventListener("input", function () { compute(block); });
              input.addEventListener("change", function () { compute(block); });
            });
            compute(block);
          }

          function bindAll() { document.querySelectorAll(".nd-timeline").forEach(bind); }
          if (window.document$ && typeof window.document$.subscribe === "function") {
            window.document$.subscribe(bindAll);
          } else if (document.readyState !== "loading") {
            bindAll();
          } else {
            document.addEventListener("DOMContentLoaded", bindAll);
          }
        })();
        """;
}
