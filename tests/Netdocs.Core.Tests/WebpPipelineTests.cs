using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core;
using Netdocs.Core.Content;
using Netdocs.Core.Optimization;
using Netdocs.Core.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// End-to-end cover for the seam issue #30 lived in: real files on disk are converted by
/// <see cref="AssetPipeline"/>, and only what it genuinely produced may be offered to the HTML
/// rewrite. <see cref="WebpHtmlRewriterTests"/> exercises the rewrite against a hand-built
/// manifest; these tests exist so a manifest that silently stops reflecting the output — or a
/// pipeline that rewrites HTML before conversion has happened — cannot pass unnoticed.
/// </summary>
public class WebpPipelineTests : IDisposable
{
    // 2x2 solid PNG.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAE0lEQVR4nGPhEpH7zwAELAxQAAASHAFEWc1phgAAAABJRU5ErkJggg==";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-webp-pipeline-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private SiteContext NewSite(bool convert = true)
    {
        var config = new SiteConfig { ProjectRoot = _root };
        config.Optimize.ConvertImagesToWebp = convert;
        Directory.CreateDirectory(config.AbsoluteDocsDir);
        return new SiteContext
        {
            Config = config,
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
    }

    private void WriteDoc(SiteContext site, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(site.Config.AbsoluteDocsDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static Task<WebpManifest> Run(SiteContext site) =>
        AssetPipeline.CopyAllAsync(site, new PluginAssets(), CancellationToken.None);

    private static string SitePath(SiteContext site, string relative) =>
        Path.Combine(site.Config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task ConvertedImagesLandOnDiskAndInTheManifest()
    {
        var site = NewSite();
        WriteDoc(site, "img/photo.png", Convert.FromBase64String(TinyPngBase64));

        var manifest = await Run(site);

        Assert.True(File.Exists(SitePath(site, "img/photo.webp")));
        Assert.True(File.Exists(SitePath(site, "img/photo.png")), "the original must be kept as the fallback");
        Assert.True(manifest.Contains("img/photo.webp"));
    }

    /// <summary>
    /// The issue #30 scenario, driven through the real pipeline: an image the encoder cannot decode
    /// produces no <c>.webp</c>, so it must not appear in the manifest and must keep a plain
    /// <c>&lt;img&gt;</c> — advertising a source that was never written breaks the image in every
    /// WebP-capable browser.
    /// </summary>
    [Fact]
    public async Task UndecodableImageIsNeverOfferedAsAWebpSource()
    {
        var site = NewSite();
        WriteDoc(site, "img/photo.png", Convert.FromBase64String(TinyPngBase64));
        WriteDoc(site, "img/corrupt.png", "GIF89a-not-really-a-png"u8.ToArray());

        var manifest = await Run(site);

        Assert.False(File.Exists(SitePath(site, "img/corrupt.webp")));
        Assert.False(manifest.Contains("img/corrupt.webp"));

        var html = WebpHtmlRewriter.Rewrite(
            """<img src="img/photo.png"><img src="img/corrupt.png">""", "", manifest);

        Assert.Contains("""<source srcset="img/photo.webp" type="image/webp">""", html);
        Assert.DoesNotContain("corrupt.webp", html);
    }

    [Fact]
    public async Task ManifestPathsAreSiteRelativeAndMatchWhatAPageWouldReference()
    {
        var site = NewSite();
        WriteDoc(site, "guide/assets/deep.png", Convert.FromBase64String(TinyPngBase64));

        var manifest = await Run(site);

        // A page at guide/setup/ referencing ../assets/deep.png must resolve onto the same entry.
        var html = WebpHtmlRewriter.Rewrite("""<img src="../assets/deep.png">""", "guide/setup/", manifest);
        Assert.Contains("""<source srcset="../assets/deep.webp" type="image/webp">""", html);
    }

    [Fact]
    public async Task NonRasterAssetsAreCopiedButNotConverted()
    {
        var site = NewSite();
        WriteDoc(site, "img/diagram.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());

        var manifest = await Run(site);

        Assert.True(File.Exists(SitePath(site, "img/diagram.svg")));
        Assert.Equal(0, manifest.Count);
    }

    [Fact]
    public async Task ConversionDisabled_ProducesAnEmptyManifestAndNoRewrite()
    {
        var site = NewSite(convert: false);
        WriteDoc(site, "img/photo.png", Convert.FromBase64String(TinyPngBase64));

        var manifest = await Run(site);

        Assert.False(File.Exists(SitePath(site, "img/photo.webp")));
        Assert.Equal(0, manifest.Count);

        var html = """<img src="img/photo.png">""";
        Assert.Equal(html, WebpHtmlRewriter.Rewrite(html, "", manifest));
    }

    [Fact]
    public async Task GeneratedWebpFilesSurviveStalePruning()
    {
        var site = NewSite();
        WriteDoc(site, "img/photo.png", Convert.FromBase64String(TinyPngBase64));

        await Run(site);
        OutputWriter.PruneStale(site, site.Config.AbsoluteSiteDir);

        Assert.True(File.Exists(SitePath(site, "img/photo.webp")));
    }
}
