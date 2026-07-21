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

    private static JsonElement EmitIndex(Page page)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plugin = new SearchPlugin();
            plugin.Configure(new FakeContext(new Dictionary<string, object?>()));

            var config = new SiteConfig { ProjectRoot = dir };
            var site = new SiteContext
            {
                Config = config,
                Options = new BuildOptions(),
                LoggerFactory = NullLoggerFactory.Instance,
            };
            site.Pages.Add(page);

            plugin.OnBuildCompleteAsync(site, CancellationToken.None).GetAwaiter().GetResult();
            var json = File.ReadAllText(Path.Combine(config.AbsoluteSiteDir, "search", "search_index.json"));
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PageLevelDoc_CarriesTextAfterH1_ForTeaser()
    {
        // Material shows the page-level doc's text as the teaser on each result's main line.
        // The H1 is the page title, so the paragraph beneath it must land in the page-level doc,
        // not be swallowed by an H1 "section" (which leaves the teaser empty).
        var page = new Page
        {
            SourcePath = "",
            RelativePath = "guide.md",
            Url = "guide/",
            Title = "Guide",
            HtmlContent = "<h1 id=\"guide\">Guide</h1><p>Intro paragraph here.</p><h2 id=\"details\">Details</h2><p>Section body.</p>",
            PlainText = "Guide Intro paragraph here. Details Section body.",
        };

        var index = EmitIndex(page);
        var docs = index.GetProperty("docs").EnumerateArray().ToList();

        var pageDoc = docs.First(d => d.GetProperty("location").GetString() == "guide/");
        Assert.Contains("Intro paragraph here.", pageDoc.GetProperty("text").GetString());

        // The H1 must not become its own anchored section.
        Assert.DoesNotContain(docs, d => d.GetProperty("location").GetString() == "guide/#guide");
        // The H2 still produces a section doc.
        Assert.Contains(docs, d => d.GetProperty("location").GetString() == "guide/#details");
    }

    [Fact]
    public void SearchText_PreservesBlockHtml()
    {
        var page = new Page
        {
            SourcePath = "",
            RelativePath = "index.md",
            Url = "",
            Title = "Home",
            HtmlContent = "<h1 id=\"home\">Home</h1><p>Hello <strong>world</strong>.</p>",
            PlainText = "Home Hello world.",
        };

        var index = EmitIndex(page);
        var pageDoc = index.GetProperty("docs").EnumerateArray()
            .First(d => d.GetProperty("location").GetString() == "");

        Assert.Contains("<p>Hello <strong>world</strong>.</p>", pageDoc.GetProperty("text").GetString());
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
