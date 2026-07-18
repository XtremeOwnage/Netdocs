using System.Globalization;
using System.Text;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>Slugifies text for URLs. Default: lowercase, hyphen-separated, accents folded.</summary>
public static class Slug
{
    /// <summary>Slugify with a custom separator (default behavior otherwise).</summary>
    public static string Make(string text, string separator = "-") =>
        Make(text, new SlugifyConfig { Separator = separator });

    /// <summary>Slugify honoring a <see cref="SlugifyConfig"/> (case, separator, ASCII folding).</summary>
    public static string Make(string text, SlugifyConfig config)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(c))
            {
                if (config.Ascii && c > 127) continue; // drop non-ASCII letters/digits when ASCII-only
                sb.Append(ApplyCase(c, config.Case));
            }
            else if (char.IsWhiteSpace(c) || c is '-' or '_' or '/')
            {
                sb.Append(' ');
            }
        }

        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(config.Separator, words);
    }

    private static char ApplyCase(char c, string @case) => @case?.ToLowerInvariant() switch
    {
        "upper" => char.ToUpperInvariant(c),
        "none" => c,
        _ => char.ToLowerInvariant(c),
    };
}
