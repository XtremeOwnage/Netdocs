using System.Text.Json;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>Collects page tags, hides shadow tags in production, and exports tags.json.</summary>
public sealed class TagsPlugin : IPlugin, IBuildHook
{
    private bool _export = true;
    private string _exportFile = "tags.json";
    private bool _shadow;
    private bool _shadowOnServe = true;
    private string _shadowPrefix = "_";
    private readonly HashSet<string> _shadowTags = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "tags";

    public void Configure(IPluginContext ctx)
    {
        var o = ctx.PluginOptions;
        _export = !o.TryGetValue("export", out var e) || e is not bool eb || eb;
        if (o.TryGetValue("export_file", out var ef) && ef is string efs) _exportFile = efs;
        _shadow = o.TryGetValue("shadow", out var s) && s is bool sb && sb;
        _shadowOnServe = !o.TryGetValue("shadow_on_serve", out var sos) || sos is not bool sosb || sosb;
        if (o.TryGetValue("shadow_tags_prefix", out var p) && p is string ps) _shadowPrefix = ps;
        if (o.TryGetValue("shadow_tags", out var st) && st is IEnumerable<object?> list)
            foreach (var t in list) if (t is not null) _shadowTags.Add(t.ToString()!);
    }

    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        var hideShadow = _shadow && !(site.Options.IsServe && _shadowOnServe);
        var index = new SortedDictionary<string, List<Page>>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in site.Pages)
        {
            if (!page.FrontMatter.TryGetValue("tags", out var raw) || raw is not IEnumerable<object?> list)
                continue;

            var tags = new List<string>();
            foreach (var item in list)
            {
                var tag = item?.ToString();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                if (hideShadow && IsShadow(tag)) continue;
                tags.Add(tag);
                (index.TryGetValue(tag, out var pages) ? pages : index[tag] = []).Add(page);
            }
            page.Meta["tags"] = tags;
        }

        site.State["tags"] = index;
        return Task.CompletedTask;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        if (!_export || site.State["tags"] is not SortedDictionary<string, List<Page>> index) return;

        var export = index.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(p => new { title = p.Title, url = p.Url }).ToArray());

        var path = Path.Combine(site.Config.AbsoluteSiteDir, _exportFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
    }

    private bool IsShadow(string tag)
    {
        var bare = tag.StartsWith(_shadowPrefix, StringComparison.Ordinal) ? tag[_shadowPrefix.Length..] : tag;
        return tag.StartsWith(_shadowPrefix, StringComparison.Ordinal) || _shadowTags.Contains(bare) || _shadowTags.Contains(tag);
    }
}
