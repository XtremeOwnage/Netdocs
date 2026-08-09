using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Optimization;
using Netdocs.Core.Plugins;

namespace Netdocs.Core;

/// <summary>Copies theme assets, docs static files, and plugin-registered files into the site output.</summary>
public static class AssetPipeline
{
    private static readonly string[] SkipExtensions = [".md", ".markdown"];

    /// <summary>
    /// Copies every asset into the output and returns the set of <c>.webp</c> files produced along
    /// the way. Pages are rendered after this runs, so the HTML rewrite can offer a webp source
    /// only for images that were genuinely converted.
    /// </summary>
    public static async Task<WebpManifest> CopyAllAsync(SiteContext site, PluginAssets assets, CancellationToken ct)
    {
        var config = site.Config;
        var siteDir = config.AbsoluteSiteDir;

        var webp = config.Optimize.ConvertImagesToWebp
            ? new WebpConverter(Path.Combine(config.ProjectRoot, ".cache", "webp"), config.Optimize.WebpQuality)
            : null;
        var converted = new List<string>();

        // 1. Theme assets -> site/assets
        if (Directory.Exists(ThemePaths.AssetsDir))
            await CopyDirectoryAsync(site, ThemePaths.AssetsDir, Path.Combine(siteDir, "assets"), webp, converted, ct);

        // 2. Docs static files (everything that isn't markdown) -> mirror into site.
        var docsDir = config.AbsoluteDocsDir;
        var customDir = string.IsNullOrEmpty(config.Theme.CustomDir)
            ? null
            : Path.GetFullPath(Path.Combine(config.ProjectRoot, config.Theme.CustomDir));

        foreach (var file in Directory.EnumerateFiles(docsDir, "*", SearchOption.AllDirectories))
        {
            if (SkipExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            var relative = Path.GetRelativePath(docsDir, file);
            if (relative.Split(Path.DirectorySeparatorChar, '/').Any(seg => seg.StartsWith('.'))) continue;
            if (customDir is not null && file.StartsWith(customDir, StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(siteDir, relative);
            await CopyAssetAsync(site, config, file, dest, webp, converted, ct);
        }

        // 3. Plugin-registered files.
        foreach (var (source, destRelative) in assets.Files)
        {
            if (!File.Exists(source)) continue;
            var dest = Path.Combine(siteDir, destRelative);
            await CopyAssetAsync(site, config, source, dest, webp, converted, ct);
        }

        return converted.Count == 0 ? WebpManifest.Empty : new WebpManifest(converted);
    }

    private static async Task CopyDirectoryAsync(SiteContext site, string source, string dest, WebpConverter? webp, List<string> converted, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            await CopyAssetAsync(site, site.Config, file, target, webp, converted, ct);
        }
    }

    /// <summary>
    /// Copies one asset, minifying CSS/JS text in place when the matching optimize toggle is on
    /// and emitting a <c>.webp</c> sibling for raster images when webp conversion is enabled.
    /// Already-minified files (<c>*.min.css</c>/<c>*.min.js</c>) are copied verbatim. Each webp
    /// that is actually produced is appended to <paramref name="converted"/> as a site-relative
    /// path, which becomes the manifest the HTML rewrite trusts.
    /// </summary>
    private static async Task CopyAssetAsync(SiteContext site, SiteConfig config, string source, string dest, WebpConverter? webp, List<string> converted, CancellationToken ct)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        var name = Path.GetFileName(source);
        var alreadyMin = name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase);

        if (!alreadyMin && ext == ".css" && config.Optimize.MinifyCss)
        {
            var min = CssJsMinifier.MinifyCss(await File.ReadAllTextAsync(source, ct));
            await OutputWriter.WriteTextIfChangedAsync(site, dest, min, ct);
            return;
        }
        if (!alreadyMin && ext == ".js" && config.Optimize.MinifyJs)
        {
            var min = CssJsMinifier.MinifyJs(await File.ReadAllTextAsync(source, ct));
            await OutputWriter.WriteTextIfChangedAsync(site, dest, min, ct);
            return;
        }

        // Copy the original, then emit a .webp sibling so the <picture> fallback still works.
        await OutputWriter.CopyIfChangedAsync(site, source, dest, ct);

        if (webp is not null && WebpConverter.IsConvertible(source))
        {
            var encoded = await webp.ConvertAsync(await File.ReadAllBytesAsync(source, ct), ct);
            if (encoded is not null)
            {
                var webpDest = Path.ChangeExtension(dest, ".webp");
                await OutputWriter.WriteBytesTrackedAsync(site, webpDest, encoded, ct);
                converted.Add(Path.GetRelativePath(config.AbsoluteSiteDir, webpDest).Replace('\\', '/'));
            }
        }
    }
}
