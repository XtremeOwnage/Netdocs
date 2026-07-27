using System.Text.Json;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>
/// Emits client-side redirect pages (an <c>index.html</c> with a canonical link plus a
/// <c>meta refresh</c>) for every <c>source → target</c> mapping.
///
/// Mappings come from three places, merged together:
/// <list type="bullet">
///   <item>Per-page front matter — a page may list the old URLs it replaces under
///     <c>redirect_from</c> (or <c>aliases</c>), as a single string or a list. Each becomes a
///     redirect to that page's current URL. This keeps a page's old locations next to the page
///     itself, so a redirect travels with the content when it is renamed or moved.</item>
///   <item><c>redirect_maps</c> — an inline object of <c>{ "old/path": "new/url" }</c> pairs.</item>
///   <item><c>redirect_files</c> — a single path or an array of paths to JSON file(s) holding
///     bulk redirects. Each file may be either an object map <c>{ "old/path": "new/url" }</c>
///     or an array of objects <c>[{ "source": "old/path", "target": "new/url", "status": 308 }]</c>.
///     Paths are resolved relative to the project root, then the docs directory, then treated as
///     absolute. This lets large migration redirect tables live outside the config file.</item>
///   <item><c>slugify_redirects</c> — one or more <em>previous</em> slugify configurations
///     (each an object of <c>{ "case", "separator", "ascii" }</c>, or an array of them). When the
///     site's <c>slugify</c> settings change, every page's URL is re-slugified under each old
///     configuration; wherever the old slug differs from the current one, a redirect from the old
///     URL to the current URL is generated automatically. This means changing a slugify parameter
///     (for example the separator from <c>_</c> to <c>-</c>) does not silently break every existing
///     link — the old URLs keep resolving. Only URLs that the <em>current</em> slugify config would
///     itself produce are considered (so hand-authored, non-slugified paths are left untouched), and
///     a generated redirect is never emitted at a path already occupied by a real page.
///     Note: this reconstructs old URLs by re-slugifying the current URL, so it captures
///     <c>separator</c> and <c>case</c> changes; a change to <c>ascii</c> folding cannot be
///     reconstructed (the dropped characters are already gone) and should be handled with an explicit
///     <c>redirect_from</c>.</item>
/// </list>
/// Merge order (later wins): generated <c>slugify_redirects</c>, then per-page <c>redirect_from</c>,
/// then explicitly configured <c>redirect_maps</c>/<c>redirect_files</c>. So an explicit redirect
/// always overrides an automatically generated one for the same source.
/// </summary>
public sealed class RedirectsPlugin : IPlugin, IBuildHook
{
    private readonly Dictionary<string, string> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SlugifyConfig> _slugifyOldConfigs = [];

    public string Name => "redirects";

    public void Configure(IPluginContext ctx)
    {
        // Previous slugify configuration(s), used to regenerate old URLs after a slugify change.
        _slugifyOldConfigs.AddRange(ParseSlugifyConfigs(ctx.PluginOptions));

        // 1) External JSON file(s) first, so inline entries can override them.
        foreach (var file in FileList(ctx.PluginOptions))
        {
            var path = ResolveFile(ctx.Config, file);
            if (path is null)
            {
                ctx.Logger.LogWarning("redirects: file not found (looked relative to project root and docs dir): {File}", file);
                continue;
            }

            try
            {
                var count = LoadJsonFile(path);
                ctx.Logger.LogInformation("redirects: loaded {Count} redirect(s) from {File}", count, file);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                ctx.Logger.LogWarning(ex, "redirects: failed to read redirect file {File}", file);
            }
        }

        // 2) Inline map (overrides file entries with the same source).
        if (ctx.PluginOptions.TryGetValue("redirect_maps", out var m) && m is IReadOnlyDictionary<string, object?> map)
            foreach (var (source, targetObj) in map)
                AddEntry(source, targetObj?.ToString());
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        // Merge sources (lowest precedence first): generated slugify redirects, then per-page
        // front matter, then explicitly configured maps (which win).
        var redirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectSlugifyRedirects(site, redirects);
        CollectFrontMatterRedirects(site, redirects);
        foreach (var (source, target) in _maps)
            redirects[source] = target;

        foreach (var (source, target) in redirects)
        {
            var relative = OutputPathFor(source);
            var dest = Path.Combine(site.Config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar));

            var escaped = System.Net.WebUtility.HtmlEncode(target);
            var html = $"""
                <!doctype html>
                <html lang="en">
                <head>
                <meta charset="utf-8">
                <title>Redirecting…</title>
                <link rel="canonical" href="{escaped}">
                <meta http-equiv="refresh" content="0; url={escaped}">
                </head>
                <body>Redirecting to <a href="{escaped}">{escaped}</a>…</body>
                </html>
                """;
            await OutputWriter.WriteTextIfChangedAsync(site, dest, html, ct);
        }
    }

    /// <summary>Collects redirects declared in page front matter (<c>redirect_from</c> or
    /// <c>aliases</c>), each pointing at that page's current site-relative URL.</summary>
    private static void CollectFrontMatterRedirects(SiteContext site, Dictionary<string, string> into)
    {
        foreach (var page in site.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Url)) continue;
            var target = "/" + page.Url.TrimStart('/');

            foreach (var key in new[] { "redirect_from", "aliases" })
            {
                if (!page.FrontMatter.TryGetValue(key, out var value)) continue;
                foreach (var source in AsStrings(value))
                    into[source] = target;
            }
        }
    }

    /// <summary>Yields the trimmed string values of a front-matter scalar or list.</summary>
    private static IEnumerable<string> AsStrings(object? value)
    {
        switch (value)
        {
            case string s when s.Trim().Length > 0:
                yield return s.Trim();
                break;
            case IEnumerable<object?> list:
                foreach (var item in list)
                    if (item?.ToString() is { } s && s.Trim().Length > 0)
                        yield return s.Trim();
                break;
        }
    }

    /// <summary>Generates redirects for a slugify-configuration change: every page URL that the
    /// current slugify config would produce is re-slugified under each previously configured
    /// slugify config; where the old slug differs, a redirect from the old URL to the current URL
    /// is added. Sources that collide with a real page URL are skipped so a stub never clobbers an
    /// actual page.</summary>
    private void CollectSlugifyRedirects(SiteContext site, Dictionary<string, string> into)
    {
        if (_slugifyOldConfigs.Count == 0) return;

        var current = site.Config.Slugify;
        var pageUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in site.Pages)
            if (!string.IsNullOrWhiteSpace(page.Url))
                pageUrls.Add(page.Url.Trim().Trim('/'));

        foreach (var page in site.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Url)) continue;
            var cur = page.Url.Trim().TrimStart('/');

            // Only act on URLs the current slugify config would itself produce; this leaves
            // hand-authored, non-slugified paths (e.g. when slugify.urls is off) untouched.
            if (!string.Equals(ReSlugifyUrl(cur, current), EnsureTrailingSlash(cur), StringComparison.Ordinal))
                continue;

            var target = "/" + cur.TrimStart('/');
            foreach (var old in _slugifyOldConfigs)
            {
                var oldUrl = ReSlugifyUrl(cur, old);
                if (string.Equals(oldUrl, EnsureTrailingSlash(cur), StringComparison.Ordinal)) continue; // unchanged
                if (pageUrls.Contains(oldUrl.Trim('/'))) continue;                                        // would clobber a real page
                into[oldUrl] = target;
            }
        }
    }

    /// <summary>Re-slugifies each path segment of a site-relative URL under the given config,
    /// preserving <c>.</c> (matching content-URL slugification), and returns it with a trailing
    /// slash. An empty URL (site root) maps to the empty string.</summary>
    private static string ReSlugifyUrl(string url, SlugifyConfig config)
    {
        var trimmed = url.Trim().Trim('/');
        if (trimmed.Length == 0) return "";
        var segments = trimmed.Split('/');
        return string.Join('/', segments.Select(s => Slug.Make(s, config, "."))) + "/";
    }

    private static string EnsureTrailingSlash(string url)
    {
        var trimmed = url.Trim().Trim('/');
        return trimmed.Length == 0 ? "" : trimmed + "/";
    }

    /// <summary>Parses the <c>slugify_redirects</c> option, which may be a single object
    /// (<c>{ case, separator, ascii }</c>) or an array of such objects.</summary>
    private static IEnumerable<SlugifyConfig> ParseSlugifyConfigs(IReadOnlyDictionary<string, object?> options)
    {
        if (!options.TryGetValue("slugify_redirects", out var value) || value is null)
            yield break;

        switch (value)
        {
            case IReadOnlyDictionary<string, object?> single:
                yield return ToSlugifyConfig(single);
                break;
            case IEnumerable<object?> list:
                foreach (var item in list)
                    if (item is IReadOnlyDictionary<string, object?> m)
                        yield return ToSlugifyConfig(m);
                break;
        }
    }

    private static SlugifyConfig ToSlugifyConfig(IReadOnlyDictionary<string, object?> m) => new()
    {
        Case = m.TryGetValue("case", out var c) && c?.ToString() is { Length: > 0 } cs ? cs : "lower",
        Separator = m.TryGetValue("separator", out var s) && s?.ToString() is { } ss ? ss : "-",
        Ascii = m.TryGetValue("ascii", out var a) && ToBool(a),
    };

    private static bool ToBool(object? value) => value switch
    {
        bool b => b,
        string s => string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Maps a redirect source path to the relative output HTML file it is written to.</summary>
    private static string OutputPathFor(string source)
    {
        // Normalize: strip a leading slash so the path stays relative to the site directory
        // (a rooted path would make Path.Combine discard the site directory entirely).
        var s = source.Trim().Replace('\\', '/').TrimStart('/');

        if (s.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return s[..^3] + "/index.html";
        if (s.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return s;
        return s.TrimEnd('/') + "/index.html";
    }

    /// <summary>Reads the <c>redirect_files</c> option as a list of file paths (accepts a single
    /// string or an array of strings).</summary>
    private static IEnumerable<string> FileList(IReadOnlyDictionary<string, object?> options)
    {
        if (!options.TryGetValue("redirect_files", out var value) || value is null)
            yield break;

        switch (value)
        {
            case string single when single.Trim().Length > 0:
                yield return single.Trim();
                break;
            case IEnumerable<object?> list:
                foreach (var item in list)
                    if (item?.ToString() is { } s && s.Trim().Length > 0)
                        yield return s.Trim();
                break;
        }
    }

    /// <summary>Resolves a configured redirect-file path against the project root, then the docs
    /// dir, then as an absolute/working-directory path. Returns null when no candidate exists.</summary>
    private static string? ResolveFile(SiteConfig config, string file)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(config.ProjectRoot, file),
            Path.Combine(config.AbsoluteDocsDir, file),
            file,
        })
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    /// <summary>Loads a JSON redirect file (object map or array of {source,target}) and merges its
    /// entries. Returns the number of entries added.</summary>
    private int LoadJsonFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var before = _maps.Count;

        switch (root.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in root.EnumerateObject())
                    AddEntry(prop.Name, prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString());
                break;

            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var source = GetProp(item, "source") ?? GetProp(item, "from") ?? GetProp(item, "old");
                    var target = GetProp(item, "target") ?? GetProp(item, "to") ?? GetProp(item, "new");
                    AddEntry(source, target);
                }
                break;
        }

        return _maps.Count - before;
    }

    private static string? GetProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private void AddEntry(string? source, string? target)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) return;
        _maps[source.Trim()] = target.Trim();
    }
}
