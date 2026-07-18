using Netdocs.Core.Optimization;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the non-destructive img -> picture webp rewrite.</summary>
public class WebpHtmlRewriterTests
{
    [Fact]
    public void WrapsLocalPngInPicture()
    {
        var html = """<p><img src="images/photo.png" alt="a"></p>""";
        var result = WebpHtmlRewriter.Rewrite(html);

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
        var result = WebpHtmlRewriter.Rewrite($"<img src=\"{src}\">");
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
        var result = WebpHtmlRewriter.Rewrite(html);
        Assert.DoesNotContain("<picture>", result);
        Assert.Equal(html, result);
    }

    [Fact]
    public void NoImgTag_ReturnsInputUnchanged()
    {
        var html = "<p>no images here</p>";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html));
    }

    [Fact]
    public void ConvertibleDetection()
    {
        Assert.True(WebpConverter.IsConvertible("a.png"));
        Assert.True(WebpConverter.IsConvertible("a.JPG"));
        Assert.False(WebpConverter.IsConvertible("a.svg"));
        Assert.False(WebpConverter.IsConvertible("a.webp"));
    }
}
