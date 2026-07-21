using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the blog plugin's excerpt handling — specifically that page-relative
/// <c>.md</c> links in a post excerpt resolve to the target's published URL when embedded in
/// the generated listing page.</summary>
public class BlogPluginTests
{
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

    private static Page Post(string relativePath, string date, string markdown) => new()
    {
        SourcePath = relativePath,
        RelativePath = relativePath,
        RawMarkdown = markdown,
        Title = Path.GetFileNameWithoutExtension(relativePath),
        FrontMatter = new Dictionary<string, object?> { ["date"] = date },
    };

    [Fact]
    public async Task ExcerptMdLink_ResolvesToTargetUrl()
    {
        var plugin = new BlogPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

        var index = new Page { SourcePath = "", RelativePath = "blog/index.md", Url = "blog/", Title = "Blog" };
        var foo = Post("blog/posts/2026/foo.md", "2026-05-01",
            "# Foo\n\nSee [the bar post](bar.md) for more.\n\n<!-- more -->\n\nBody.");
        var bar = Post("blog/posts/2026/bar.md", "2026-04-01", "# Bar\n\nBar intro.\n\n<!-- more -->\n\nBody.");

        var site = new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = Path.GetTempPath() },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        site.Pages.Add(index);
        site.Pages.Add(foo);
        site.Pages.Add(bar);

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        // The generated index embeds foo's excerpt; its bar.md link must point at bar's URL.
        Assert.Contains("](/blog/2026/bar/)", index.RawMarkdown);
        Assert.DoesNotContain("](bar.md)", index.RawMarkdown);
    }

    [Fact]
    public async Task ExcerptExternalMdLink_IsLeftUntouched()
    {
        var plugin = new BlogPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

        var index = new Page { SourcePath = "", RelativePath = "blog/index.md", Url = "blog/", Title = "Blog" };
        var foo = Post("blog/posts/2026/foo.md", "2026-05-01",
            "# Foo\n\nSee [readme](https://github.com/x/y/blob/main/README.md).\n\n<!-- more -->\n\nBody.");

        var site = new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = Path.GetTempPath() },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        site.Pages.Add(index);
        site.Pages.Add(foo);

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        Assert.Contains("https://github.com/x/y/blob/main/README.md", index.RawMarkdown);
    }
}
