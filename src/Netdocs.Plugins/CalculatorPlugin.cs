using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Configuration;

namespace Netdocs.Plugins;

/// <summary>
/// Turns a fenced <c>```calc</c> block written in YAML into an interactive, client-side
/// calculator: labelled input fields plus live-computed outputs, all authored in Markdown.
/// <para>
/// The block declares <c>inputs</c> (numbers, ranges or selects) and <c>outputs</c>
/// (a <c>label</c> plus an <c>expr</c> referencing the input names). A small vanilla-JS
/// evaluator recomputes every output whenever an input changes — no server, no build-time
/// math. Expressions are sanitised to a safe character set and evaluated with the inputs as
/// named variables (the JS <c>Math</c> object is available).
/// </para>
/// Runs as a Markdown preprocessor (order 15) and replaces each fence with a raw-HTML form
/// that Markdig passes through untouched.
/// </summary>
public sealed class CalculatorPlugin : IPlugin, IMarkdownPreprocessor
{
    private ILogger? _log;

    public string Name => "calculator";

    // After snippets (10) so an included snippet can carry a calc block; before table-reader (20).
    public int Order => 15;

    // Matches the opening line of a fenced code block: optional indent, a run of 3+ backticks or
    // tildes, then the info word. Trailing attributes (e.g. ```json title="x", ```python {.foo})
    // are allowed after the info token so fence tracking stays in sync with such blocks. `calc`
    // blocks are rendered; every other fence is copied through verbatim so a ```calc shown *inside*
    // a larger ```` example fence is left untouched.
    private static readonly Regex FenceOpen = new(
        @"^(?<indent>[ \t]{0,3})(?<fence>`{3,}|~{3,})[ \t]*(?<info>[^`\s]*)[^\r\n]*$",
        RegexOptions.Compiled);

    // Characters allowed in an output expression. Deliberately excludes ';', quotes, brackets
    // and backslashes so a stray expression can't inject arbitrary JS into the generated function.
    private static readonly Regex UnsafeExprChars = new(@"[^0-9a-zA-Z_.\+\-\*/%()<>=!?:,&|\s]", RegexOptions.Compiled);

    public void Configure(IPluginContext ctx)
    {
        _log = ctx.Logger;
        // Register the evaluator once for the whole site rather than injecting it into page
        // content. It subscribes to Material's `document$` so it re-binds forms on instant
        // navigation, and it never leaks into a page's rendered markdown/examples.
        ctx.AddInlineScript(EvaluatorJs);
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (markdown.IndexOf("calc", StringComparison.Ordinal) < 0)
            return Task.FromResult(markdown);

        var lines = markdown.Split('\n');
        var sb = new StringBuilder(markdown.Length);
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var open = FenceOpen.Match(lines[i].TrimEnd('\r'));
            if (!open.Success)
            {
                sb.Append(lines[i]);
                if (i < lines.Length - 1) sb.Append('\n');
                continue;
            }

            var fence = open.Groups["fence"].Value;
            var marker = fence[0];
            // Closing fence: same character, at least as long, nothing but the marker on the line.
            int end = i + 1;
            for (; end < lines.Length; end++)
            {
                var t = lines[end].Trim().TrimEnd('\r');
                if (t.Length >= fence.Length && t.All(c => c == marker)) break;
            }

            if (open.Groups["info"].Value.Equals("calc", StringComparison.OrdinalIgnoreCase))
            {
                // Our block: render body (lines between the fences) to a standalone HTML island.
                var body = end > i + 1 ? string.Join("\n", lines, i + 1, end - i - 1) : "";
                sb.Append('\n').Append(RenderBlock(body, page)).Append('\n');
                changed = true;
            }
            else
            {
                // Some other fence (e.g. a ```` markdown example that itself contains ```calc):
                // copy it through unchanged, including its closing line.
                for (var k = i; k <= end && k < lines.Length; k++)
                {
                    sb.Append(lines[k]);
                    if (k < lines.Length - 1) sb.Append('\n');
                }
            }
            i = end; // resume after the closing fence line
        }

        return Task.FromResult(changed ? sb.ToString() : markdown);
    }

    private string RenderBlock(string body, Page page)
    {
        object? tree;
        try
        {
            tree = YamlTree.Parse(body);
        }
        catch (Exception ex)
        {
            _log?.LogWarning("calculator: could not parse calc block in {Page}: {Message}", page.RelativePath, ex.Message);
            return ErrorBox("Invalid calculator definition (YAML parse error).");
        }

        if (tree is not IReadOnlyDictionary<string, object?> map)
            return ErrorBox("A calc block must be a YAML mapping with `inputs` and `outputs`.");

        var title = Str(map, "title");
        var inputs = ReadInputs(map);
        var outputs = ReadOutputs(map);

        if (inputs.Count == 0 || outputs.Count == 0)
            return ErrorBox("A calc block needs at least one input and one output.");

        var sb = new StringBuilder();
        sb.Append("<form class=\"nd-calc\" onsubmit=\"return false\">");
        if (!string.IsNullOrEmpty(title))
            sb.Append("<div class=\"nd-calc__title\">").Append(Esc(title)).Append("</div>");

        sb.Append("<div class=\"nd-calc__inputs\">");
        foreach (var input in inputs)
        {
            var id = "ndc-" + Guid.NewGuid().ToString("N")[..8];
            sb.Append("<div class=\"nd-calc__field\">");
            sb.Append("<label for=\"").Append(id).Append("\">").Append(Esc(input.Label)).Append("</label>");
            if (input.Options.Count > 0)
            {
                sb.Append("<select id=\"").Append(id).Append("\" data-calc-var=\"").Append(Esc(input.Name)).Append("\">");
                foreach (var opt in input.Options)
                {
                    sb.Append("<option value=\"").Append(Esc(opt.Value)).Append('"');
                    if (opt.Value == input.Default) sb.Append(" selected");
                    sb.Append('>').Append(Esc(opt.Label)).Append("</option>");
                }
                sb.Append("</select>");
            }
            else
            {
                sb.Append("<input id=\"").Append(id).Append("\" type=\"").Append(input.Type).Append('"');
                sb.Append(" data-calc-var=\"").Append(Esc(input.Name)).Append('"');
                if (input.Min is not null) sb.Append(" min=\"").Append(Num(input.Min.Value)).Append('"');
                if (input.Max is not null) sb.Append(" max=\"").Append(Num(input.Max.Value)).Append('"');
                if (input.Step is not null) sb.Append(" step=\"").Append(Num(input.Step.Value)).Append('"');
                sb.Append(" value=\"").Append(Esc(input.Default)).Append("\">");
                if (input.Type == "range")
                    sb.Append("<output class=\"nd-calc__range\" data-calc-mirror=\"").Append(Esc(input.Name)).Append("\"></output>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");

        sb.Append("<div class=\"nd-calc__outputs\">");
        foreach (var output in outputs)
        {
            sb.Append("<div class=\"nd-calc__result\">");
            sb.Append("<span class=\"nd-calc__label\">").Append(Esc(output.Label)).Append("</span>");
            sb.Append("<span class=\"nd-calc__value\" data-calc-expr=\"").Append(Esc(output.Expr)).Append('"');
            if (!string.IsNullOrEmpty(output.Format))
                sb.Append(" data-calc-format=\"").Append(Esc(output.Format!)).Append('"');
            sb.Append("></span>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
        sb.Append("</form>");

        return sb.ToString();
    }

    private sealed record InputDef(string Name, string Label, string Type, string Default,
        double? Min, double? Max, double? Step, IReadOnlyList<OptionDef> Options);

    private sealed record OptionDef(string Value, string Label);

    private sealed record OutputDef(string Label, string Expr, string? Format);

    private List<InputDef> ReadInputs(IReadOnlyDictionary<string, object?> map)
    {
        var list = new List<InputDef>();
        if (!map.TryGetValue("inputs", out var raw) || raw is not IEnumerable<object?> items) return list;

        foreach (var item in items)
        {
            if (item is not IReadOnlyDictionary<string, object?> im) continue;
            var name = Str(im, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                _log?.LogWarning("calculator: input name '{Name}' is not a valid identifier; skipping", name);
                continue;
            }

            var label = Str(im, "label") is { Length: > 0 } l ? l : name;
            var options = ReadOptions(im);
            var type = Str(im, "type") switch
            {
                "range" => "range",
                "select" => "select",
                _ => options.Count > 0 ? "select" : "number",
            };

            list.Add(new InputDef(
                name, label, type,
                Str(im, "default") ?? (options.Count > 0 ? options[0].Value : "0"),
                Dbl(im, "min"), Dbl(im, "max"), Dbl(im, "step"), options));
        }
        return list;
    }

    private static List<OptionDef> ReadOptions(IReadOnlyDictionary<string, object?> map)
    {
        var opts = new List<OptionDef>();
        if (!map.TryGetValue("options", out var raw) || raw is not IEnumerable<object?> items) return opts;
        foreach (var item in items)
        {
            switch (item)
            {
                case IReadOnlyDictionary<string, object?> om:
                    var v = Str(om, "value") ?? Str(om, "label");
                    if (v is null) break;
                    opts.Add(new OptionDef(v, Str(om, "label") ?? v));
                    break;
                case { } scalar:
                    var s = ScalarString(scalar);
                    opts.Add(new OptionDef(s, s));
                    break;
            }
        }
        return opts;
    }

    private List<OutputDef> ReadOutputs(IReadOnlyDictionary<string, object?> map)
    {
        var list = new List<OutputDef>();
        if (!map.TryGetValue("outputs", out var raw) || raw is not IEnumerable<object?> items) return list;

        foreach (var item in items)
        {
            if (item is not IReadOnlyDictionary<string, object?> om) continue;
            var expr = Str(om, "expr");
            if (string.IsNullOrWhiteSpace(expr)) continue;
            if (UnsafeExprChars.IsMatch(expr))
            {
                _log?.LogWarning("calculator: output expression '{Expr}' contains unsupported characters; skipping", expr);
                continue;
            }
            var label = Str(om, "label") is { Length: > 0 } l ? l : "Result";
            list.Add(new OutputDef(label, expr!.Trim(), Str(om, "format")));
        }
        return list;
    }

    private static string ErrorBox(string message) =>
        "<div class=\"nd-calc nd-calc--error\">" + Esc(message) + "</div>";

    private static string? Str(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) && v is not null ? ScalarString(v) : null;

    private static double? Dbl(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var v) || v is null) return null;
        return double.TryParse(ScalarString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string ScalarString(object v) => v switch
    {
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);

    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    // Vanilla-JS evaluator, registered once for the whole site via AddInlineScript. Collects each
    // form's inputs into a scope, builds a Function from the sanitised expression with the input
    // names as parameters, and recomputes on input. Binds on Material's `document$` so instant
    // navigation keeps working; falls back to DOMContentLoaded when Material isn't present.
    private const string EvaluatorJs = """
(function () {
  function fmt(value, spec) {
    if (!isFinite(value)) return "—";
    if (!spec) return (Number.isInteger(value) ? value : value.toFixed(2)).toString();
    return spec.replace(/0(?:\.0+)?/, function (tok) {
      var dot = tok.indexOf(".");
      var dec = dot < 0 ? 0 : tok.length - dot - 1;
      return value.toFixed(dec);
    });
  }
  function bind(form) {
    if (form.__ndCalcBound) return;
    form.__ndCalcBound = true;
    var inputs = Array.prototype.slice.call(form.querySelectorAll("[data-calc-var]"));
    var outputs = Array.prototype.slice.call(form.querySelectorAll("[data-calc-expr]"));
    var mirrors = Array.prototype.slice.call(form.querySelectorAll("[data-calc-mirror]"));
    function compute() {
      var names = inputs.map(function (i) { return i.getAttribute("data-calc-var"); });
      var values = inputs.map(function (i) { var n = parseFloat(i.value); return isNaN(n) ? 0 : n; });
      mirrors.forEach(function (mo) {
        var idx = names.indexOf(mo.getAttribute("data-calc-mirror"));
        if (idx >= 0) mo.textContent = values[idx];
      });
      outputs.forEach(function (out) {
        var expr = out.getAttribute("data-calc-expr");
        try {
          var fn = Function.apply(null, names.concat(["return (" + expr + ")"]));
          out.textContent = fmt(fn.apply(null, values), out.getAttribute("data-calc-format"));
        } catch (e) {
          out.textContent = "—";
        }
      });
    }
    inputs.forEach(function (i) { i.addEventListener("input", compute); i.addEventListener("change", compute); });
    compute();
  }
  function bindAll() { document.querySelectorAll("form.nd-calc").forEach(bind); }
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
