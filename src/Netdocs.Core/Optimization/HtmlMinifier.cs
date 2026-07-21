using System.Text.RegularExpressions;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Conservative HTML minifier: collapses insignificant whitespace and strips
/// non-conditional comments while preserving the exact contents of
/// <c>pre</c>, <c>code</c>, <c>textarea</c>, <c>script</c>, and <c>style</c> blocks.
/// </summary>
public static partial class HtmlMinifier
{
    public static string Minify(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        // 1. Pull out regions whose whitespace is significant.
        var protectedBlocks = new List<string>();
        var withPlaceholders = ProtectedRegions().Replace(html, m =>
        {
            protectedBlocks.Add(m.Value);
            return $"\u0000{protectedBlocks.Count - 1}\u0000";
        });

        // 2. Remove HTML comments except IE conditionals.
        withPlaceholders = Comments().Replace(withPlaceholders, "");

        // 3. Collapse any run of whitespace to a single space, then tidy tag boundaries.
        withPlaceholders = Whitespace().Replace(withPlaceholders, " ");
        withPlaceholders = BetweenTags().Replace(withPlaceholders, "><");
        withPlaceholders = withPlaceholders.Trim();

        // 4. Restore protected regions.
        return Placeholder().Replace(withPlaceholders, m =>
            protectedBlocks[int.Parse(m.Groups[1].Value)]);
    }

    [GeneratedRegex(@"<(pre|code|textarea|script|style)\b[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedRegions();

    [GeneratedRegex(@"<!--(?!\[if)(?!<!)[\s\S]*?-->")]
    private static partial Regex Comments();

    [GeneratedRegex(@"\s{2,}|[\r\n\t]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@">\s+<")]
    private static partial Regex BetweenTags();

    [GeneratedRegex("\u0000(\\d+)\u0000")]
    private static partial Regex Placeholder();
}
