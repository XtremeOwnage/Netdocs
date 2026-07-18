using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Netdocs.Core;

/// <summary>
/// Ensures the bundled Material theme is available on disk at build time.
///
/// Normal (folder) publishes copy the theme next to the executable, so nothing needs to
/// happen. Single-file publishes (<c>PublishSingleFile=true</c>) ship only the executable,
/// so the theme is instead embedded as assembly resources and extracted here to a stable
/// per-version temp directory, with <see cref="ThemePaths.Root"/> repointed at it.
/// </summary>
public static class ThemeBootstrapper
{
    private const string ResourcePrefix = "theme/";
    private static readonly object Gate = new();
    private static bool _done;

    /// <summary>
    /// Materializes the theme if the loose copy next to the executable is missing.
    /// Safe to call multiple times; the work runs at most once per process.
    /// </summary>
    public static void EnsureExtracted()
    {
        if (_done) return;
        lock (Gate)
        {
            if (_done) return;

            // Loose theme present next to the executable (normal build/publish): use it as-is.
            if (!Directory.Exists(ThemePaths.TemplatesDir))
                ExtractEmbeddedTheme();

            _done = true;
        }
    }

    private static void ExtractEmbeddedTheme()
    {
        var assembly = typeof(ThemeBootstrapper).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(n => Normalize(n).StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToList();

        if (resources.Count == 0)
            return; // Nothing embedded (should not happen in a real build) - leave defaults.

        var version = assembly.GetName().Version?.ToString() ?? "0";
        var stamp = Stamp(resources, version);
        var root = Path.Combine(Path.GetTempPath(), "netdocs-theme", stamp);
        var marker = Path.Combine(root, ".complete");

        if (!File.Exists(marker))
        {
            Directory.CreateDirectory(root);
            foreach (var name in resources)
            {
                var relative = Normalize(name)[ResourcePrefix.Length..];
                var dest = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var file = File.Create(dest);
                stream.CopyTo(file);
            }
            File.WriteAllText(marker, stamp);
        }

        ThemePaths.Root = root;
    }

    /// <summary>Forward-slash resource name so paths compare consistently across OSes.</summary>
    private static string Normalize(string name) => name.Replace('\\', '/');

    /// <summary>Stable per-version fingerprint so extraction is reused and re-run on change.</summary>
    private static string Stamp(IEnumerable<string> resources, string version)
    {
        var sb = new StringBuilder(version).Append('|');
        foreach (var r in resources.OrderBy(x => x, StringComparer.Ordinal))
            sb.Append(Normalize(r)).Append(';');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return version + "-" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
