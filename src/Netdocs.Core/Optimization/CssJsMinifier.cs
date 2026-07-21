using System.Text.RegularExpressions;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Conservative CSS/JS minifier: strips comments and collapses insignificant whitespace
/// without attempting to rename identifiers or reorder rules. It preserves the contents of
/// string and template literals so URLs, selectors, and text are never corrupted. This is a
/// safe, dependency-free size reduction — not an aggressive compressor.
/// </summary>
public static partial class CssJsMinifier
{
    /// <summary>Minifies CSS: drops <c>/* … */</c> comments and squeezes whitespace.</summary>
    public static string MinifyCss(string css)
    {
        if (string.IsNullOrEmpty(css)) return css;

        var (withPlaceholders, strings) = ProtectStrings(css);
        withPlaceholders = CssComments().Replace(withPlaceholders, "");
        withPlaceholders = Whitespace().Replace(withPlaceholders, " ");
        // Tidy spaces around structural punctuation.
        withPlaceholders = CssAroundPunctuation().Replace(withPlaceholders, "$1");
        withPlaceholders = withPlaceholders.Replace(";}", "}").Trim();
        return RestoreStrings(withPlaceholders, strings);
    }

    /// <summary>
    /// Minifies JavaScript conservatively: drops block and line comments and collapses runs of
    /// whitespace to a single space. Line breaks are preserved as spaces so automatic
    /// semicolon insertion is unaffected; string/template literals are protected verbatim.
    /// </summary>
    public static string MinifyJs(string js)
    {
        if (string.IsNullOrEmpty(js)) return js;

        var (withPlaceholders, strings) = ProtectStrings(js);
        withPlaceholders = JsBlockComments().Replace(withPlaceholders, "");
        withPlaceholders = JsLineComments().Replace(withPlaceholders, "");
        withPlaceholders = Whitespace().Replace(withPlaceholders, " ");
        withPlaceholders = withPlaceholders.Trim();
        return RestoreStrings(withPlaceholders, strings);
    }

    /// <summary>Replaces quoted strings and template/regex-safe literals with placeholders.</summary>
    private static (string Text, List<string> Strings) ProtectStrings(string source)
    {
        var strings = new List<string>();
        var text = StringLiterals().Replace(source, m =>
        {
            strings.Add(m.Value);
            return $"\u0000{strings.Count - 1}\u0000";
        });
        return (text, strings);
    }

    private static string RestoreStrings(string text, List<string> strings) =>
        Placeholder().Replace(text, m => strings[int.Parse(m.Groups[1].Value)]);

    [GeneratedRegex(@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`")]
    private static partial Regex StringLiterals();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex CssComments();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex JsBlockComments();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex JsLineComments();

    [GeneratedRegex(@"\s{2,}|[\r\n\t]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*([{}:;,>])\s*")]
    private static partial Regex CssAroundPunctuation();

    [GeneratedRegex("\u0000(\\d+)\u0000")]
    private static partial Regex Placeholder();
}
