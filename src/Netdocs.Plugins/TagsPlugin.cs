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
        RenderTagIndex(site, index);
        return Task.CompletedTask;
    }

    /// <summary>Replaces the <c>&lt;!-- material/tags --&gt;</c> marker with a rendered, hierarchical tag index.</summary>
    private static void RenderTagIndex(SiteContext site, SortedDictionary<string, List<Page>> index)
    {
        const string marker = "<!-- material/tags -->";

        // Build a tree from '/'-separated tag paths so parents nest their children.
        var root = new TagNode();
        foreach (var (tag, pages) in index)
        {
            var node = root;
            var path = "";
            foreach (var segment in tag.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                path = path.Length == 0 ? segment : path + "/" + segment;
                if (!node.Children.TryGetValue(segment, out var child))
                    node.Children[segment] = child = new TagNode { FullPath = path };
                node = child;
            }
            node.Pages = pages;
        }

        var markdown = new System.Text.StringBuilder();
        RenderNode(root, 0, markdown);

        foreach (var page in site.Pages)
        {
            if (page.RawMarkdown.Contains(marker, StringComparison.Ordinal))
                page.RawMarkdown = page.RawMarkdown.Replace(marker, markdown.ToString());
        }
    }

    private static void RenderNode(TagNode node, int depth, System.Text.StringBuilder markdown)
    {
        foreach (var child in node.Children.Values)
        {
            var level = Math.Min(2 + depth, 6);
            markdown.Append('\n').Append(new string('#', level)).Append(' ').AppendLine(child.FullPath).AppendLine();
            if (child.Pages is { Count: > 0 })
            {
                foreach (var page in child.Pages.DistinctBy(p => p.Url).OrderBy(GetDisplayTitle, StringComparer.OrdinalIgnoreCase))
                    markdown.Append("- [").Append(GetDisplayTitle(page)).Append("](/").Append(page.Url).AppendLine(")");
                markdown.AppendLine();
            }
            RenderNode(child, depth + 1, markdown);
        }
    }

    private sealed class TagNode
    {
        public string FullPath = "";
        public SortedDictionary<string, TagNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Page>? Pages { get; set; }
    }

    /// <summary>Display name for a page on the tags page: an explicit <c>tags_title</c> front-matter
    /// override wins, then the resolved title, then the first H1, then the filename.</summary>
    private static string GetDisplayTitle(Page page)
    {
        if (page.FrontMatter.TryGetValue("tags_title", out var tt) && tt is string tts && tts.Length > 0)
            return tts;
        if (!string.IsNullOrEmpty(page.Title)) return page.Title;
        if (page.FrontMatter.TryGetValue("title", out var t) && t is string ft && ft.Length > 0) return ft;
        foreach (var line in page.RawMarkdown.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].Trim();
        }
        var name = System.IO.Path.GetFileNameWithoutExtension(page.RelativePath);
        return name.Replace('-', ' ').Replace('_', ' ');
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        if (!_export || site.State["tags"] is not SortedDictionary<string, List<Page>> index) return;

        var export = index.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(p => new { title = GetDisplayTitle(p), url = p.Url }).ToArray());

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
