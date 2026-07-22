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
    public async Task Download_RendersDownloadLink_WithDefaultText()
    {
        var target = new Page { SourcePath = "a", RelativePath = "files/setup.md", Url = "files/setup/" };
        var page = new Page { SourcePath = "b", RelativePath = "files/index.md", Url = "files/" };
        var site = Site(target, page);

        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ download(\"setup.md\") }}", site, default);

        Assert.Contains("class=\"md-button md-button--download\"", result);
        Assert.Contains("download=\"setup.md\"", result);
        Assert.Contains(">Download setup.md</a>", result);
        Assert.DoesNotContain("download(", result);
    }

    [Fact]
    public async Task Download_UsesCustomText_AndRelativeMode()
    {
        var target = new Page { SourcePath = "a", RelativePath = "files/setup.md", Url = "files/setup/" };
        var page = new Page { SourcePath = "b", RelativePath = "guides/index.md", Url = "guides/" };
        var site = Site(target, page);

        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ download(\"setup.md\", \"Grab the script\", \"relative\") }}", site, default);

        Assert.Contains(">Grab the script</a>", result);
        Assert.Contains("href=\"../files/setup/\"", result);
    }

    [Fact]
    public async Task Download_MissingFile_EmitsComment()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ download(\"nope.sh\") }}", Site(page), default);

        Assert.Contains("macros: download('nope.sh') not found", result);
    }

    [Fact]
    public async Task Version_RendersBadge_WithTagIcon()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ version(\"1.2.0\") }}", Site(page), default);

        Assert.Contains("class=\"nd-badge\"", result);
        Assert.Contains("class=\"nd-badge__icon\"", result);
        Assert.Contains(">1.2.0</span>", result);
        Assert.DoesNotContain("version(", result);
    }

    [Fact]
    public async Task Version_WithUrl_LinksTheVersion()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ version(\"1.2.0\", \"../changelog/\") }}", Site(page), default);

        Assert.Contains("<a href=\"../changelog/\">1.2.0</a>", result);
    }

    [Fact]
    public async Task Flag_KnownName_UsesDefaultLabel()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ flag(\"experimental\") }}", Site(page), default);

        Assert.Contains("class=\"nd-badge\"", result);
        Assert.Contains(">Experimental</span>", result);
    }

    [Fact]
    public async Task Flag_CustomText_OverridesLabel()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ flag(\"required\", \"Must set\") }}", Site(page), default);

        Assert.Contains(">Must set</span>", result);
    }

    [Fact]
    public async Task Badge_RendersIconAndText()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ badge(\"shield-check\", \"Stable\") }}", Site(page), default);

        Assert.Contains("class=\"nd-badge\"", result);
        Assert.Contains(">Stable</span>", result);
        Assert.Contains("<svg", result);
    }

    [Fact]
    public async Task Badge_UnknownIcon_FallsBackToTag()
    {
        var page = new Page { SourcePath = "b", RelativePath = "index.md", Url = "" };
        var result = await new MacrosPlugin()
            .ProcessAsync(page, "{{ badge(\"not-a-real-icon\", \"Label\") }}", Site(page), default);

        Assert.Contains("class=\"nd-badge\"", result);
        Assert.Contains(">Label</span>", result);
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
