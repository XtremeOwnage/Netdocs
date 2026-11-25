using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Plugins;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// <c>draft: true</c> in front matter keeps a page out of a normal build (output, nav, search)
/// and keeps it in when drafts are included (the <c>serve</c> default, or <c>--drafts</c>).
/// </summary>
public class DraftPagesTests : IDisposable
{
    private readonly string _root;

    public DraftPagesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nddrafts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static Page PageWith(Dictionary<string, object?> fm, bool generated = false) => new()
    {
        SourcePath = "x.md",
        RelativePath = "x.md",
        Url = "x/",
        FrontMatter = fm,
        IsGenerated = generated,
    };

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    public void IsDraft_ReadsTheDraftKey(object value, bool expected) =>
        Assert.Equal(expected, DraftPages.IsDraft(PageWith(new() { ["draft"] = value })));

    [Fact]
    public void IsDraft_FalseWithoutTheKey_AndForGeneratedPages()
    {
        Assert.False(DraftPages.IsDraft(PageWith(new())));
        Assert.False(DraftPages.IsDraft(PageWith(new() { ["draft"] = true }, generated: true)));
    }

    [Fact]
    public void IsDraft_ParsesRealFrontMatter()
    {
        var (meta, _) = FrontMatter.Split("---\ntitle: Wip\ndraft: true\n---\n# Wip\n");
        Assert.True(DraftPages.IsDraft(PageWith(new(meta))));
    }

    private async Task<SiteContext> Build(bool includeDrafts)
    {
        var docs = Path.Combine(_root, "docs");
        File.WriteAllText(Path.Combine(docs, "index.md"), "# Home\n");
        File.WriteAllText(Path.Combine(docs, "published.md"), "---\ntitle: Published post\n---\n# Published post\n");
        File.WriteAllText(Path.Combine(docs, "secret.md"), "---\ntitle: Secret draft\ndraft: true\n---\n# Secret draft\n");

        var config = new SiteConfig
        {
            ProjectRoot = _root,
            DocsDir = "docs",
            SiteDir = "site",
            SiteName = "Drafts",
            Plugins = [new PluginConfig { Name = "search" }],
        };
        var options = new BuildOptions { IncludeDrafts = includeDrafts, Clean = true };
        var registry = new PluginRegistry().Register<SearchPlugin>("search");
        return await new BuildEngine(config, options, registry, NullLoggerFactory.Instance).BuildAsync();
    }

    private static IEnumerable<NavNode> Flatten(IEnumerable<NavNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [Fact]
    public async Task Build_LeavesDraftsOut()
    {
        var site = await Build(includeDrafts: false);
        var siteDir = Path.Combine(_root, "site");

        Assert.DoesNotContain(site.Pages, p => p.RelativePath == "secret.md");
        Assert.False(File.Exists(Path.Combine(siteDir, "secret", "index.html")));
        Assert.True(File.Exists(Path.Combine(siteDir, "published", "index.html")));
        Assert.DoesNotContain(Flatten(site.Navigation), n => n.Title == "Secret draft");

        var index = File.ReadAllText(Path.Combine(siteDir, "search", "search_index.json"));
        Assert.DoesNotContain("Secret draft", index);
        Assert.Contains("Published post", index);
    }

    [Fact]
    public async Task Build_KeepsDrafts_WhenIncluded()
    {
        var site = await Build(includeDrafts: true);

        Assert.Contains(site.Pages, p => p.RelativePath == "secret.md");
        Assert.True(File.Exists(Path.Combine(_root, "site", "secret", "index.html")));
    }
}
