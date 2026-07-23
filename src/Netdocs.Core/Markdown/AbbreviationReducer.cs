using System.Text.RegularExpressions;

namespace Netdocs.Core.Markdown;

/// <summary>
/// Reduces rendered abbreviation tooltips to the first occurrence of each term on a page.
/// The Markdig abbreviations extension wraps <em>every</em> occurrence of a defined term in an
/// <c>&lt;abbr&gt;</c> element; when a term repeats many times this covers the page in
/// dotted-underline placeholders. This post-processor keeps the first <c>&lt;abbr&gt;</c> for each
/// distinct term and unwraps the rest back to plain text.
/// </summary>
public static partial class AbbreviationReducer
{
    // <abbr> elements never nest and wrap only the plain term text, so a non-greedy single-line
    // match is safe and avoids re-serializing (and thus perturbing) the surrounding HTML.
    [GeneratedRegex(@"<abbr\b[^>]*>(?<text>.*?)</abbr>", RegexOptions.Singleline)]
    private static partial Regex AbbrRegex();

    /// <summary>Returns <paramref name="html"/> with every abbreviation term marked up only on its
    /// first occurrence; subsequent occurrences are replaced by their inner text.</summary>
    public static string KeepFirstInstances(string html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("<abbr", StringComparison.OrdinalIgnoreCase) < 0)
            return html;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return AbbrRegex().Replace(html, match =>
        {
            var text = match.Groups["text"].Value;
            return seen.Add(text) ? match.Value : text;
        });
    }
}
