using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the UserDefinedMacro base class that lets authors register macros without regex.</summary>
public class UserDefinedMacroTests
{
    private static SiteContext Site() => new()
    {
        Config = new SiteConfig(),
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static Page Page(Dictionary<string, object?>? frontMatter = null) =>
        new() { SourcePath = "x", RelativePath = "index.md", Url = "", FrontMatter = frontMatter ?? new() };

    private sealed class SampleMacros : UserDefinedMacro
    {
        public override string Name => "sample";
        protected override void DefineMacros(IMacroBuilder macros) => macros
            .Add("year", () => "2024")
            .Add("greet", args => $"Hello {(args.Count > 0 ? args[0] : "")}")
            .Add("page", inv => inv.Page.RelativePath)
            .Variable("product", "Netdocs");
    }

    [Fact]
    public async Task ExpandsFunctionAndVariableMacros()
    {
        var result = await new SampleMacros().ProcessAsync(
            Page(), "© {{ year() }} {{ product }} — {{ greet(\"World\") }} on {{ page() }}", Site(), default);

        Assert.Contains("© 2024 Netdocs", result);
        Assert.Contains("Hello World", result);
        Assert.Contains("on index.md", result);
    }

    [Fact]
    public async Task LeavesUnknownTokensUntouched()
    {
        const string md = "{{ unknown() }} and {{ mystery }}";
        var result = await new SampleMacros().ProcessAsync(Page(), md, Site(), default);
        Assert.Equal(md, result);
    }

    [Fact]
    public async Task RespectsIgnoreMacrosFrontMatter()
    {
        const string md = "{{ year() }}";
        var page = Page(new Dictionary<string, object?> { ["ignore_macros"] = true });
        var result = await new SampleMacros().ProcessAsync(page, md, Site(), default);
        Assert.Equal(md, result);
    }

    [Fact]
    public async Task RespectsRenderMacrosFalse()
    {
        const string md = "{{ year() }}";
        var page = Page(new Dictionary<string, object?> { ["render_macros"] = false });
        var result = await new SampleMacros().ProcessAsync(page, md, Site(), default);
        Assert.Equal(md, result);
    }
}
