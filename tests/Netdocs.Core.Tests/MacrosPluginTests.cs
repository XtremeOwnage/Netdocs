using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the mkdocs-macros port: fileuri resolution and the button example macro.</summary>
public class MacrosPluginTests
{
    private static SiteContext Site(params Page[] pages)
    {
        var site = new SiteContext
        {
            Config = new SiteConfig(),
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        site.Pages.AddRange(pages);
        return site;
    }

    [Fact]
    public async Task FileUri_ResolvesPageUrl()
    {
        var target = new Page { SourcePath = "a", RelativePath = "guides/setup/install.md", Url = "guides/setup/install/" };
        var page = new Page { SourcePath = "b", RelativePath = "guides/setup/index.md", Url = "guides/setup/" };
        var site = Site(target, page);

        var result = await new MacrosPlugin()
            .ProcessAsync(page, "See [install]({{ fileuri(\"install.md\") }}).", site, default);

        Assert.Contains("/guides/setup/install/", result);
        Assert.DoesNotContain("fileuri", result);
    }

    [Fact]
    public async Task FileUri_MissingFile_EmitsComment()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ fileuri(\"nope.md\") }}", Site(page), default);

        Assert.Contains("macros: fileuri('nope.md') not found", result);
    }

    [Fact]
    public async Task Button_RendersMaterialButton()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ button(\"Get started\", \"getting-started/\") }}", Site(page), default);

        Assert.Contains("class=\"md-button\"", result);
        Assert.Contains("href=\"getting-started/\"", result);
        Assert.Contains(">Get started</a>", result);
    }

    [Fact]
    public async Task IgnoreMacros_LeavesMarkdownUntouched()
    {
        var page = new Page
        {
            SourcePath = "b",
            RelativePath = "index.md",
            Url = "",
            FrontMatter = new Dictionary<string, object?> { ["ignore_macros"] = true },
        };
        const string md = "{{ button(\"x\", \"y\") }}";
        var result = await new MacrosPlugin().ProcessAsync(page, md, Site(page), default);

        Assert.Equal(md, result);
    }

    [Fact]
    public async Task Variables_ExpandDefinedTokens_AndLeaveUnknownUntouched()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var plugin = new MacrosPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>
        {
            ["variables"] = new Dictionary<string, object?>
            {
                ["product"] = "Netdocs",
                ["version"] = "1.2.3",
            },
        }));

        var result = await plugin.ProcessAsync(page, "{{ product }} v{{ version }} — {{ unknown }}", Site(page), default);

        Assert.Contains("Netdocs v1.2.3", result);
        Assert.Contains("{{ unknown }}", result);
    }

    [Fact]
    public async Task FileUri_ModeVariants_FormatUrl()
    {
        var target = new Page { SourcePath = "a", RelativePath = "guides/setup/install.md", Url = "guides/setup/install/" };
        var page = new Page { SourcePath = "b", RelativePath = "guides/index.md", Url = "guides/" };
        var site = Site(target, page);

        // Root-absolute path (never includes a host, even if site_url were set).
        var path = await new MacrosPlugin()
            .ProcessAsync(page, "{{ fileuri(\"install.md\", \"path\") }}", site, default);
        Assert.Contains("/guides/setup/install/", path);

        // Page-relative URI: one `../` back out of guides/ to the site root.
        var rel = await new MacrosPlugin()
            .ProcessAsync(page, "{{ fileuri(\"install.md\", \"relative\") }}", site, default);
        Assert.Contains("../guides/setup/install/", rel);
    }

    [Fact]
    public async Task FileUri_AbsoluteMode_UsesSiteUrl()
    {
        var target = new Page { SourcePath = "a", RelativePath = "feed.md", Url = "feed/" };
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var site = Site(target, page);

        var plugin = new MacrosPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>())
        {
            Config = new SiteConfig { SiteUrl = "https://example.com/" },
        });

        var result = await plugin.ProcessAsync(page, "{{ fileuri(\"feed.md\", \"absolute\") }}", site, default);

        Assert.Contains("https://example.com/feed/", result);
    }

    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options) : IPluginContext
    {
        public SiteConfig Config { get; init; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }
}
