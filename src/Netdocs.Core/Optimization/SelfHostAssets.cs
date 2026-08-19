using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Self-hosts external CDN assets for offline usage. Scans emitted HTML for external
/// <c>&lt;script src&gt;</c>, <c>&lt;img src&gt;</c>, subresource-fetching <c>&lt;link&gt;</c> tags
/// (see <see cref="SelfHostableLinkRels"/>), and dynamic <c>import()</c> calls, downloads each
/// once into <c>assets/external/</c>, and rewrites every page to point at the local copies with
/// page-relative paths so the site works from <c>file://</c>.
/// <para>
/// Two kinds of reference pull in more than one file. A downloaded stylesheet has its
/// <c>url(...)</c> targets fetched into the same directory (web fonts, images). An ES module has
/// its relative imports followed recursively into a mirror of its own directory layout, because a
/// bundled entry is typically a stub that loads its real code from sibling chunks at runtime — see
/// <see cref="DownloadModuleGraphAsync"/>.
/// </para>
/// <para>
/// Only the attribute spans that matched are rewritten. Outbound links to the same URL — an
/// <c>&lt;a href&gt;</c>, a <c>&lt;link rel=canonical&gt;</c>, a redirect's meta refresh — are left
/// exactly as authored.
/// </para>
/// </summary>
public static partial class SelfHostAssets
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string ExternalDir = "assets/external";

    /// <summary>Ceiling on files pulled for one module graph, so a pathological (or circular-by-URL)
    /// dependency tree cannot download without bound. Mermaid, the heaviest real case here, is ~53.</summary>
    private const int MaxModulesPerGraph = 500;

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

        // 1. Collect every external URL referenced by an asset tag across all pages, together with
        //    the exact span each URL occupies so the rewrite in step 3 can be surgical.
        var urls = new Dictionary<string, bool>(StringComparer.Ordinal);
        var fileText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileRefs = new Dictionary<string, List<AssetRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in htmlFiles)
        {
            var text = await File.ReadAllTextAsync(f, ct);
            var refs = ExtractAssetRefs(text).ToList();
            if (refs.Count == 0) continue;
            fileText[f] = text;
            fileRefs[f] = refs;
            // A URL referenced both ways is treated as a module: following its imports is the
            // superset of behaviours, and a non-module never has any to follow.
            foreach (var r in refs) urls[r.Url] = urls.GetValueOrDefault(r.Url) || r.Module;
        }
        if (urls.Count == 0) return;

        // 2. Download each URL once (plus CSS-referenced assets), mapping url -> local filename.
        var absExternalDir = Path.Combine(siteDir, ExternalDir);
        Directory.CreateDirectory(absExternalDir);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = 0;
        foreach (var (url, isModule) in urls)
        {
            var local = isModule
                ? await DownloadModuleGraphAsync(url, absExternalDir, site, fetch, log, ct)
                : await DownloadAsync(url, absExternalDir, site, fetch, log, ct);
            if (local is null) { failures++; continue; }
            map[url] = local;
        }

        // 3. Rewrite each HTML file, replacing external URLs with page-relative local paths.
        //    Only the spans found in step 1 are touched — a plain string replace would also clobber
        //    the same URL where it legitimately appears as navigable content (an <a href>, a meta
        //    refresh, JSON-LD), turning outbound links into downloads of a self-hosted copy.
        var rewritten = 0;
        foreach (var (path, original) in fileText)
        {
            var pageDir = Path.GetDirectoryName(path)!;
            var sb = new StringBuilder(original.Length);
            var cursor = 0;
            foreach (var r in fileRefs[path].OrderBy(r => r.Start))
            {
                if (r.Start < cursor) continue; // overlapping match — first one wins
                if (!map.TryGetValue(r.Url, out var local)) continue; // download failed; keep the CDN URL
                sb.Append(original, cursor, r.Start - cursor);
                sb.Append(RelativePath(pageDir, Path.Combine(absExternalDir, local.Replace('/', Path.DirectorySeparatorChar))));
                cursor = r.Start + r.Length;
            }
            if (cursor == 0) continue; // nothing replaced
            sb.Append(original, cursor, original.Length - cursor);

            var updated = sb.ToString();
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

    /// <summary>
    /// Self-hosts an ES module together with everything it imports, mirroring the module's own
    /// directory layout under <c>assets/external/&lt;hash&gt;/</c>.
    ///
    /// <para>Mirroring is the point. A bundled ESM entry is usually a stub that pulls dozens of
    /// sibling chunks at runtime (<c>mermaid.esm.min.mjs</c> alone imports 52 of them), and those
    /// specifiers are relative. Downloading only the entry into the flat asset directory leaves
    /// every one of them resolving to a path that was never written, so the import rejects and the
    /// feature silently dies offline. Reproducing the layout means the specifiers resolve as-is and
    /// no JavaScript has to be rewritten.</para>
    ///
    /// <para>Returns the entry's path relative to <paramref name="dir"/>, or null if it could not
    /// be downloaded. A dependency that fails is logged and skipped: a partial graph is no worse
    /// than the CDN URL it replaces, and failing the whole build over one chunk is worse than both.</para>
    /// </summary>
    private static async Task<string?> DownloadModuleGraphAsync(string url, string dir, SiteContext site, Fetcher fetch, ILogger log, CancellationToken ct)
    {
        Uri entry;
        try { entry = new Uri(url); }
        catch (UriFormatException) { log.LogWarning("Offline: {Url} is not a valid module URL.", url); return null; }

        // Everything is stored relative to the entry's own directory, so `./chunks/x.mjs` lands at
        // <root>/chunks/x.mjs exactly as the module expects to find it.
        var baseUri = new Uri(entry, ".");
        var root = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8].ToLowerInvariant();
        var rootDir = Path.Combine(dir, root);

        var entryName = RelativeUnder(baseUri, entry);
        if (entryName is null) { log.LogWarning("Offline: could not place module {Url}.", url); return null; }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(Uri Url, string RelPath)>();
        queue.Enqueue((entry, entryName));
        visited.Add(entry.ToString());

        var downloaded = 0;
        var entryWritten = false;
        while (queue.Count > 0)
        {
            var (moduleUrl, relPath) = queue.Dequeue();
            if (downloaded >= MaxModulesPerGraph)
            {
                log.LogWarning("Offline: module graph for {Url} exceeded {Max} files; the rest still point at their CDN.", url, MaxModulesPerGraph);
                break;
            }

            byte[] bytes;
            try
            {
                var result = await fetch(moduleUrl.ToString(), ct);
                if (result is not { } r)
                {
                    log.LogWarning("Offline: failed to download module {Url}.", moduleUrl);
                    if (moduleUrl == entry) return null;
                    continue;
                }
                bytes = r.Bytes;
            }
            catch (Exception ex)
            {
                log.LogWarning("Offline: failed to download module {Url}: {Message}", moduleUrl, ex.Message);
                if (moduleUrl == entry) return null;
                continue;
            }

            var outPath = Path.Combine(rootDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            await File.WriteAllBytesAsync(outPath, bytes, ct);
            site.TrackOutput(outPath);
            downloaded++;
            if (moduleUrl == entry) entryWritten = true;

            foreach (var specifier in RelativeSpecifiers(Encoding.UTF8.GetString(bytes)))
            {
                if (!Uri.TryCreate(moduleUrl, specifier, out var dep)) continue;
                if (!visited.Add(dep.ToString())) continue;

                var depPath = RelativeUnder(baseUri, dep);
                if (depPath is null)
                {
                    // Resolves outside the entry's directory, so it has nowhere to live in the
                    // mirrored tree. Rare for a bundled library; worth saying out loud when it happens.
                    log.LogWarning("Offline: module {Dep} sits outside {Base} and was skipped.", dep, baseUri);
                    continue;
                }
                queue.Enqueue((dep, depPath));
            }
        }

        return entryWritten ? $"{root}/{entryName}" : null;
    }

    /// <summary>Relative import specifiers (<c>./x</c>, <c>../x</c>) named by a module's source.
    /// Bare specifiers are ignored: a browser cannot resolve them without an import map, so a
    /// CDN-built bundle never emits one.</summary>
    private static IEnumerable<string> RelativeSpecifiers(string source)
    {
        foreach (Match m in ModuleSpecifierRegex().Matches(source))
            yield return m.Groups[1].Value;
    }

    /// <summary>The path of <paramref name="target"/> relative to <paramref name="baseUri"/>, or
    /// null when it escapes that directory (or is not a child path we can safely write).</summary>
    private static string? RelativeUnder(Uri baseUri, Uri target)
    {
        if (!string.Equals(baseUri.Host, target.Host, StringComparison.OrdinalIgnoreCase)) return null;

        var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(target).ToString());
        if (relative.Length == 0) return null;

        var segments = new List<string>();
        foreach (var raw in relative.Split('/'))
        {
            var segment = raw.Split('?')[0].Split('#')[0];
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == ".." || Path.IsPathRooted(segment)) return null;
            segments.Add(Sanitize(segment));
        }
        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>True when a URL names an ES module by extension alone.</summary>
    private static bool IsModuleUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.AbsolutePath.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>An external URL to self-host, and where it sits in the page it was found on.
    /// <paramref name="Module"/> marks an ES module, whose own relative imports must be followed.</summary>
    private readonly record struct AssetRef(int Start, int Length, string Url, bool Module);

    /// <summary>
    /// <c>rel</c> values that actually fetch a subresource, and so are safe to self-host. Everything
    /// else a <c>&lt;link&gt;</c> can express is navigational metadata pointing at a document —
    /// <c>canonical</c>, <c>alternate</c>, <c>prev</c>/<c>next</c>, <c>author</c> — or a hint that
    /// fetches a whole page rather than an asset (<c>prefetch</c>, <c>prerender</c>, used by instant
    /// navigation). Downloading those and repointing the page at a local copy is always wrong.
    /// </summary>
    private static readonly HashSet<string> SelfHostableLinkRels = new(StringComparer.OrdinalIgnoreCase)
    {
        "stylesheet", "icon", "apple-touch-icon", "apple-touch-icon-precomposed", "mask-icon",
        "manifest", "preload", "modulepreload",
    };

    private static IEnumerable<AssetRef> ExtractAssetRefs(string html)
    {
        foreach (Match m in ScriptImgRegex().Matches(html))
            yield return RefFor(m, ModuleTypeRegex().IsMatch(m.Value));
        foreach (Match m in LinkRegex().Matches(html))
        {
            if (!IsSelfHostableLink(m.Value)) continue;
            yield return RefFor(m, m.Value.Contains("modulepreload", StringComparison.OrdinalIgnoreCase));
        }
        // A dynamic import() always names an ES module.
        foreach (Match m in ImportRegex().Matches(html)) yield return RefFor(m, module: true);

        static AssetRef RefFor(Match m, bool module)
        {
            var g = m.Groups[1];
            return new AssetRef(g.Index, g.Length, g.Value, module || IsModuleUrl(g.Value));
        }
    }

    /// <summary>True when a <c>&lt;link&gt;</c> tag references a subresource worth self-hosting.</summary>
    private static bool IsSelfHostableLink(string tag)
    {
        var rel = RelAttrRegex().Match(tag);
        // A <link> without rel has no defined relationship — treat it as navigational and leave it.
        if (!rel.Success) return false;
        foreach (var token in rel.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (SelfHostableLinkRels.Contains(token)) return true;
        return false;
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

    [GeneratedRegex("\\srel=[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex RelAttrRegex();

    [GeneratedRegex("import\\(\\s*[\"'](https?://[^\"']+)[\"']\\s*\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ImportRegex();

    [GeneratedRegex("url\\(\\s*[\"']?([^\"')]+)[\"']?\\s*\\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex InvalidCharRegex();

    // `from "./x"`, `import "./x"`, `import("./x")` and `export ... from "./x"`, minified or not.
    // Restricted to relative specifiers, which keeps ordinary strings in the source out of it.
    [GeneratedRegex("(?:\\bfrom|\\bimport)\\s*\\(?\\s*[\"']([.][^\"']*)[\"']")]
    private static partial Regex ModuleSpecifierRegex();

    [GeneratedRegex("\\stype=[\"']module[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ModuleTypeRegex();
}
