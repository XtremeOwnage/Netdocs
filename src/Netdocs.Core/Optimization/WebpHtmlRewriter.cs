using System.Text.RegularExpressions;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Wraps <c>&lt;img&gt;</c> tags that reference a local raster image (png/jpg) in a
/// <c>&lt;picture&gt;</c> element offering the generated <c>.webp</c> as the preferred source,
/// keeping the original <c>&lt;img&gt;</c> as a universal fallback. External (http/data) and
/// already-webp sources are left untouched, so the transform is safe and non-destructive.
/// </summary>
public static partial class WebpHtmlRewriter
{
    public static string Rewrite(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            return html;

        return ImgTag().Replace(html, static m =>
        {
            var tag = m.Value;
            var srcMatch = SrcAttr().Match(tag);
            if (!srcMatch.Success) return tag;

            var src = srcMatch.Groups[1].Value.Trim();
            if (src.Length == 0
                || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("//", StringComparison.Ordinal)
                || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return tag;

            var ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg")) return tag;

            var webp = src[..^ext.Length] + ".webp";
            return $"<picture><source srcset=\"{webp}\" type=\"image/webp\">{tag}</picture>";
        });
    }

    // Matches a full <img ...> tag (self-closing or not). Img is a void element, so it has no
    // closing tag; the greedy-to-'>' body is fine because attributes cannot contain a bare '>'.
    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex("""src\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SrcAttr();
}
