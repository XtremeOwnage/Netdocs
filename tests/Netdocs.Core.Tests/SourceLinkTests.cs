using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core;
using Netdocs.Core.Plugins;
using Netdocs.Core.Templating;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Covers where an imported page's "edit"/"view source" links point. An imported page's
/// RelativePath is where it landed in this site, not where it lives upstream, so the site-wide
/// repoUrl/editUri pattern builds a link into the wrong repository — issue #58.
/// </summary>
public class SourceLinkTests
{
    private static ImportedDocsPullSource Pull(string repo, string? repoUrl = null, string? editUri = null,
        string? sourcePath = "docs") => new()
        {
            Repository = repo,
            RepoUrl = repoUrl,
            EditUri = editUri,
            SourcePath = sourcePath,
        };

    // ---------------------------------------------------------------- clone URL -> browsable URL

    [Theory]
    [InlineData("https://github.com/org/repo.git", "https://github.com/org/repo")]
    [InlineData("https://github.com/org/repo", "https://github.com/org/repo")]
    [InlineData("git@github.com:org/repo.git", "https://github.com/org/repo")]
    [InlineData("git@gitlab.example.com:team/docs.git", "https://gitlab.example.com/team/docs")]
    [InlineData("ssh://git@github.com/org/repo.git", "https://github.com/org/repo")]
    public void DerivesABrowsableUrlFromACloneUrl(string clone, string expected) =>
        Assert.Equal(expected, SourceLinkBuilder.WebUrlFor(clone));

    [Theory]
    [InlineData("/srv/local/repo")]
    [InlineData("../relative/repo")]
    [InlineData("")]
    public void RefusesToGuessAHostForANonUrlRemote(string clone) =>
        Assert.Null(SourceLinkBuilder.WebUrlFor(clone));

    // ---------------------------------------------------------------- derivation

    [Fact]
    public void DerivesLinksFromTheCloneWhenNothingIsConfigured()
    {
        var builder = SourceLinkBuilder.ForPullSource(
            Pull("git@github.com:org/handbook.git"), branch: "main", NullLogger.Instance);

        var links = builder!.For("guides/setup.md");

        Assert.Equal("https://github.com/org/handbook/edit/main/docs/guides/setup.md", links.Edit);
        Assert.Equal("https://github.com/org/handbook/blob/main/docs/guides/setup.md", links.View);
    }

    [Fact]
    public void UsesTheCheckedOutBranchNotAnAssumedOne()
    {
        var builder = SourceLinkBuilder.ForPullSource(
            Pull("https://github.com/org/repo.git"), branch: "develop", NullLogger.Instance);

        Assert.Contains("/edit/develop/docs/", builder!.For("a.md").Edit);
    }

    [Fact]
    public void HonoursACustomSourcePath()
    {
        var builder = SourceLinkBuilder.ForPullSource(
            Pull("https://github.com/org/repo.git", sourcePath: "documentation/public"),
            branch: "main", NullLogger.Instance);

        Assert.Equal("https://github.com/org/repo/edit/main/documentation/public/a.md",
            builder!.For("a.md").Edit);
    }

    [Fact]
    public void ExplicitConfigurationWinsOverDerivation()
    {
        var builder = SourceLinkBuilder.ForPullSource(
            Pull("git@internal:mirror/repo.git", repoUrl: "https://git.example.com/team/docs", editUri: "edit/trunk/content"),
            branch: "main", NullLogger.Instance);

        Assert.Equal("https://git.example.com/team/docs/edit/trunk/content/a.md", builder!.For("a.md").Edit);
    }

    // ---------------------------------------------------------------- suppression

    /// <summary>
    /// A pinned tag or commit leaves the clone on a detached HEAD, so there is no branch to build an
    /// edit URL around. The issue asks for no button in that case rather than a wrong one.
    /// </summary>
    [Fact]
    public void NoBranchAndNoConfigMeansNoLinks() =>
        Assert.Null(SourceLinkBuilder.ForPullSource(
            Pull("https://github.com/org/repo.git"), branch: null, NullLogger.Instance));

    [Fact]
    public void ADetachedCloneStillLinksWhenEditUriIsConfigured()
    {
        var builder = SourceLinkBuilder.ForPullSource(
            Pull("https://github.com/org/repo.git", editUri: "blob/v2.0/docs"), branch: null, NullLogger.Instance);

        Assert.Equal("https://github.com/org/repo/blob/v2.0/docs/a.md", builder!.For("a.md").Edit);
    }

    [Fact]
    public void AnUnrecognisedRemoteMeansNoLinks() =>
        Assert.Null(SourceLinkBuilder.ForPullSource(
            Pull("/mnt/mirror/repo"), branch: "main", NullLogger.Instance));

    // ---------------------------------------------------------------- s3

    [Fact]
    public void S3SourcesHaveNoLinksUnlessBothOptionsAreConfigured()
    {
        Assert.Null(SourceLinkBuilder.ForS3Source(new ImportedDocsS3Source
        { Bucket = "b", Prefix = "p", Region = "r" }));

        Assert.Null(SourceLinkBuilder.ForS3Source(new ImportedDocsS3Source
        { Bucket = "b", Prefix = "p", Region = "r", RepoUrl = "https://github.com/org/repo" }));
    }

    [Fact]
    public void S3SourceUsesItsConfiguredRepository()
    {
        var builder = SourceLinkBuilder.ForS3Source(new ImportedDocsS3Source
        {
            Bucket = "b",
            Prefix = "p",
            Region = "r",
            RepoUrl = "https://github.com/org/repo",
            EditUri = "edit/main/docs",
        });

        Assert.Equal("https://github.com/org/repo/edit/main/docs/nested/a.md", builder!.For("nested/a.md").Edit);
    }

    // ---------------------------------------------------------------- what the page renders

    /// <summary>Renders a page through the real template engine, returning "edit|view".</summary>
    private static string RenderLinks(Page page, SiteConfig config)
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "main.html"), "{{ edit_url }}|{{ view_url }}");
            var site = new SiteContext
            {
                Config = config,
                Options = new BuildOptions(),
                LoggerFactory = NullLoggerFactory.Instance,
            };
            site.Pages.Add(page);
            return PageRenderer.Render(new TemplateEngine([dir]), site, page, new PluginAssets());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static SiteConfig SiteWithRepo() =>
        new() { RepoUrl = "https://github.com/org/site", EditUri = "edit/main/docs" };

    [Fact]
    public void AnOrdinaryPageStillUsesTheSiteWideRepository()
    {
        var page = new Page { SourcePath = "x", RelativePath = "guides/a.md", Url = "guides/a/" };

        Assert.Equal("https://github.com/org/site/edit/main/docs/guides/a.md|"
                   + "https://github.com/org/site/blob/main/docs/guides/a.md",
            RenderLinks(page, SiteWithRepo()));
    }

    /// <summary>
    /// The bug in #58: an imported page's RelativePath is its destination here, so the site-wide
    /// pattern produced a link into *this* repo at a path that only exists upstream.
    /// </summary>
    [Fact]
    public void AnImportedPageLinksUpstreamNotIntoThisRepository()
    {
        var page = new Page
        {
            SourcePath = "/tmp/clone/docs/a.md",
            RelativePath = "imported/team/a.md",
            Url = "imported/team/a/",
            SourceLinks = new SourceLinks(
                "https://github.com/org/handbook/edit/main/docs/a.md",
                "https://github.com/org/handbook/blob/main/docs/a.md"),
        };

        var rendered = RenderLinks(page, SiteWithRepo());

        Assert.DoesNotContain("org/site", rendered);
        Assert.Contains("org/handbook/edit/main/docs/a.md", rendered);
    }

    [Fact]
    public void AnImportedPageWithNoKnownOriginRendersNoLinks()
    {
        var page = new Page
        {
            SourcePath = "/tmp/clone/docs/a.md",
            RelativePath = "imported/a.md",
            Url = "imported/a/",
            SourceLinks = new SourceLinks(null, null),
        };

        Assert.Equal("|", RenderLinks(page, SiteWithRepo()));
    }

    [Fact]
    public void GeneratedPagesStillHaveNoLinks()
    {
        var page = new Page { SourcePath = "", RelativePath = "tags.md", Url = "tags/", IsGenerated = true };

        Assert.Equal("|", RenderLinks(page, SiteWithRepo()));
    }
}
