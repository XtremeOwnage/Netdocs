using Netdocs.Abstractions;
using Netdocs.Core.Content;
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
            await OutputWriter.CopyIfChangedAsync(site, file, dest, ct);
        }

        // 3. Plugin-registered files.
        foreach (var (source, destRelative) in assets.Files)
        {
            if (!File.Exists(source)) continue;
            var dest = Path.Combine(siteDir, destRelative);
            await OutputWriter.CopyIfChangedAsync(site, source, dest, ct);
        }
    }

    private static async Task CopyDirectoryAsync(SiteContext site, string source, string dest, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            await OutputWriter.CopyIfChangedAsync(site, file, target, ct);
        }
    }
}
