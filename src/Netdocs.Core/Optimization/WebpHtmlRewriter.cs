using System.Text.RegularExpressions;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Wraps <c>&lt;img&gt;</c> tags that reference a local raster image (png/jpg) in a
/// <c>&lt;picture&gt;</c> element offering the generated <c>.webp</c> as the preferred source,
/// keeping the original <c>&lt;img&gt;</c> as a universal fallback. External (http/data) and
/// already-webp sources are left untouched, so the transform is safe and non-destructive.
///
/// <para>A <c>&lt;source&gt;</c> is only emitted when the build actually produced that
/// <c>.webp</c> (see <see cref="WebpManifest"/>). Because <c>src</c> is written relative to the
/// page, resolving it against the page's own URL is what makes that lookup possible.</para>
/// </summary>
public static partial class WebpHtmlRewriter
{
    private static readonly string[] ConvertibleExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>
    /// Rewrites <paramref name="html"/> for a page served at <paramref name="pageUrl"/> (the
    /// site-relative URL, e.g. <c>guide/setup/</c>), offering only the webp files listed in
    /// <paramref name="manifest"/>.
    /// </summary>
    public static string Rewrite(string html, string pageUrl, WebpManifest manifest)
    {
        if (string.IsNullOrEmpty(html)
            || manifest.Count == 0
            || !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            return html;

        // Author-written <picture> blocks already declare their own sources; wrapping an <img>
        // inside one would nest <picture> elements and leave the outer <source> applying to the
        // wrong image. Note their spans and leave those tags alone.
        var authored = ExistingPicture().Matches(html)
            .Select(m => (Start: m.Index, End: m.Index + m.Length))
            .ToList();

        return ImgTag().Replace(html, m =>
            authored.Any(r => m.Index >= r.Start && m.Index < r.End)
                ? m.Value
                : RewriteTag(m.Value, pageUrl, manifest));
    }

    private static string RewriteTag(string tag, string pageUrl, WebpManifest manifest)
    {
        var srcMatch = SrcAttr().Match(tag);
        if (!srcMatch.Success) return tag;

        var src = srcMatch.Groups[1].Value.Trim();
        if (src.Length == 0
            || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("//", StringComparison.Ordinal)
            || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return tag;

        // A cache-busting query or fragment is not part of the file name, but it does belong on
        // the emitted srcset — split it off for the lookup and re-attach it afterwards.
        var cut = src.IndexOfAny(['?', '#']);
        var path = cut >= 0 ? src[..cut] : src;
        var suffix = cut >= 0 ? src[cut..] : "";

        var ext = Path.GetExtension(path);
        if (!ConvertibleExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return tag;

        var webp = path[..^ext.Length] + ".webp";
        var resolved = ResolveSiteRelative(pageUrl, webp);
        if (resolved is null || !manifest.Contains(resolved)) return tag;

        return $"<picture><source srcset=\"{webp}{suffix}\" type=\"image/webp\">{tag}</picture>";
    }

    /// <summary>
    /// Resolves an <c>src</c> as written on a page into a path relative to the site root, or null
    /// when it escapes the root. Percent-escapes are decoded because the manifest holds real file
    /// names, while markdown renders <c>my image.png</c> as <c>my%20image.png</c>.
    /// </summary>
    internal static string? ResolveSiteRelative(string pageUrl, string src)
    {
        string decoded;
        try { decoded = Uri.UnescapeDataString(src); }
        catch (UriFormatException) { decoded = src; }

        var combined = decoded.StartsWith('/') ? decoded : PageDirectory(pageUrl) + decoded;

        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>The directory a page's relative links resolve against: <c>guide/setup/</c> for a
    /// directory-style URL, the parent for a file-style one (<c>404.html</c> → site root).</summary>
    private static string PageDirectory(string pageUrl)
    {
        var url = (pageUrl ?? "").Replace('\\', '/').TrimStart('/');
        if (url.Length == 0) return "";
        if (url.EndsWith('/')) return url;
        var slash = url.LastIndexOf('/');
        return slash < 0 ? "" : url[..(slash + 1)];
    }

    // Matches a full <img ...> tag (self-closing or not). Img is a void element, so it has no
    // closing tag; the greedy-to-'>' body is fine because attributes cannot contain a bare '>'.
    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    // The lookbehind keeps `data-src`/`lazy-src` from being read as the real `src` attribute
    // (a plain \b would still match there, since '-' is a non-word character).
    [GeneratedRegex("""(?<![-\w])src\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SrcAttr();

    [GeneratedRegex(@"<picture\b[^>]*>.*?</picture\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ExistingPicture();
}
