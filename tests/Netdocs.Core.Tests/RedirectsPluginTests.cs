using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the redirects plugin: inline maps, JSON files (object and array shapes),
/// multiple files, precedence, and source-path normalization.</summary>
public class RedirectsPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-redirects-" + Guid.NewGuid().ToString("N"));

    public RedirectsPluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private SiteContext Site()
    {
        var site = new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = _root, SiteDir = "site", DocsDir = "docs" },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        return site;
    }

    private async Task<SiteContext> Run(IReadOnlyDictionary<string, object?> options)
    {
        var site = Site();
        var plugin = new RedirectsPlugin();
        plugin.Configure(new FakeContext(options, site.Config));
        await plugin.OnBuildCompleteAsync(site, default);
        return site;
    }

    private string ReadStub(SiteContext site, string relative) =>
        File.ReadAllText(Path.Combine(site.Config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public async Task InlineMap_EmitsRedirectStub()
    {
        var options = new Dictionary<string, object?>
        {
            ["redirect_maps"] = new Dictionary<string, object?> { ["old/page/"] = "/new/page/" },
        };
        var site = await Run(options);

        var html = ReadStub(site, "old/page/index.html");
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"0; url=/new/page/\">", html);
        Assert.Contains("<link rel=\"canonical\" href=\"/new/page/\">", html);
    }

    [Fact]
    public async Task JsonObjectFile_EmitsAllRedirects()
    {
        File.WriteAllText(Path.Combine(_root, "redirects.json"),
            """{ "blog/2018/ls--how-to-turn-on-the-alternator/": "/blog/2018/ls-how-to-turn-on-the-alternator/", "a/": "/b/" }""");
        var site = await Run(new Dictionary<string, object?> { ["redirect_files"] = "redirects.json" });

        var html = ReadStub(site, "blog/2018/ls--how-to-turn-on-the-alternator/index.html");
        Assert.Contains("url=/blog/2018/ls-how-to-turn-on-the-alternator/", html);
        Assert.True(File.Exists(Path.Combine(site.Config.AbsoluteSiteDir, "a", "index.html")));
    }

    [Fact]
    public async Task JsonArrayFile_WithSourceTargetObjects()
    {
        File.WriteAllText(Path.Combine(_root, "redirects.json"),
            """[ { "source": "old/x/", "target": "/new/x/", "status": 308 }, { "from": "old/y/", "to": "/new/y/" } ]""");
        var site = await Run(new Dictionary<string, object?> { ["redirect_files"] = "redirects.json" });

        Assert.Contains("url=/new/x/", ReadStub(site, "old/x/index.html"));
        Assert.Contains("url=/new/y/", ReadStub(site, "old/y/index.html"));
    }

    [Fact]
    public async Task MultipleFiles_AreAllLoaded()
    {
        File.WriteAllText(Path.Combine(_root, "one.json"), """{ "one/": "/1/" }""");
        File.WriteAllText(Path.Combine(_root, "two.json"), """{ "two/": "/2/" }""");
        var site = await Run(new Dictionary<string, object?>
        {
            ["redirect_files"] = new List<object?> { "one.json", "two.json" },
        });

        Assert.Contains("url=/1/", ReadStub(site, "one/index.html"));
        Assert.Contains("url=/2/", ReadStub(site, "two/index.html"));
    }

    [Fact]
    public async Task InlineMap_OverridesFileEntry()
    {
        File.WriteAllText(Path.Combine(_root, "redirects.json"), """{ "dup/": "/from-file/" }""");
        var site = await Run(new Dictionary<string, object?>
        {
            ["redirect_files"] = "redirects.json",
            ["redirect_maps"] = new Dictionary<string, object?> { ["dup/"] = "/from-inline/" },
        });

        Assert.Contains("url=/from-inline/", ReadStub(site, "dup/index.html"));
    }

    [Fact]
    public async Task LeadingSlashSource_IsNormalizedRelativeToSiteDir()
    {
        var site = await Run(new Dictionary<string, object?>
        {
            ["redirect_maps"] = new Dictionary<string, object?> { ["/deep/old/"] = "/new/" },
        });

        // The stub must land inside the site dir, not at the filesystem root.
        Assert.Contains("url=/new/", ReadStub(site, "deep/old/index.html"));
    }

    [Fact]
    public async Task MdSource_WritesIndexHtmlNextToStrippedPath()
    {
        var site = await Run(new Dictionary<string, object?>
        {
            ["redirect_maps"] = new Dictionary<string, object?> { ["old-post.md"] = "/blog/new-post/" },
        });

        Assert.Contains("url=/blog/new-post/", ReadStub(site, "old-post/index.html"));
    }

    [Fact]
    public async Task MissingFile_DoesNotThrow()
    {
        var site = await Run(new Dictionary<string, object?> { ["redirect_files"] = "does-not-exist.json" });
        Assert.False(Directory.Exists(site.Config.AbsoluteSiteDir) &&
            Directory.EnumerateFiles(site.Config.AbsoluteSiteDir).Any());
    }

    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options, SiteConfig config) : IPluginContext
    {
        public SiteConfig Config { get; } = config;
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
