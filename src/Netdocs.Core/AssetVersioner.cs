using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Netdocs.Core;

/// <summary>
/// Appends a short content-hash query string (e.g. <c>?v=1a2b3c4d</c>) to local, unhashed
/// asset references so a changed file is fetched immediately instead of being served from a
/// browser/CDN cache until its max-age expires.
/// </summary>
/// <remarks>
/// Only local assets whose source file can be located are versioned: the bundled theme assets
/// (<c>assets/…</c>, e.g. <c>assets/netdocs.css</c>) and user <c>extra_css</c>/<c>extra_javascript</c>
/// resolved under the docs directory. Absolute/CDN URLs, protocol-relative URLs, and hrefs that
/// already carry a query string are returned unchanged, so the pre-hashed vendored Material assets
/// and external stylesheets are never touched.
/// </remarks>
public sealed class AssetVersioner
{
    /// <summary>A versioner that never rewrites hrefs (used when no build context is available).</summary>
    public static readonly AssetVersioner NoOp = new(null, null);

    private readonly string? _themeAssetsDir;
    private readonly string? _docsDir;
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);

    public AssetVersioner(string? themeAssetsDir, string? docsDir)
    {
        _themeAssetsDir = themeAssetsDir;
        _docsDir = docsDir;
    }

    /// <summary>Returns <paramref name="href"/> with a content-hash query appended, or unchanged if it is external or cannot be resolved to a local file.</summary>
    public string Version(string? href)
    {
        if (string.IsNullOrEmpty(href)) return href ?? "";
        if (href.Contains("://", StringComparison.Ordinal)
            || href.StartsWith("//", StringComparison.Ordinal)
            || href.StartsWith("data:", StringComparison.Ordinal)
            || href.Contains('?', StringComparison.Ordinal)
            || href.Contains('#', StringComparison.Ordinal))
        {
            return href;
        }

        var hash = _cache.GetOrAdd(href, ComputeHash);
        return hash is null ? href : href + "?v=" + hash;
    }

    private string? ComputeHash(string href)
    {
        var source = LocateSource(href);
        if (source is null) return null;
        try
        {
            var bytes = File.ReadAllBytes(source);
            return Convert.ToHexString(SHA256.HashData(bytes))[..8].ToLowerInvariant();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? LocateSource(string href)
    {
        var relative = href.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        if (_themeAssetsDir is not null
            && href.StartsWith("assets/", StringComparison.Ordinal))
        {
            var themeRelative = relative[("assets" + Path.DirectorySeparatorChar).Length..];
            var themePath = Path.Combine(_themeAssetsDir, themeRelative);
            if (File.Exists(themePath)) return themePath;
        }

        if (_docsDir is not null)
        {
            var docsPath = Path.Combine(_docsDir, relative);
            if (File.Exists(docsPath)) return docsPath;
        }

        return null;
    }
}
