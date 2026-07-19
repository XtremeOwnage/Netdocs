using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the versioning plugin: version model building and versions.json emission.</summary>
public class VersioningPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-ver-" + Guid.NewGuid().ToString("N"));

    public VersioningPluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private SiteContext Site() => new()
    {
        Config = new SiteConfig { ProjectRoot = _root, SiteDir = "", SiteName = "Test" },
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static object? Version(string v, string? title = null, string? url = null, params string[] aliases) =>
        new Dictionary<string, object?>
        {
            ["version"] = v,
            ["title"] = title,
            ["url"] = url,
            ["aliases"] = aliases.Cast<object?>().ToList(),
        };

    private static async Task<VersioningPlugin> Run(IReadOnlyDictionary<string, object?> options, SiteContext site)
    {
        var plugin = new VersioningPlugin();
        plugin.Configure(new FakeContext(options) { Config = site.Config });
        await plugin.OnBuildStartAsync(site, default);
        await plugin.OnBuildCompleteAsync(site, default);
        return plugin;
    }

    [Fact]
    public async Task StaticVersions_BuildModelAndMarkCurrent()
    {
        var site = Site();
        await Run(new Dictionary<string, object?>
        {
            ["current"] = "2.0",
            ["versions"] = new List<object?> { Version("2.0", "2.0 (latest)"), Version("1.0") },
        }, site);

        var versions = Assert.IsType<List<object?>>(site.State["versions"]);
        Assert.Equal(2, versions.Count);
        var current = Assert.IsType<Dictionary<string, object?>>(site.State["current_version"]);
        Assert.Equal("2.0", current["version"]);
        Assert.Equal(true, current["current"]);
    }

    [Fact]
    public async Task Version_WithoutUrl_UsesUrlTemplate()
    {
        var site = Site();
        await Run(new Dictionary<string, object?>
        {
            ["url_template"] = "/{version}/",
            ["versions"] = new List<object?> { Version("3.1") },
        }, site);

        var versions = Assert.IsType<List<object?>>(site.State["versions"]);
        var first = Assert.IsType<Dictionary<string, object?>>(versions[0]);
        Assert.Equal("/3.1/", first["url"]);
    }

    [Fact]
    public async Task LatestAlias_PicksCurrentWhenNotSet()
    {
        var site = Site();
        await Run(new Dictionary<string, object?>
        {
            ["versions"] = new List<object?> { Version("1.0"), Version("2.0", aliases: "latest") },
        }, site);

        var current = Assert.IsType<Dictionary<string, object?>>(site.State["current_version"]);
        Assert.Equal("2.0", current["version"]);
    }

    [Fact]
    public async Task EmitsMikeCompatibleVersionsJson()
    {
        var site = Site();
        await Run(new Dictionary<string, object?>
        {
            ["versions"] = new List<object?> { Version("2.0", "2.0", aliases: "latest"), Version("1.0") },
        }, site);

        var json = File.ReadAllText(Path.Combine(_root, "versions.json"));
        Assert.Contains("\"version\": \"2.0\"", json);
        Assert.Contains("\"latest\"", json);
        Assert.Contains("\"version\": \"1.0\"", json);
    }

    [Fact]
    public async Task NoVersions_EmitsNothing()
    {
        var site = Site();
        await Run(new Dictionary<string, object?>(), site);

        Assert.False(site.State.ContainsKey("versions"));
        Assert.False(File.Exists(Path.Combine(_root, "versions.json")));
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
