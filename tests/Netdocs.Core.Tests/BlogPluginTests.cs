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

    private static Page Post(string relativePath, string date, string markdown, string? category = null)
    {
        var fm = new Dictionary<string, object?> { ["date"] = date };
        if (category is not null) fm["categories"] = new List<object?> { category };
        return new Page
        {
            SourcePath = relativePath,
            RelativePath = relativePath,
            RawMarkdown = markdown,
            Title = Path.GetFileNameWithoutExtension(relativePath),
            FrontMatter = fm,
        };
    }

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

    [Fact]
    public async Task Posts_CarryPopulatedArchiveAndCategoryNav()
    {
        // Regression: the shared Archive/Categories nav must be attached to posts AFTER it is
        // computed. A prior bug assigned it inside the discovery loop, capturing empty initial
        // lists, so posts rendered an empty blog nav while the index (assigned later) did not.
        var plugin = new BlogPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

        var index = new Page { SourcePath = "", RelativePath = "blog/index.md", Url = "blog/", Title = "Blog" };
        var foo = Post("blog/posts/2026/foo.md", "2026-05-01",
            "# Foo\n\nIntro.\n\n<!-- more -->\n\nBody.", "Homelab");
        var bar = Post("blog/posts/2025/bar.md", "2025-04-01",
            "# Bar\n\nBar intro.\n\n<!-- more -->\n\nBody.", "Networking");

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

        foreach (var post in new[] { foo, bar })
        {
            Assert.True(post.Meta.ContainsKey("is_post"));
            var archives = Assert.IsAssignableFrom<System.Collections.IEnumerable>(post.Meta["blog_archives"]);
            Assert.NotEmpty(archives.Cast<object?>());
            var categories = Assert.IsAssignableFrom<System.Collections.IEnumerable>(post.Meta["blog_categories"]);
            Assert.NotEmpty(categories.Cast<object?>());
        }
    }

    private static SiteContext NewSite() => new()
    {
        Config = new SiteConfig { ProjectRoot = Path.GetTempPath() },
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    [Fact]
    public async Task PostUrl_UsesSlugifiedTitle_NotFileName()
    {
        // mkdocs-material derives the post URL from the title, so a post titled
        // "Hacking KVM with IP Control" stored as 2025-02-24-KVM-Esphome.md must publish at
        // /blog/2025/hacking-kvm-with-ip-control/ — matching the old MkDocs URLs.
        var plugin = new BlogPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

        var post = new Page
        {
            SourcePath = "blog/posts/2025/2025-02-24-KVM-Esphome.md",
            RelativePath = "blog/posts/2025/2025-02-24-KVM-Esphome.md",
            RawMarkdown = "# Hacking a cheap KVM with IP Control\n\nBody.",
            Title = "Hacking KVM with IP Control",
            FrontMatter = new Dictionary<string, object?> { ["date"] = "2025-02-24" },
        };

        var site = NewSite();
        site.Pages.Add(post);

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        Assert.Equal("blog/2025/hacking-kvm-with-ip-control/", post.Url);
    }

    [Fact]
    public async Task PostUrl_ExplicitFrontMatterSlug_Wins()
    {
        var plugin = new BlogPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

        var post = new Page
        {
            SourcePath = "blog/posts/2025/whatever.md",
            RelativePath = "blog/posts/2025/whatever.md",
            RawMarkdown = "# A Totally Different Title\n\nBody.",
            Title = "A Totally Different Title",
            FrontMatter = new Dictionary<string, object?> { ["date"] = "2025-02-24", ["slug"] = "custom-slug" },
        };

        var site = NewSite();
        site.Pages.Add(post);

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        Assert.Equal("blog/2025/custom-slug/", post.Url);
    }

    [Fact]
    public async Task PostUrl_FallsBackToFirstH1_WhenNoFrontMatterTitle()
    {
        // With no front-matter title, the title (and therefore the slug) comes from the first H1,
        // mirroring how mkdocs-material resolves the page title before rendering.
        var plugin = new BlogPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

        var post = new Page
        {
            SourcePath = "blog/posts/2025/2025-05-08-KVM-Assistant-V2.md",
            RelativePath = "blog/posts/2025/2025-05-08-KVM-Assistant-V2.md",
            RawMarkdown = "# Introducing [KVM](https://example.com) Assistant\n\nBody.",
            // Title deliberately left null: only the H1 is available at slug-assignment time.
            FrontMatter = new Dictionary<string, object?> { ["date"] = "2025-05-08" },
        };

        var site = NewSite();
        site.Pages.Add(post);

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        // Link syntax collapses to its text, so the URL word set is "introducing kvm assistant".
        Assert.Equal("blog/2025/introducing-kvm-assistant/", post.Url);
    }
}
