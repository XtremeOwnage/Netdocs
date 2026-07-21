using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers content-hash cache-busting for local, unhashed asset references.</summary>
public class AssetVersionerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-assetver-" + Guid.NewGuid().ToString("N"));
    private readonly string _themeAssets;
    private readonly string _docs;

    public AssetVersionerTests()
    {
        _themeAssets = Path.Combine(_root, "theme", "assets");
        _docs = Path.Combine(_root, "docs");
        Directory.CreateDirectory(_themeAssets);
        Directory.CreateDirectory(_docs);
        File.WriteAllText(Path.Combine(_themeAssets, "netdocs.css"), "body{color:red}");
        File.WriteAllText(Path.Combine(_docs, "custom.js"), "console.log(1)");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Versions_theme_asset_with_content_hash()
    {
        var v = new AssetVersioner(_themeAssets, _docs);
        var result = v.Version("assets/netdocs.css");
        Assert.Matches(@"^assets/netdocs\.css\?v=[0-9a-f]{8}$", result);
    }

    [Fact]
    public void Versions_user_asset_under_docs()
    {
        var v = new AssetVersioner(_themeAssets, _docs);
        var result = v.Version("custom.js");
        Assert.Matches(@"^custom\.js\?v=[0-9a-f]{8}$", result);
    }

    [Fact]
    public void Hash_changes_when_content_changes()
    {
        var v1 = new AssetVersioner(_themeAssets, _docs).Version("assets/netdocs.css");
        File.WriteAllText(Path.Combine(_themeAssets, "netdocs.css"), "body{color:blue}");
        var v2 = new AssetVersioner(_themeAssets, _docs).Version("assets/netdocs.css");
        Assert.NotEqual(v1, v2);
    }

    [Theory]
    [InlineData("https://cdn.example.com/a.css")]
    [InlineData("http://cdn.example.com/a.css")]
    [InlineData("//cdn.example.com/a.css")]
    [InlineData("data:text/css,body{}")]
    [InlineData("assets/netdocs.css?already=1")]
    [InlineData("assets/netdocs.css#frag")]
    public void Leaves_external_or_qualified_hrefs_unchanged(string href)
    {
        var v = new AssetVersioner(_themeAssets, _docs);
        Assert.Equal(href, v.Version(href));
    }

    [Fact]
    public void Leaves_unknown_local_href_unchanged()
    {
        var v = new AssetVersioner(_themeAssets, _docs);
        Assert.Equal("assets/missing.css", v.Version("assets/missing.css"));
    }

    [Fact]
    public void NoOp_never_rewrites()
    {
        Assert.Equal("assets/netdocs.css", AssetVersioner.NoOp.Version("assets/netdocs.css"));
    }
}
