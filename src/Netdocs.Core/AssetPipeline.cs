using Netdocs.Abstractions;
using Netdocs.Core.Plugins;

namespace Netdocs.Core;

/// <summary>Copies theme assets, docs static files, and plugin-registered files into the site output.</summary>
public static class AssetPipeline
{
    private static readonly string[] SkipExtensions = [".md", ".markdown"];

    public static async Task CopyAllAsync(SiteConfig config, PluginAssets assets, CancellationToken ct)
    {
        var siteDir = config.AbsoluteSiteDir;

        // 1. Theme assets -> site/assets
        if (Directory.Exists(ThemePaths.AssetsDir))
            CopyDirectory(ThemePaths.AssetsDir, Path.Combine(siteDir, "assets"));

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
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        // 3. Plugin-registered files.
        foreach (var (source, destRelative) in assets.Files)
        {
            if (!File.Exists(source)) continue;
            var dest = Path.Combine(siteDir, destRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
        }

        await Task.CompletedTask;
    }

    private static void CopyDirectory(string source, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
