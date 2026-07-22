using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class CalculatorPluginTests
{
    private sealed class FakeContext : IPluginContext
    {
        public SiteConfig Config { get; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = new Dictionary<string, object?>();
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }

    private static string Run(string markdown)
    {
        var plugin = new CalculatorPlugin();
        plugin.Configure(new FakeContext());
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        var page = new Page { SourcePath = "x.md", RelativePath = "x.md", RawMarkdown = markdown };
        return plugin.ProcessAsync(page, markdown, site, CancellationToken.None).GetAwaiter().GetResult();
    }

    private const string PowerCalc = """
        ```calc
        title: Power cost
        inputs:
          - name: watts
            label: Power (W)
            default: 100
          - name: hours
            label: Hours/day
            default: 24
        outputs:
          - label: Daily cost
            expr: (watts * hours / 1000) * 0.12
            format: "$0.00"
        ```
        """;

    [Fact]
    public void CalcFence_BecomesForm()
    {
        var result = Run(PowerCalc);

        Assert.Contains("<form class=\"nd-calc\"", result);
        Assert.Contains("<div class=\"nd-calc__title\">Power cost</div>", result);
        Assert.Contains("data-calc-var=\"watts\"", result);
        Assert.Contains("data-calc-var=\"hours\"", result);
        Assert.DoesNotContain("```calc", result);
    }

    [Fact]
    public void Output_CarriesExprAndFormat()
    {
        var result = Run(PowerCalc);

        Assert.Contains("data-calc-expr=\"(watts * hours / 1000) * 0.12\"", result);
        Assert.Contains("data-calc-format=\"$0.00\"", result);
        Assert.Contains("<span class=\"nd-calc__label\">Daily cost</span>", result);
    }

    [Fact]
    public void EvaluatorScript_EmittedOncePerPage()
    {
        var md = PowerCalc + "\n\nSome text.\n\n" + PowerCalc;
        var result = Run(md);

        var scripts = System.Text.RegularExpressions.Regex.Matches(result, "function bindAll");
        Assert.Single(scripts);
        // Two forms though.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(result, "<form class=\"nd-calc\"").Count);
    }

    [Fact]
    public void UnsafeExpression_IsRejected()
    {
        var md = """
            ```calc
            inputs:
              - name: x
                default: 1
            outputs:
              - label: Bad
                expr: "alert('xss'); x"
            ```
            """;
        var result = Run(md);

        // The quote/semicolon/bracket characters are unsupported, so the output is dropped and
        // the block has no outputs -> renders the error box.
        Assert.DoesNotContain("alert(", result);
        Assert.Contains("nd-calc--error", result);
    }

    [Fact]
    public void RangeInput_GetsMirrorOutput()
    {
        var md = """
            ```calc
            inputs:
              - name: pct
                label: Percent
                type: range
                min: 0
                max: 100
                default: 50
                step: 5
            outputs:
              - label: Half
                expr: pct / 2
            ```
            """;
        var result = Run(md);

        Assert.Contains("type=\"range\"", result);
        Assert.Contains("min=\"0\"", result);
        Assert.Contains("max=\"100\"", result);
        Assert.Contains("step=\"5\"", result);
        Assert.Contains("data-calc-mirror=\"pct\"", result);
    }

    [Fact]
    public void SelectInput_RendersOptions()
    {
        var md = """
            ```calc
            inputs:
              - name: mode
                label: Mode
                type: select
                default: b
                options:
                  - { value: a, label: Option A }
                  - { value: b, label: Option B }
            outputs:
              - label: Echo
                expr: mode
            ```
            """;
        var result = Run(md);

        Assert.Contains("<select", result);
        Assert.Contains("value=\"a\"", result);
        Assert.Contains("value=\"b\" selected", result);
        Assert.Contains("Option A", result);
    }

    [Fact]
    public void InvalidInputName_IsSkipped()
    {
        var md = """
            ```calc
            inputs:
              - name: "not a var"
                default: 1
              - name: ok
                default: 2
            outputs:
              - label: R
                expr: ok
            ```
            """;
        var result = Run(md);

        Assert.Contains("data-calc-var=\"ok\"", result);
        Assert.DoesNotContain("not a var", result);
    }

    [Fact]
    public void MissingInputsOrOutputs_RendersError()
    {
        var md = """
            ```calc
            title: Empty
            ```
            """;
        var result = Run(md);
        Assert.Contains("nd-calc--error", result);
    }

    [Fact]
    public void MarkdownWithoutCalcFence_IsUnchanged()
    {
        var md = "Just a normal paragraph with the word calc in it.";
        Assert.Equal(md, Run(md));
    }

    [Fact]
    public void PlainCodeFence_IsNotTreatedAsCalc()
    {
        var md = "```python\nprint('calc')\n```";
        var result = Run(md);
        Assert.DoesNotContain("nd-calc", result);
        Assert.Contains("```python", result);
    }
}
