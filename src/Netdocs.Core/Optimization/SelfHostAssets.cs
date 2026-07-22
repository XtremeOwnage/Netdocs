using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Self-hosts external CDN assets for offline usage. Scans emitted HTML for external
/// <c>&lt;script src&gt;</c>, <c>&lt;link href&gt;</c>, <c>&lt;img src&gt;</c>, and the Mermaid
/// dynamic <c>import()</c>, downloads each once into <c>assets/external/</c> (recursively fetching
/// <c>url(...)</c> references inside downloaded CSS, e.g. web-font files), and rewrites every page
/// to point at the local copies with page-relative paths so the site works from <c>file://</c>.
/// </summary>
public static partial class SelfHostAssets
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string ExternalDir = "assets/external";

    /// <summary>Fetches a URL, returning its bytes and response media type (or null on failure).</summary>
    public delegate Task<(byte[] Bytes, string? MediaType)?> Fetcher(string url, CancellationToken ct);

    public static Task RunAsync(SiteContext site, ILogger log, CancellationToken ct) =>
        RunAsync(site, log, DefaultFetch, ct);

    private static async Task<(byte[], string?)?> DefaultFetch(string url, CancellationToken ct)
    {
        var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return (bytes, resp.Content.Headers.ContentType?.MediaType);
    }

    public static async Task RunAsync(SiteContext site, ILogger log, Fetcher fetch, CancellationToken ct)
    {
        var siteDir = Path.GetFullPath(site.Config.AbsoluteSiteDir);
        var htmlFiles = Directory.EnumerateFiles(siteDir, "*.html", SearchOption.AllDirectories).ToList();
        if (htmlFiles.Count == 0) return;

        // 1. Collect every external URL referenced by an asset tag across all pages.
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var fileText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in htmlFiles)
        {
            var text = await File.ReadAllTextAsync(f, ct);
            fileText[f] = text;
            foreach (var u in ExtractAssetUrls(text)) urls.Add(u);
        }
        if (urls.Count == 0) return;

        // 2. Download each URL once (plus CSS-referenced assets), mapping url -> local filename.
        var absExternalDir = Path.Combine(siteDir, ExternalDir);
        Directory.CreateDirectory(absExternalDir);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = 0;
        foreach (var url in urls)
        {
            var local = await DownloadAsync(url, absExternalDir, site, fetch, log, ct);
            if (local is null) { failures++; continue; }
            map[url] = local;
        }

        // 3. Rewrite each HTML file, replacing external URLs with page-relative local paths.
        var rewritten = 0;
        foreach (var (path, original) in fileText)
        {
            var pageDir = Path.GetDirectoryName(path)!;
            var updated = original;
            foreach (var (url, local) in map)
            {
                var rel = RelativePath(pageDir, Path.Combine(absExternalDir, local));
                updated = updated.Replace(url, rel, StringComparison.Ordinal);
            }
            if (updated != original)
            {
                await File.WriteAllTextAsync(path, updated, ct);
                rewritten++;
            }
        }

        if (map.Count > 0)
            log.LogInformation("Offline: self-hosted {Count} external asset(s) into {Dir} across {Files} page(s).",
                map.Count, ExternalDir, rewritten);
        if (failures > 0)
            log.LogWarning("Offline: {Count} external asset(s) could not be downloaded and still point at their CDN.", failures);
    }

    /// <summary>Downloads one URL into <paramref name="dir"/>, returning the local filename, and
    /// recursively self-hosts <c>url(...)</c> references inside downloaded CSS.</summary>
    private static async Task<string?> DownloadAsync(string url, string dir, SiteContext site, Fetcher fetch, ILogger log, CancellationToken ct)
    {
        try
        {
            var result = await fetch(url, ct);
            if (result is not { } r) { log.LogWarning("Offline: failed to download {Url}.", url); return null; }
            var (bytes, mediaType) = r;
            var name = LocalNameFor(url, mediaType);

            if (name.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                var css = Encoding.UTF8.GetString(bytes);
                css = await InlineCssReferencesAsync(css, url, dir, site, fetch, log, ct);
                bytes = Encoding.UTF8.GetBytes(css);
            }

            var outPath = Path.Combine(dir, name);
            await File.WriteAllBytesAsync(outPath, bytes, ct);
            site.TrackOutput(outPath);
            return name;
        }
        catch (Exception ex)
        {
            log.LogWarning("Offline: failed to download {Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    /// <summary>Rewrites absolute <c>url(...)</c> targets in a downloaded stylesheet to local
    /// filenames (same directory), downloading each referenced file (fonts, images).</summary>
    private static async Task<string> InlineCssReferencesAsync(string css, string cssUrl, string dir,
        SiteContext site, Fetcher fetch, ILogger log, CancellationToken ct)
    {
        var matches = CssUrlRegex().Matches(css).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        foreach (var raw in matches)
        {
            var abs = ToAbsolute(raw, cssUrl);
            if (abs is null) continue; // data: or unresolvable
            var local = await DownloadAsync(abs, dir, site, fetch, log, ct);
            if (local is not null) css = css.Replace(raw, local, StringComparison.Ordinal);
        }
        return css;
    }

    private static IEnumerable<string> ExtractAssetUrls(string html)
    {
        foreach (Match m in ScriptImgRegex().Matches(html)) yield return m.Groups[1].Value;
        foreach (Match m in LinkRegex().Matches(html))
        {
            var tag = m.Value;
            // Preconnect / dns-prefetch hints don't fetch an asset — skip them.
            if (tag.Contains("preconnect", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("dns-prefetch", StringComparison.OrdinalIgnoreCase)) continue;
            yield return m.Groups[1].Value;
        }
        foreach (Match m in ImportRegex().Matches(html)) yield return m.Groups[1].Value;
    }

    private static string LocalNameFor(string url, string? mediaType)
    {
        var uri = new Uri(url);
        var last = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrEmpty(last) || !last.Contains('.'))
        {
            var ext = ExtensionFor(mediaType) ?? ".bin";
            last = (string.IsNullOrEmpty(last) ? "asset" : last) + ext;
        }
        // Prefix a short hash of the full URL to avoid collisions between same-named files.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8].ToLowerInvariant();
        return $"{hash}-{Sanitize(last)}";
    }

    private static string? ExtensionFor(string? mediaType) => mediaType switch
    {
        "text/css" => ".css",
        "application/javascript" or "text/javascript" => ".js",
        "font/woff2" => ".woff2",
        "font/woff" => ".woff",
        "image/png" => ".png",
        "image/svg+xml" => ".svg",
        _ => null,
    };

    private static string? ToAbsolute(string reference, string baseUrl)
    {
        if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        return Uri.TryCreate(new Uri(baseUrl), reference, out var abs) ? abs.ToString() : null;
    }

    private static string RelativePath(string fromDir, string toFile) =>
        Path.GetRelativePath(fromDir, toFile).Replace('\\', '/');

    private static string Sanitize(string name) =>
        InvalidCharRegex().Replace(name, "_");

    [GeneratedRegex("<(?:script|img)\\b[^>]*?\\ssrc=[\"'](https?://[^\"']+)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptImgRegex();

    [GeneratedRegex("<link\\b[^>]*?\\shref=[\"'](https?://[^\"']+)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("import\\(\\s*[\"'](https?://[^\"']+)[\"']\\s*\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ImportRegex();

    [GeneratedRegex("url\\(\\s*[\"']?([^\"')]+)[\"']?\\s*\\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex InvalidCharRegex();
}
