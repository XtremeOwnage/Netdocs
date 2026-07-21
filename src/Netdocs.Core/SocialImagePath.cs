using Netdocs.Abstractions;

namespace Netdocs.Core;

/// <summary>Deterministic path for a page's generated social (OG) card, shared by renderer and plugin.</summary>
public static class SocialImagePath
{
    public static string For(Page page)
    {
        var slug = page.Url.Trim('/');
        slug = slug.Length == 0 ? "index" : slug.Replace('/', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
            slug = slug.Replace(c, '_');
        return $"assets/social/{slug}.png";
    }
}
