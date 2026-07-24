using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the tags plugin's marker rendering — the bare <c>&lt;!-- material/tags --&gt;</c>
/// full index and the scoped <c>{ include: […] }</c> / <c>{ exclude: […] }</c> variants that filter
/// the listing to specific tag categories.</summary>
public class TagsPluginTests
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

    private static Page Tagged(string relativePath, string title, params string[] tags) => new()
    {
        SourcePath = relativePath,
        RelativePath = relativePath,
        Url = Path.ChangeExtension(relativePath, null).Replace('\\', '/') + "/",
        Title = title,
        RawMarkdown = $"# {title}\n\nBody.",
        FrontMatter = new Dictionary<string, object?> { ["tags"] = tags.Cast<object?>().ToList() },
    };

    private static Page Marker(string relativePath, string marker) => new()
    {
        SourcePath = relativePath,
        RelativePath = relativePath,
        Url = Path.ChangeExtension(relativePath, null).Replace('\\', '/') + "/",
        Title = "Tags",
        RawMarkdown = $"# Tags\n\n{marker}\n",
        FrontMatter = new Dictionary<string, object?>(),
    };

    private static SiteContext BuildSite(params Page[] pages)
    {
        var site = new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = Path.GetTempPath() },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        foreach (var p in pages) site.Pages.Add(p);
        return site;
    }

    private static TagsPlugin Configured()
    {
        var plugin = new TagsPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?> { ["export"] = false }));
        return plugin;
    }

    [Fact]
    public async Task BareMarker_RendersFullIndex()
    {
        var app = Tagged("app.md", "App Page", "Application");
        var proc = Tagged("proc.md", "Proc Page", "Process");
        var index = Marker("tags/index.md", "<!-- material/tags -->");
        var site = BuildSite(app, proc, index);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        Assert.Contains("## Application", index.RawMarkdown);
        Assert.Contains("## Process", index.RawMarkdown);
        Assert.Contains("[App Page](/app/)", index.RawMarkdown);
        Assert.Contains("[Proc Page](/proc/)", index.RawMarkdown);
        Assert.DoesNotContain("material/tags", index.RawMarkdown);
    }

    [Fact]
    public async Task ScopedInclude_RendersOnlyMatchingCategory()
    {
        var app = Tagged("app.md", "App Page", "Application");
        var appAws = Tagged("aws.md", "AWS Page", "Application/AWS");
        var proc = Tagged("proc.md", "Proc Page", "Process");
        var page = Marker("tags/apps.md", "<!-- material/tags { include: [Application] } -->");
        var site = BuildSite(app, appAws, proc, page);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        // Parent category and its nested child are included...
        Assert.Contains("[App Page](/app/)", page.RawMarkdown);
        Assert.Contains("[AWS Page](/aws/)", page.RawMarkdown);
        Assert.Contains("Application/AWS", page.RawMarkdown);
        // ...but the unrelated category is excluded.
        Assert.DoesNotContain("[Proc Page](/proc/)", page.RawMarkdown);
        Assert.DoesNotContain("## Process", page.RawMarkdown);
    }

    [Fact]
    public async Task ScopedExclude_DropsMatchingCategory()
    {
        var app = Tagged("app.md", "App Page", "Application");
        var proc = Tagged("proc.md", "Proc Page", "Process");
        var page = Marker("tags/rest.md", "<!-- material/tags { exclude: [Process] } -->");
        var site = BuildSite(app, proc, page);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        Assert.Contains("[App Page](/app/)", page.RawMarkdown);
        Assert.DoesNotContain("[Proc Page](/proc/)", page.RawMarkdown);
    }

    [Fact]
    public async Task ScopedInclude_UnknownCategory_RendersEmpty()
    {
        var app = Tagged("app.md", "App Page", "Application");
        var page = Marker("tags/scripts.md", "<!-- material/tags { include: [Scripts] } -->");
        var site = BuildSite(app, page);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        // Marker is consumed (no leftover comment) but produces no listing.
        Assert.DoesNotContain("material/tags", page.RawMarkdown);
        Assert.DoesNotContain("[App Page]", page.RawMarkdown);
    }

    [Fact]
    public async Task MultipleMarkers_EachScopedIndependently()
    {
        var app = Tagged("app.md", "App Page", "Application");
        var proc = Tagged("proc.md", "Proc Page", "Process");
        var apps = Marker("tags/apps.md", "<!-- material/tags { include: [Application] } -->");
        var procs = Marker("tags/procs.md", "<!-- material/tags { include: [Process] } -->");
        var site = BuildSite(app, proc, apps, procs);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        Assert.Contains("[App Page](/app/)", apps.RawMarkdown);
        Assert.DoesNotContain("[Proc Page]", apps.RawMarkdown);

        Assert.Contains("[Proc Page](/proc/)", procs.RawMarkdown);
        Assert.DoesNotContain("[App Page]", procs.RawMarkdown);
    }

    [Fact]
    public async Task Heading_EmitsMaterialCompatibleAnchor_ForHierarchicalTag()
    {
        var page = Tagged("cs.md", "C# Page", "Development/C#");
        var index = Marker("tags/index.md", "<!-- material/tags -->");
        var site = BuildSite(page, index);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        // Parent category and its child each carry an explicit "tag:" id matching MkDocs Material,
        // with the '/' preserved and the '#' dropped by per-segment slugification.
        Assert.Contains("## Development { #tag:development }", index.RawMarkdown);
        Assert.Contains("### Development/C# { #tag:development/c }", index.RawMarkdown);
    }

    [Fact]
    public async Task Heading_Anchor_SlugifiesSpacesAndPunctuationPerSegment()
    {
        var monitoring = Tagged("mon.md", "Mon Page", "Energy Monitoring");
        var dotnet = Tagged("net.md", "Net Page", "Development/.NET");
        var index = Marker("tags/index.md", "<!-- material/tags -->");
        var site = BuildSite(monitoring, dotnet, index);

        await Configured().OnBuildStartAsync(site, CancellationToken.None);

        Assert.Contains("{ #tag:energy-monitoring }", index.RawMarkdown);
        Assert.Contains("{ #tag:development/net }", index.RawMarkdown);
    }
}
