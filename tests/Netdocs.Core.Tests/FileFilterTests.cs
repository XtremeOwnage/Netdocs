using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Covers <see cref="FileFilterSettings"/> resolution and the gating of <c>.mkdocsignore</c> in
/// <see cref="ContentDiscovery"/>. The key behaviour: dev-only sections listed in
/// <c>.mkdocsignore</c> stay in non-production builds and disappear only when the file-filter is
/// enabled (production).
/// </summary>
public class FileFilterTests : IDisposable
{
    private readonly string _root;

    public FileFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ndfilter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFilter(string yaml) => File.WriteAllText(Path.Combine(_root, ".file-filter.yml"), yaml);

    // ---- FileFilterSettings unit behaviour -------------------------------------------------

    [Fact]
    public void NoConfig_IgnoreFileAlwaysApplies_FilterInactive()
    {
        var s = FileFilterSettings.Load(_root);

        Assert.False(s.Exists);
        Assert.False(s.IsActive(isServe: false));
        Assert.True(s.AppliesMkdocsIgnore(isServe: false));   // backward compatible: honored
        Assert.True(s.AppliesMkdocsIgnore(isServe: true));
        Assert.False(s.AppliesLabelFilter(isServe: false));
    }

    [Fact]
    public void EnabledFalse_IgnoreFileSkipped()
    {
        WriteFilter("enabled: false\nmkdocsignore: true\n");
        var s = FileFilterSettings.Load(_root);

        Assert.True(s.Exists);
        Assert.False(s.IsActive(isServe: false));
        Assert.False(s.AppliesMkdocsIgnore(isServe: false));
    }

    [Fact]
    public void EnabledTrue_IgnoreFileApplied_OnBuild_ButNotServeWhenServeDisabled()
    {
        WriteFilter("enabled: true\nenabled_on_serve: false\nmkdocsignore: true\n");
        var s = FileFilterSettings.Load(_root);

        Assert.True(s.IsActive(isServe: false));      // production build
        Assert.True(s.AppliesMkdocsIgnore(isServe: false));
        Assert.False(s.IsActive(isServe: true));      // serve stays off
        Assert.False(s.AppliesMkdocsIgnore(isServe: true));
    }

    [Fact]
    public void EnvResolution_ProdBuildFlag_TogglesFilter()
    {
        WriteFilter("enabled: !ENV [MKDOCS_PROD_BUILD, false]\nmkdocsignore: true\n");

        Environment.SetEnvironmentVariable("MKDOCS_PROD_BUILD", "false");
        Assert.False(FileFilterSettings.Load(_root).AppliesMkdocsIgnore(isServe: false));

        Environment.SetEnvironmentVariable("MKDOCS_PROD_BUILD", "true");
        Assert.True(FileFilterSettings.Load(_root).AppliesMkdocsIgnore(isServe: false));

        Environment.SetEnvironmentVariable("MKDOCS_PROD_BUILD", null);
    }

    [Fact]
    public void LabelFilter_RequiresEnabledAndExcludeTags()
    {
        WriteFilter("enabled: true\nexclude_tag:\n  - draft\n");
        Assert.True(FileFilterSettings.Load(_root).AppliesLabelFilter(isServe: false));

        WriteFilter("enabled: true\n");   // no exclude tags
        Assert.False(FileFilterSettings.Load(_root).AppliesLabelFilter(isServe: false));

        WriteFilter("enabled: false\nexclude_tag:\n  - draft\n");
        Assert.False(FileFilterSettings.Load(_root).AppliesLabelFilter(isServe: false));
    }

    // ---- ContentDiscovery integration ------------------------------------------------------

    private void Seed()
    {
        var docs = Path.Combine(_root, "docs");
        File.WriteAllText(Path.Combine(docs, "index.md"), "# Home\n");
        Directory.CreateDirectory(Path.Combine(docs, "teams"));
        File.WriteAllText(Path.Combine(docs, "teams", "index.md"), "# Teams\n");
        File.WriteAllText(Path.Combine(_root, ".mkdocsignore"), "teams/\n");
    }

    private IReadOnlyList<Page> Discover(bool isProduction)
    {
        var config = new SiteConfig { ProjectRoot = _root, DocsDir = "docs" };
        var options = new BuildOptions { IsProduction = isProduction, IsServe = false };
        return new ContentDiscovery(config, options, NullLogger<ContentDiscovery>.Instance).Discover();
    }

    [Fact]
    public void DevBuild_KeepsMkdocsIgnoredSection_WhenFilterDisabled()
    {
        Seed();
        WriteFilter("enabled: false\nmkdocsignore: true\n");

        var pages = Discover(isProduction: false);

        Assert.Contains(pages, p => p.RelativePath.Replace('\\', '/') == "teams/index.md");
    }

    [Fact]
    public void ProdBuild_HidesMkdocsIgnoredSection_WhenFilterEnabled()
    {
        Seed();
        WriteFilter("enabled: true\nmkdocsignore: true\n");

        var pages = Discover(isProduction: true);

        Assert.DoesNotContain(pages, p => p.RelativePath.Replace('\\', '/') == "teams/index.md");
        Assert.Contains(pages, p => p.RelativePath.Replace('\\', '/') == "index.md");
    }

    [Fact]
    public void NoFilterConfig_HonorsMkdocsIgnore_LikeGitignore()
    {
        Seed();   // no .file-filter.yml

        var pages = Discover(isProduction: false);

        Assert.DoesNotContain(pages, p => p.RelativePath.Replace('\\', '/') == "teams/index.md");
    }
}
