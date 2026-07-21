using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>Emits a Material-compatible <c>search/search_index.json</c> (page + per-section docs).</summary>
public sealed class SearchPlugin : IPlugin, IBuildHook
{
    private static readonly string[] DefaultPipeline = ["stemmer", "stopWordFilter", "trimmer"];
    private const string DefaultSeparator = "[\\s\\-]+";

    private IReadOnlyList<string> _languages = ["en"];
    private string _separator = DefaultSeparator;
    private IReadOnlyList<string> _pipeline = DefaultPipeline;

    public string Name => "search";

    public void Configure(IPluginContext ctx)
    {
        var opts = ctx.PluginOptions;

        // `lang` accepts a single language ("en") or a list (["en", "de"]).
        if (opts.TryGetValue("lang", out var lang))
            _languages = AsStringList(lang) is { Count: > 0 } list ? list : _languages;

        if (opts.TryGetValue("separator", out var sep) && sep is string s && s.Length > 0)
            _separator = s;

        // `pipeline` overrides the lunr token pipeline; an empty list disables stemming/stopwords.
        if (opts.TryGetValue("pipeline", out var pipe) && AsStringList(pipe) is { } p)
            _pipeline = p;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        var parser = new HtmlParser();
        var docs = new List<SearchDoc>();

        foreach (var page in site.Pages)
        {
            var tags = ExtractTags(page);
            var (intro, sections) = SplitPage(parser, page);
            docs.Add(new SearchDoc(page.Url, page.Title, intro, tags));

            foreach (var section in sections)
                docs.Add(section with { Tags = tags });
        }

        var fields = new Dictionary<string, object?>
        {
            ["title"] = new Dictionary<string, object?> { ["boost"] = 1000.0 },
            ["text"] = new Dictionary<string, object?> { ["boost"] = 1.0 },
            ["tags"] = new Dictionary<string, object?> { ["boost"] = 1000000.0 },
        };
        var index = new SearchIndex(
            new SearchConfig(_languages, _separator, _pipeline, fields),
            docs);

        var dir = Path.Combine(site.Config.AbsoluteSiteDir, "search");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(index, SearchJson.Options);
        await File.WriteAllTextAsync(Path.Combine(dir, "search_index.json"), json, ct);
    }

    /// <summary>Splits a page into its intro (text before the first heading) and per-section docs.
    /// The page-level doc uses only the intro so search teasers stay concise (matches Material);
    /// the body text lives in the per-section docs.</summary>
    private static (string Intro, IReadOnlyList<SearchDoc> Sections) SplitPage(HtmlParser parser, Page page)
    {
        if (string.IsNullOrEmpty(page.HtmlContent))
            return (Collapse(page.PlainText), []);

        var doc = parser.ParseDocument($"<body>{page.HtmlContent}</body>");
        var body = doc.Body;
        if (body is null) return (Collapse(page.PlainText), []);

        var sections = new List<SearchDoc>();
        var intro = new StringBuilder();
        string? currentId = null, currentTitle = null;
        var buffer = new StringBuilder();
        var seenHeading = false;

        foreach (var node in body.ChildNodes)
        {
            if (node is IElement el && el.TagName is "H1" or "H2" or "H3" && !string.IsNullOrEmpty(el.Id))
            {
                if (currentId is not null)
                    sections.Add(new SearchDoc($"{page.Url}#{currentId}", currentTitle ?? "", Collapse(buffer.ToString()), []));
                currentId = el.Id;
                currentTitle = el.TextContent.Trim();
                buffer.Clear();
                seenHeading = true;
            }
            else if (seenHeading)
            {
                buffer.Append(node.TextContent).Append(' ');
            }
            else
            {
                intro.Append(node.TextContent).Append(' ');
            }
        }
        if (currentId is not null)
            sections.Add(new SearchDoc($"{page.Url}#{currentId}", currentTitle ?? "", Collapse(buffer.ToString()), []));

        var introText = Collapse(intro.ToString());
        if (introText.Length == 0 && sections.Count == 0) introText = Collapse(page.PlainText);
        return (introText, sections);
    }

    private static string[] ExtractTags(Page page)
    {
        if (page.FrontMatter.TryGetValue("tags", out var t) && t is IEnumerable<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToArray();
        return [];
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Normalizes a plugin option that may be a single string or a list into a string list.
    /// Returns null when the value is neither (so callers can keep their default).</summary>
    private static IReadOnlyList<string>? AsStringList(object? value) => value switch
    {
        string s => [s],
        IEnumerable<object?> items => items.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
        _ => null,
    };
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
