using Markdig;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the arithmatex math plugin: Markdig parsing and MathJax injection.</summary>
public class ArithmatexPluginTests
{
    private static SiteContext Site() => new()
    {
        Config = new SiteConfig(),
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static string Render(string markdown)
    {
        var builder = new MarkdownPipelineBuilder();
        new ArithmatexPlugin().Extend(builder, Site());
        return Markdig.Markdown.ToHtml(markdown, builder.Build());
    }

    [Fact]
    public void InlineMath_BecomesMathSpanWithDelimiters()
    {
        var html = Render("Euler $e^{i\\pi}+1=0$ done.");
        Assert.Contains("<span class=\"math\">\\(e^{i\\pi}+1=0\\)</span>", html);
    }

    [Fact]
    public void BlockMath_BecomesMathDivWithDelimiters()
    {
        var html = Render("$$\n\\int_0^1 x\\,dx\n$$");
        Assert.Contains("<div class=\"math\">", html);
        Assert.Contains("\\[", html);
        Assert.Contains("\\]", html);
    }

    [Fact]
    public void DollarInsideCode_IsNotTreatedAsMath()
    {
        var html = Render("`$not math$`");
        Assert.Contains("<code>$not math$</code>", html);
        Assert.DoesNotContain("class=\"math\"", html);
    }

    [Fact]
    public void Configure_InjectsMathJaxScript()
    {
        var assets = new CapturingContext();
        new ArithmatexPlugin().Configure(assets);

        var script = Assert.Single(assets.InlineScripts);
        Assert.Contains("window.MathJax", script);
        Assert.Contains("mathjax@4", script);
        Assert.Contains("document$", script);
    }

    [Fact]
    public void Configure_HonorsCustomMathJaxUrl()
    {
        var assets = new CapturingContext(new Dictionary<string, object?>
        {
            ["mathjax_url"] = "/assets/vendor/mathjax/tex-mml-chtml.js",
        });
        new ArithmatexPlugin().Configure(assets);

        Assert.Contains("/assets/vendor/mathjax/tex-mml-chtml.js", Assert.Single(assets.InlineScripts));
    }

    private sealed class CapturingContext(IReadOnlyDictionary<string, object?>? options = null) : IPluginContext
    {
        public List<string> InlineScripts { get; } = [];
        public SiteConfig Config { get; } = new();
        public BuildOptions Options { get; } = new();
        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
        public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } =
            new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options ?? new Dictionary<string, object?>();
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) => InlineScripts.Add(javascript);
        public void AddAsset(string sourcePath, string destRelative) { }
    }
}
