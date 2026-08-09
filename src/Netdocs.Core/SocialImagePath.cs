using Netdocs.Abstractions;

namespace Netdocs.Core;

/// <summary>
/// Where a build writes generated social cards. The social plugin publishes its resolved settings
/// into <see cref="SiteContext.State"/> under <see cref="SocialImagePath.StateKey"/> during
/// <c>OnBuildStart</c>, which is what lets the page renderer point <c>og:image</c> at the very
/// file the plugin is about to produce — including a custom directory or image format.
/// </summary>
public sealed record SocialCardSettings(string Directory, string Extension)
{
    public static SocialCardSettings Default { get; } = new("assets/social", ".png");
}

/// <summary>Deterministic path for a page's generated social (OG) card, shared by renderer and plugin.</summary>
public static class SocialImagePath
{
    /// <summary><see cref="SiteContext.State"/> key holding the active <see cref="SocialCardSettings"/>.
    /// Absent when no plugin is generating cards, so no <c>og:image</c> should be advertised.</summary>
    public const string StateKey = "social_cards";

    public static string For(Page page) => For(page, SocialCardSettings.Default);

    public static string For(Page page, SocialCardSettings settings)
    {
        var slug = page.Url.Trim('/');
        slug = slug.Length == 0 ? "index" : slug.Replace('/', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
            slug = slug.Replace(c, '_');

        var dir = settings.Directory.Replace('\\', '/').Trim('/');
        return dir.Length == 0 ? slug + settings.Extension : $"{dir}/{slug}{settings.Extension}";
    }

    /// <summary>The card path for a page, or null when nothing is generating cards this build.</summary>
    public static string? Resolve(SiteContext site, Page page) =>
        site.State.GetValueOrDefault(StateKey) is SocialCardSettings settings ? For(page, settings) : null;
}
