using Netdocs.Core.Markdown.Emoji;

namespace Netdocs.Core.Cms;

/// <summary>An icon the picker can offer: its front matter name, shortcode and SVG.</summary>
public sealed record IconMatch(string Name, string Shortcode, string Svg);

/// <summary>Searches the bundled icon set for the CMS icon picker.</summary>
public static class IconCatalog
{
    private static readonly Lazy<string[]> Sorted = new(() => [.. IconRegistry.Names.Order(StringComparer.Ordinal)]);

    /// <summary>
    /// Icons whose name contains every word of <paramref name="query"/>. Exact and prefix matches
    /// come first; Material icons come before the other sets.
    /// </summary>
    public static IReadOnlyList<IconMatch> Search(string query, int limit)
    {
        var words = query.ToLowerInvariant().Replace('/', '-').Replace(':', ' ')
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var hits = Sorted.Value.Where(n => words.All(w => n.Contains(w, StringComparison.Ordinal)));
        if (words.Length == 0) hits = hits.Where(n => n.StartsWith("material-", StringComparison.Ordinal));
        var q = string.Join('-', words);
        return [.. hits
            .OrderBy(n => Rank(n, q))
            .ThenBy(n => n.Length)
            .Take(limit)
            .Select(n => new IconMatch(ToName(n), n, IconRegistry.RenderSvg(n) ?? ""))];
    }

    private static int Rank(string shortcode, string q)
    {
        var bare = Bare(shortcode);
        var set = shortcode.StartsWith("material-", StringComparison.Ordinal) ? 0 : 1;
        if (q.Length == 0) return set;
        if (bare == q) return 0 + set;
        if (bare.StartsWith(q, StringComparison.Ordinal)) return 2 + set;
        return 4 + set;
    }

    private static string Bare(string shortcode)
    {
        var name = ToName(shortcode);
        return name[(name.LastIndexOf('/') + 1)..];
    }

    /// <summary><c>material-home</c> → <c>material/home</c>; <c>fontawesome-brands-github</c> → <c>fontawesome/brands/github</c>.</summary>
    public static string ToName(string shortcode)
    {
        var dash = shortcode.IndexOf('-');
        if (dash < 0) return shortcode;
        var set = shortcode[..dash];
        var rest = shortcode[(dash + 1)..];
        if (set == "fontawesome")
        {
            var second = rest.IndexOf('-');
            if (second > 0) rest = rest[..second] + "/" + rest[(second + 1)..];
        }
        return set + "/" + rest;
    }
}
