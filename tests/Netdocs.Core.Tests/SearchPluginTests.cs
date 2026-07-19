using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Verifies the search index emitter honors its (previously hard-coded) lunr config knobs:
/// <c>lang</c> (single or list), <c>separator</c>, and <c>pipeline</c>.
/// </summary>
public class SearchPluginTests
{
    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options) : IPluginContext
    {
        public SiteConfig Config { get; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }

    private static JsonElement EmitConfig(IReadOnlyDictionary<string, object?> options)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plugin = new SearchPlugin();
            plugin.Configure(new FakeContext(options));

            var config = new SiteConfig { ProjectRoot = dir };
            var site = new SiteContext
            {
                Config = config,
                Options = new BuildOptions(),
                LoggerFactory = NullLoggerFactory.Instance,
            };
            site.Pages.Add(new Page
            {
                SourcePath = "",
                RelativePath = "index.md",
                Url = "",
                Title = "Home",
                HtmlContent = "<p>Hello world.</p>",
                PlainText = "Hello world.",
            });

            plugin.OnBuildCompleteAsync(site, CancellationToken.None).GetAwaiter().GetResult();
            var json = File.ReadAllText(Path.Combine(config.AbsoluteSiteDir, "search", "search_index.json"));
            return JsonDocument.Parse(json).RootElement.GetProperty("config").Clone();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Defaults_MatchMaterialLunrConfig()
    {
        var cfg = EmitConfig(new Dictionary<string, object?>());
        Assert.Equal("en", cfg.GetProperty("lang")[0].GetString());
        Assert.Equal("[\\s\\-]+", cfg.GetProperty("separator").GetString());
        Assert.Equal(
            new[] { "stemmer", "stopWordFilter", "trimmer" },
            cfg.GetProperty("pipeline").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Lang_AcceptsAList()
    {
        var cfg = EmitConfig(new Dictionary<string, object?>
        {
            ["lang"] = new List<object?> { "en", "de" },
        });
        Assert.Equal(new[] { "en", "de" }, cfg.GetProperty("lang").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void SeparatorAndPipeline_AreConfigurable()
    {
        var cfg = EmitConfig(new Dictionary<string, object?>
        {
            ["separator"] = "[\\s]+",
            ["pipeline"] = new List<object?> { "trimmer" },
        });
        Assert.Equal("[\\s]+", cfg.GetProperty("separator").GetString());
        Assert.Equal(new[] { "trimmer" }, cfg.GetProperty("pipeline").EnumerateArray().Select(e => e.GetString()).ToArray());
    }
}
