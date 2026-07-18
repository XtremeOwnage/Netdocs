using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the abbreviations preprocessor that appends *[ABBR]: definitions to pages.</summary>
public class AbbreviationsPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-abbr-" + Guid.NewGuid().ToString("N"));
    private readonly string _docs;

    public AbbreviationsPluginTests()
    {
        _docs = Path.Combine(_root, "docs");
        Directory.CreateDirectory(Path.Combine(_docs, "_include"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static SiteContext Site() => new()
    {
        Config = new SiteConfig(),
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static readonly Page Page = new() { SourcePath = "x", RelativePath = "index.md", Url = "" };

    private AbbreviationsPlugin Configured(params (string key, object? value)[] options)
    {
        var plugin = new AbbreviationsPlugin();
        var dict = new Dictionary<string, object?>();
        foreach (var (k, v) in options) dict[k] = v;
        plugin.Configure(new FakeContext(dict) { Config = new SiteConfig { ProjectRoot = _root, DocsDir = "docs" } });
        return plugin;
    }

    [Fact]
    public async Task AppendsDefaultAbbreviationFile()
    {
        File.WriteAllText(Path.Combine(_docs, "_include", "abbv.md"), "*[HTML]: HyperText Markup Language");
        var plugin = Configured();

        var result = await plugin.ProcessAsync(Page, "Uses HTML.", Site(), default);

        Assert.Contains("Uses HTML.", result);
        Assert.Contains("*[HTML]: HyperText Markup Language", result);
    }

    [Fact]
    public async Task LeavesMarkdownUntouchedWhenNoFile()
    {
        var plugin = Configured();
        const string md = "No abbreviations here.";

        var result = await plugin.ProcessAsync(Page, md, Site(), default);

        Assert.Equal(md, result);
    }

    [Fact]
    public async Task HonorsCustomFileList()
    {
        File.WriteAllText(Path.Combine(_docs, "glossary.md"), "*[API]: Application Programming Interface");
        var plugin = Configured(("files", new object?[] { "glossary.md" }));

        var result = await plugin.ProcessAsync(Page, "The API.", Site(), default);

        Assert.Contains("Application Programming Interface", result);
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
