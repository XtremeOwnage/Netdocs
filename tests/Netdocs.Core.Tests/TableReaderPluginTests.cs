using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers mkdocs-table-reader: read_csv expansion and page-relative path resolution.</summary>
public class TableReaderPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-tbl-" + Guid.NewGuid().ToString("N"));

    public TableReaderPluginTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static SiteContext Site() => new()
    {
        Config = new SiteConfig(),
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static TableReaderPlugin Configured(string projectRoot)
    {
        var plugin = new TableReaderPlugin();
        plugin.Configure(new FakeContext { Config = new SiteConfig { ProjectRoot = projectRoot } });
        return plugin;
    }

    [Fact]
    public async Task ReadCsv_ResolvesRelativeToPageDirectory()
    {
        // CSV sits next to the post, referenced with a page-relative path (mkdocs-table-reader behavior).
        var assetsDir = Path.Combine(_dir, "blog", "posts", "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "data.csv"), "Make,Model\nChelsio,T520-CR\n");

        var page = new Page
        {
            SourcePath = Path.Combine(_dir, "blog", "posts", "post.md"),
            RelativePath = "blog/posts/post.md",
        };

        var result = await Configured(_dir)
            .ProcessAsync(page, "{{ read_csv(\"assets/data.csv\") }}", Site(), default);

        Assert.Contains("| Make | Model |", result);
        Assert.Contains("| Chelsio | T520-CR |", result);
        Assert.DoesNotContain("file not found", result);
    }

    [Fact]
    public async Task ReadCsv_MissingFile_EmitsComment()
    {
        var page = new Page { SourcePath = Path.Combine(_dir, "post.md"), RelativePath = "post.md" };

        var result = await Configured(_dir)
            .ProcessAsync(page, "{{ read_csv(\"nope.csv\") }}", Site(), default);

        Assert.Contains("table-reader: file not found: nope.csv", result);
    }

    private sealed class FakeContext : IPluginContext
    {
        public SiteConfig Config { get; init; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = new Dictionary<string, object?>();
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }
}
