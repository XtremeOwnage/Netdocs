using System.Globalization;
using System.Text;

namespace Netdocs.Plugins;

/// <summary>Slugifies text for URLs (lowercase, hyphen-separated, ASCII).</summary>
public static class Slug
{
    public static string Make(string text, string separator = "-")
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (char.IsWhiteSpace(c) || c is '-' or '_' or '/') sb.Append(' ');
        }

        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(separator, words);
    }
}
