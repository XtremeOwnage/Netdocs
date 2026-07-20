using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>Emits a Material-compatible <c>search/search_index.json</c> (page + per-section docs).</summary>
public sealed class SearchPlugin : IPlugin, IBuildHook
{
    private string _language = "en";

    public string Name => "search";

    public void Configure(IPluginContext ctx)
    {
        if (ctx.PluginOptions.TryGetValue("lang", out var lang) && lang is string l)
            _language = l;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        var parser = new HtmlParser();
        var docs = new List<SearchDoc>();

        foreach (var page in site.Pages)
        {
            var tags = ExtractTags(page);
            docs.Add(new SearchDoc(page.Url, page.Title, Collapse(page.PlainText), tags));

            foreach (var section in SplitSections(parser, page))
                docs.Add(section with { Tags = tags });
        }

        var fields = new Dictionary<string, object?>
        {
            ["title"] = new Dictionary<string, object?> { ["boost"] = 1000.0 },
            ["text"] = new Dictionary<string, object?> { ["boost"] = 1.0 },
            ["tags"] = new Dictionary<string, object?> { ["boost"] = 1000000.0 },
        };
        var index = new SearchIndex(
            new SearchConfig([_language], "[\\s\\-]+", ["stemmer", "stopWordFilter", "trimmer"], fields),
            docs);

        var dir = Path.Combine(site.Config.AbsoluteSiteDir, "search");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(index, SearchJson.Options);
        await File.WriteAllTextAsync(Path.Combine(dir, "search_index.json"), json, ct);
    }

    private static IEnumerable<SearchDoc> SplitSections(HtmlParser parser, Page page)
    {
        if (string.IsNullOrEmpty(page.HtmlContent)) yield break;
        var doc = parser.ParseDocument($"<body>{page.HtmlContent}</body>");
        var body = doc.Body;
        if (body is null) yield break;

        string? currentId = null, currentTitle = null;
        var buffer = new StringBuilder();

        foreach (var node in body.ChildNodes)
        {
            if (node is IElement el && el.TagName is "H1" or "H2" or "H3" && !string.IsNullOrEmpty(el.Id))
            {
                if (currentId is not null)
                    yield return new SearchDoc($"{page.Url}#{currentId}", currentTitle ?? "", Collapse(buffer.ToString()), []);
                currentId = el.Id;
                currentTitle = el.TextContent.Trim();
                buffer.Clear();
            }
            else
            {
                buffer.Append(node.TextContent).Append(' ');
            }
        }
        if (currentId is not null)
            yield return new SearchDoc($"{page.Url}#{currentId}", currentTitle ?? "", Collapse(buffer.ToString()), []);
    }

    private static string[] ExtractTags(Page page)
    {
        if (page.FrontMatter.TryGetValue("tags", out var t) && t is IEnumerable<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToArray();
        return [];
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed record SearchIndex(SearchConfig Config, IReadOnlyList<SearchDoc> Docs);
public sealed record SearchConfig(IReadOnlyList<string> Lang, string Separator, IReadOnlyList<string> Pipeline, IReadOnlyDictionary<string, object?> Fields);
public sealed record SearchDoc(string Location, string Title, string Text, string[] Tags);

internal static class SearchJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
