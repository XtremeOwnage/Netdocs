using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Optimization;
using Netdocs.Core.Plugins;

namespace Netdocs.Core;

/// <summary>Copies theme assets, docs static files, and plugin-registered files into the site output.</summary>
public static class AssetPipeline
{
    private static readonly string[] SkipExtensions = [".md", ".markdown"];

    public static async Task CopyAllAsync(SiteContext site, PluginAssets assets, CancellationToken ct)
    {
        var config = site.Config;
        var siteDir = config.AbsoluteSiteDir;

        // 1. Theme assets -> site/assets
        if (Directory.Exists(ThemePaths.AssetsDir))
            await CopyDirectoryAsync(site, ThemePaths.AssetsDir, Path.Combine(siteDir, "assets"), ct);

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
            await CopyAssetAsync(site, config, file, dest, ct);
        }

        // 3. Plugin-registered files.
        foreach (var (source, destRelative) in assets.Files)
        {
            if (!File.Exists(source)) continue;
            var dest = Path.Combine(siteDir, destRelative);
            await CopyAssetAsync(site, config, source, dest, ct);
        }
    }

    private static async Task CopyDirectoryAsync(SiteContext site, string source, string dest, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            await CopyAssetAsync(site, site.Config, file, target, ct);
        }
    }

    /// <summary>
    /// Copies one asset, minifying CSS/JS text in place when the matching optimize toggle is on.
    /// Already-minified files (<c>*.min.css</c>/<c>*.min.js</c>) are copied verbatim.
    /// </summary>
    private static async Task CopyAssetAsync(SiteContext site, SiteConfig config, string source, string dest, CancellationToken ct)
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

        await OutputWriter.CopyIfChangedAsync(site, source, dest, ct);
    }
}
