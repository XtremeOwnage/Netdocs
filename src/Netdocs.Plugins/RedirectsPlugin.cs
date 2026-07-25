using System.Text.Json;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>
/// Emits client-side redirect pages (an <c>index.html</c> with a canonical link plus a
/// <c>meta refresh</c>) for every <c>source → target</c> mapping.
///
/// Mappings come from two places, merged together:
/// <list type="bullet">
///   <item><c>redirect_maps</c> — an inline object of <c>{ "old/path": "new/url" }</c> pairs.</item>
///   <item><c>redirect_files</c> — a single path or an array of paths to JSON file(s) holding
///     bulk redirects. Each file may be either an object map <c>{ "old/path": "new/url" }</c>
///     or an array of objects <c>[{ "source": "old/path", "target": "new/url", "status": 308 }]</c>.
///     Paths are resolved relative to the project root, then the docs directory, then treated as
///     absolute. This lets large migration redirect tables live outside the config file.</item>
/// </list>
/// Later entries win on conflict, so an inline <c>redirect_maps</c> value overrides one loaded
/// from a file with the same source.
/// </summary>
public sealed class RedirectsPlugin : IPlugin, IBuildHook
{
    private readonly Dictionary<string, string> _maps = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "redirects";

    public void Configure(IPluginContext ctx)
    {
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
        foreach (var (source, target) in _maps)
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
