using Netdocs.Core.Optimization;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the non-destructive img -> picture webp rewrite.</summary>
public class WebpHtmlRewriterTests
{
    private static WebpManifest Manifest(params string[] paths) => new(paths);

    [Fact]
    public void WrapsLocalPngInPicture()
    {
        var html = """<p><img src="images/photo.png" alt="a"></p>""";
        var result = WebpHtmlRewriter.Rewrite(html, "", Manifest("images/photo.webp"));

        Assert.Contains("<picture>", result);
        Assert.Contains("""<source srcset="images/photo.webp" type="image/webp">""", result);
        Assert.Contains("""<img src="images/photo.png" alt="a">""", result);
        Assert.Contains("</picture>", result);
    }

    [Theory]
    [InlineData("photo.jpg", "photo.webp")]
    [InlineData("a/b/c.jpeg", "a/b/c.webp")]
    public void RewritesJpgAndJpeg(string src, string expectedWebp)
    {
        var result = WebpHtmlRewriter.Rewrite($"<img src=\"{src}\">", "", Manifest(expectedWebp));
        Assert.Contains($"srcset=\"{expectedWebp}\"", result);
    }

    [Theory]
    [InlineData("https://cdn.example.com/x.png")]
    [InlineData("//cdn.example.com/x.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("diagram.svg")]
    [InlineData("already.webp")]
    public void LeavesNonLocalOrNonRasterUntouched(string src)
    {
        var html = $"<img src=\"{src}\">";
        var result = WebpHtmlRewriter.Rewrite(html, "", Manifest("x.webp", "diagram.webp", "already.webp"));
        Assert.DoesNotContain("<picture>", result);
        Assert.Equal(html, result);
    }

    [Fact]
    public void NoImgTag_ReturnsInputUnchanged()
    {
        var html = "<p>no images here</p>";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html, "", Manifest("a.webp")));
    }

    /// <summary>
    /// The regression behind issue #30: an image the converter could not decode has no .webp in
    /// the output, so offering one points every webp-capable browser at a 404.
    /// </summary>
    [Fact]
    public void LeavesImageAloneWhenNoWebpWasProduced()
    {
        var html = """<img src="images/corrupt.png" alt="c">""";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html, "", Manifest("images/other.webp")));
    }

    [Fact]
    public void EmptyManifest_RewritesNothing()
    {
        var html = """<img src="a.png">""";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html, "", WebpManifest.Empty));
    }

    [Theory]
    // A page at guide/setup/ reaching back up to the shared image directory.
    [InlineData("guide/setup/", "../../img/a.png", "img/a.webp")]
    [InlineData("guide/setup/", "shot.png", "guide/setup/shot.webp")]
    // File-style URLs resolve against their parent directory, not themselves.
    [InlineData("404.html", "img/a.png", "img/a.webp")]
    // Root-relative sources ignore the page entirely.
    [InlineData("deep/page/", "/img/a.png", "img/a.webp")]
    public void ResolvesSrcRelativeToThePage(string pageUrl, string src, string manifestPath)
    {
        var result = WebpHtmlRewriter.Rewrite($"<img src=\"{src}\">", pageUrl, Manifest(manifestPath));
        Assert.Contains("<picture>", result);
    }

    [Fact]
    public void DoesNotOfferWebpFromAnotherDirectory()
    {
        // "img/a.webp" exists at the site root, but this page's own img/a.png would be
        // "guide/img/a.webp" — a different file that was never produced.
        var result = WebpHtmlRewriter.Rewrite("""<img src="img/a.png">""", "guide/", Manifest("img/a.webp"));
        Assert.DoesNotContain("<picture>", result);
    }

    [Fact]
    public void SrcEscapingTheSiteRootIsIgnored()
    {
        var html = """<img src="../../../secret.png">""";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html, "", Manifest("secret.webp")));
    }

    [Fact]
    public void KeepsQueryStringOnTheGeneratedSource()
    {
        var result = WebpHtmlRewriter.Rewrite("""<img src="img/a.png?v=2">""", "", Manifest("img/a.webp"));
        Assert.Contains("""<source srcset="img/a.webp?v=2" type="image/webp">""", result);
    }

    [Fact]
    public void MatchesPercentEncodedFileNames()
    {
        var result = WebpHtmlRewriter.Rewrite("""<img src="img/my%20photo.png">""", "", Manifest("img/my photo.webp"));
        Assert.Contains("""<source srcset="img/my%20photo.webp" type="image/webp">""", result);
    }

    [Fact]
    public void DoesNotNestInsideAnAuthoredPicture()
    {
        var html = """<picture><source srcset="a.webp" type="image/webp"><img src="a.png"></picture>""";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html, "", Manifest("a.webp")));
    }

    [Fact]
    public void StillRewritesImagesOutsideAnAuthoredPicture()
    {
        var html = """<picture><source srcset="a.webp" type="image/webp"><img src="a.png"></picture><img src="b.png">""";
        var result = WebpHtmlRewriter.Rewrite(html, "", Manifest("a.webp", "b.webp"));

        Assert.Contains("""<picture><source srcset="b.webp" type="image/webp"><img src="b.png"></picture>""", result);
        // The authored block is untouched: exactly one <source> for a.webp, not two.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "a\\.webp"));
    }

    [Fact]
    public void IgnoresDataSrcAttributes()
    {
        var html = """<img data-src="lazy.png" src="real.png">""";
        var result = WebpHtmlRewriter.Rewrite(html, "", Manifest("real.webp", "lazy.webp"));
        Assert.Contains("""<source srcset="real.webp" type="image/webp">""", result);
    }

    [Fact]
    public void ConvertibleDetection()
    {
        Assert.True(WebpConverter.IsConvertible("a.png"));
        Assert.True(WebpConverter.IsConvertible("a.JPG"));
        Assert.False(WebpConverter.IsConvertible("a.svg"));
        Assert.False(WebpConverter.IsConvertible("a.webp"));
    }

    [Fact]
    public void ManifestLookupIsPathNormalized()
    {
        var manifest = Manifest("/img/a.webp", "./img/b.webp", "img\\c.webp");
        Assert.True(manifest.Contains("img/a.webp"));
        Assert.True(manifest.Contains("img/b.webp"));
        Assert.True(manifest.Contains("img/c.webp"));
        Assert.False(manifest.Contains("img/d.webp"));
    }
}
