using System.Text.Json;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>Collects page tags, hides shadow tags in production, and exports tags.json.</summary>
public sealed partial class TagsPlugin : IPlugin, IBuildHook
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

    /// <summary>
    /// Replaces every <c>&lt;!-- material/tags --&gt;</c> marker with a rendered, hierarchical tag
    /// index. The marker may carry an optional scope object to filter which tags are listed:
    /// <c>&lt;!-- material/tags { include: [Foo, Bar] } --&gt;</c> or
    /// <c>&lt;!-- material/tags { exclude: [Baz] } --&gt;</c>. A scope entry matches a tag when it
    /// equals the tag or is a parent category of it (e.g. <c>Foo</c> matches <c>Foo</c> and
    /// <c>Foo/Sub</c>). Each marker is rendered independently against its own scope.
    /// </summary>
    private static void RenderTagIndex(SiteContext site, SortedDictionary<string, List<Page>> index)
    {
        var regex = TagMarkerRegex();
        var scopedRendered = 0;
        var scopedEmpty = 0;

        foreach (var page in site.Pages)
        {
            if (!page.RawMarkdown.Contains("material/tags", StringComparison.Ordinal))
                continue;

            page.RawMarkdown = regex.Replace(page.RawMarkdown, match =>
            {
                var cfg = match.Groups["cfg"].Success ? match.Groups["cfg"].Value : null;
                var (include, exclude) = ParseScope(cfg);
                var scoped = FilterIndex(index, include, exclude);

                scopedRendered++;
                if (scoped.Count == 0) scopedEmpty++;

                var root = BuildTree(scoped);
                var markdown = new System.Text.StringBuilder();
                RenderNode(root, 0, markdown);
                return markdown.ToString();
            });
        }

        // A tags page with an empty index renders as a blank body, which looks like a bug. Surface
        // why: no page declared front-matter tags, they were all hidden as shadow tags, or a
        // marker's include/exclude scope matched nothing.
        if (scopedEmpty > 0)
        {
            var log = site.LoggerFactory.CreateLogger("tags");
            log.LogWarning(
                "{Empty} of {Total} '<!-- material/tags -->' marker(s) produced an empty tag list; " +
                "those sections will render blank. Add 'tags:' front matter to pages, verify the " +
                "marker's include/exclude scope, or check shadow-tag settings.",
                scopedEmpty, scopedRendered);
        }
    }

    /// <summary>Builds a hierarchical <see cref="TagNode"/> tree from '/'-separated tag paths so
    /// parent categories nest their children.</summary>
    private static TagNode BuildTree(SortedDictionary<string, List<Page>> index)
    {
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
        return root;
    }

    /// <summary>Filters the tag index by optional include/exclude category lists. When
    /// <paramref name="include"/> is non-empty a tag is kept only if it matches one of its entries;
    /// any tag matching an <paramref name="exclude"/> entry is dropped. Matching is prefix-aware so
    /// a category like <c>Application</c> also selects <c>Application/AWS</c>.</summary>
    private static SortedDictionary<string, List<Page>> FilterIndex(
        SortedDictionary<string, List<Page>> index, List<string> include, List<string> exclude)
    {
        if (include.Count == 0 && exclude.Count == 0)
            return index;

        var result = new SortedDictionary<string, List<Page>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, pages) in index)
        {
            if (include.Count > 0 && !include.Any(e => MatchesCategory(tag, e))) continue;
            if (exclude.Any(e => MatchesCategory(tag, e))) continue;
            result[tag] = pages;
        }
        return result;
    }

    /// <summary>True when <paramref name="tag"/> equals <paramref name="entry"/> or sits beneath it
    /// as a nested category (<c>entry/…</c>), case-insensitively.</summary>
    private static bool MatchesCategory(string tag, string entry) =>
        tag.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses a marker scope object like <c>include: [A, B], exclude: [C]</c> into two
    /// lists. Values may be bare, single- or double-quoted, and separated by commas.</summary>
    private static (List<string> Include, List<string> Exclude) ParseScope(string? cfg)
    {
        var include = new List<string>();
        var exclude = new List<string>();
        if (string.IsNullOrWhiteSpace(cfg)) return (include, exclude);

        foreach (var key in new[] { "include", "exclude" })
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                cfg, key + @"\s*:\s*\[(?<vals>[^\]]*)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var target = key == "include" ? include : exclude;
            foreach (var raw in m.Groups["vals"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var val = raw.Trim().Trim('"', '\'').Trim();
                if (val.Length > 0) target.Add(val);
            }
        }
        return (include, exclude);
    }

    // Matches the bare marker and the scoped variant, capturing the optional { … } body.
    [System.Text.RegularExpressions.GeneratedRegex(
        @"<!--\s*material/tags\s*(\{(?<cfg>[^}]*)\})?\s*-->",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex TagMarkerRegex();

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

    /// <summary>Display name for a page on the tags page: an explicit <c>tag_title</c> (or legacy
    /// <c>tags_title</c>) front-matter override wins, then the resolved title, then the first H1,
    /// then the filename.</summary>
    private static string GetDisplayTitle(Page page)
    {
        if (!string.IsNullOrWhiteSpace(page.TagTitle))
            return page.TagTitle!;
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
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        await OutputWriter.WriteTextIfChangedAsync(site, path, json, ct);
    }

    private bool IsShadow(string tag)
    {
        var bare = tag.StartsWith(_shadowPrefix, StringComparison.Ordinal) ? tag[_shadowPrefix.Length..] : tag;
        return tag.StartsWith(_shadowPrefix, StringComparison.Ordinal) || _shadowTags.Contains(bare) || _shadowTags.Contains(tag);
    }
}
