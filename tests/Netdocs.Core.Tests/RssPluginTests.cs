using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers RSS/Atom feed generation, per-post overrides, and image support.</summary>
public class RssPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-rss-" + Guid.NewGuid().ToString("N"));

    public RssPluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private SiteContext Site(params BlogPost[] posts)
    {
        var site = new SiteContext
        {
            Config = new SiteConfig
            {
                ProjectRoot = _root,
                SiteDir = "",
                SiteName = "Test Site",
                SiteDescription = "A test",
                SiteUrl = "https://example.com/",
            },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        site.State["blog_posts"] = posts.ToList();
        return site;
    }

    private static BlogPost Post(string title, string url, DateTimeOffset date,
        string excerpt = "Excerpt", string html = "<p>Body</p>",
        Dictionary<string, object?>? frontMatter = null, params string[] categories) =>
        new(new Page
        {
            SourcePath = "x",
            RelativePath = url + ".md",
            Url = url + "/",
            Title = title,
            HtmlContent = html,
            FrontMatter = frontMatter ?? new(),
        }, date, categories, excerpt);

    private async Task<RssPlugin> Run(IReadOnlyDictionary<string, object?> options, SiteContext site)
    {
        var plugin = new RssPlugin();
        plugin.Configure(new FakeContext(options));
        await plugin.OnBuildCompleteAsync(site, default);
        return plugin;
    }

    [Fact]
    public async Task WritesRssFeedWithItemMetadata()
    {
        var site = Site(Post("Hello", "blog/hello", new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            categories: new[] { "News" }));
        await Run(new Dictionary<string, object?>(), site);

        var xml = File.ReadAllText(Path.Combine(_root, "feed_rss_created.xml"));
        Assert.Contains("<rss version=\"2.0\"", xml);
        Assert.Contains("<title>Hello</title>", xml);
        Assert.Contains("https://example.com/blog/hello/", xml);
        Assert.Contains("<category>News</category>", xml);
        Assert.Contains("rel=\"self\"", xml);
    }

    [Fact]
    public async Task PerPostTitleOverride_WinsOverPageTitle()
    {
        var fm = new Dictionary<string, object?> { ["rss_title"] = "Feed Title" };
        var site = Site(Post("Page Title", "blog/p", DateTimeOffset.UtcNow, frontMatter: fm));
        await Run(new Dictionary<string, object?>(), site);

        var xml = File.ReadAllText(Path.Combine(_root, "feed_rss_created.xml"));
        Assert.Contains("<title>Feed Title</title>", xml);
    }

    [Fact]
    public async Task FrontMatterImage_EmittedAsEnclosure()
    {
        var fm = new Dictionary<string, object?> { ["image"] = "/img/hero.png" };
        var site = Site(Post("P", "blog/p", DateTimeOffset.UtcNow, frontMatter: fm));
        await Run(new Dictionary<string, object?>(), site);

        var xml = File.ReadAllText(Path.Combine(_root, "feed_rss_created.xml"));
        Assert.Contains("<enclosure", xml);
        Assert.Contains("https://example.com/img/hero.png", xml);
        Assert.Contains("type=\"image/png\"", xml);
    }

    [Fact]
    public async Task ContentImage_UsedWhenNoFrontMatterImage()
    {
        var site = Site(Post("P", "blog/p", DateTimeOffset.UtcNow,
            html: "<p>Hi</p><img src=\"pic.jpg\">"));
        await Run(new Dictionary<string, object?>(), site);

        var xml = File.ReadAllText(Path.Combine(_root, "feed_rss_created.xml"));
        Assert.Contains("https://example.com/blog/p/pic.jpg", xml);
    }

    [Fact]
    public async Task FullContent_EmitsContentEncoded()
    {
        var site = Site(Post("P", "blog/p", DateTimeOffset.UtcNow, html: "<p>Full body</p>"));
        await Run(new Dictionary<string, object?> { ["full_content"] = true }, site);

        var xml = File.ReadAllText(Path.Combine(_root, "feed_rss_created.xml"));
        Assert.Contains("encoded", xml);
        Assert.Contains("Full body", xml);
    }

    [Fact]
    public async Task Atom_GeneratedWhenEnabled()
    {
        var site = Site(Post("Hello", "blog/hello", new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        await Run(new Dictionary<string, object?> { ["atom"] = true }, site);

        var path = Path.Combine(_root, "feed_atom_created.xml");
        Assert.True(File.Exists(path));
        var xml = File.ReadAllText(path);
        Assert.Contains("http://www.w3.org/2005/Atom", xml);
        Assert.Contains("<entry>", xml);
        Assert.Contains("<title>Hello</title>", xml);
    }

    [Fact]
    public async Task Length_LimitsItems()
    {
        var now = DateTimeOffset.UtcNow;
        var site = Site(
            Post("A", "blog/a", now),
            Post("B", "blog/b", now),
            Post("C", "blog/c", now));
        await Run(new Dictionary<string, object?> { ["length"] = 2 }, site);

        var xml = File.ReadAllText(Path.Combine(_root, "feed_rss_created.xml"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(xml, "<item>").Count);
    }

    [Fact]
    public void SocialIcon_AddsRssFeedEntryToExtraSocial()
    {
        var config = new SiteConfig { SiteUrl = "https://example.com/" };
        var plugin = new RssPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?> { ["social_icon"] = true }, config));

        Assert.True(config.Extra.TryGetValue("social", out var socialObj));
        var social = Assert.IsAssignableFrom<System.Collections.IEnumerable>(socialObj).Cast<object?>().ToList();
        var entry = Assert.IsAssignableFrom<IDictionary<string, object?>>(social.Single());
        Assert.Equal("fontawesome/solid/rss", entry["icon"]);
        Assert.Equal("https://example.com/feed_rss_created.xml", entry["link"]);
    }

    [Fact]
    public void SocialFeedAtom_LinksAtomFeed()
    {
        var config = new SiteConfig { SiteUrl = "https://example.com" };
        var plugin = new RssPlugin();
        plugin.Configure(new FakeContext(
            new Dictionary<string, object?> { ["social_icon"] = true, ["social_feed"] = "atom" }, config));

        var social = ((System.Collections.IEnumerable)config.Extra["social"]!).Cast<object?>().ToList();
        var entry = (IDictionary<string, object?>)social.Single()!;
        Assert.Equal("https://example.com/feed_atom_created.xml", entry["link"]);
    }

    [Fact]
    public void SocialIcon_PreservesExistingSocialEntries()
    {
        var config = new SiteConfig
        {
            SiteUrl = "https://example.com",
            Extra = new Dictionary<string, object?>
            {
                ["social"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["icon"] = "fontawesome/brands/github", ["link"] = "https://github.com/x" },
                },
            },
        };
        var plugin = new RssPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?> { ["social_icon"] = true }, config));

        var social = ((System.Collections.IEnumerable)config.Extra["social"]!).Cast<object?>().ToList();
        Assert.Equal(2, social.Count);
    }

    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options, SiteConfig? config = null) : IPluginContext
    {
        public SiteConfig Config { get; } = config ?? new();
        public BuildOptions Options { get; } = new();
        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
        public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; }
            = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }
}
