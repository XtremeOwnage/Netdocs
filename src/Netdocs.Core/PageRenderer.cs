using Netdocs.Abstractions;
using Netdocs.Core.Plugins;
using Netdocs.Core.Templating;

namespace Netdocs.Core;

/// <summary>Builds the per-page template model and renders the final HTML document.</summary>
public static class PageRenderer
{
    public static string Render(TemplateEngine engine, SiteContext site, Page page, PluginAssets assets)
    {
        var template = page.Meta.TryGetValue("template", out var t) && t is string tpl && tpl.Length > 0
            ? tpl
            : "main.html";

        var palette = site.Config.Theme.Palette.Count > 0 ? site.Config.Theme.Palette[0] : null;
        var font = site.Config.Theme.Font;
        var siteUrl = (site.Config.SiteUrl ?? "").TrimEnd('/');
        var socialPath = SocialImagePath.For(page);
        var description = page.FrontMatter.TryGetValue("description", out var d) && d is string ds && ds.Length > 0
            ? ds : site.Config.SiteDescription ?? "";

        Page? prev = null, next = null;
        if (site.State.GetValueOrDefault("nav_pages") is List<Page> navPages)
        {
            var i = navPages.IndexOf(page);
            if (i >= 0)
            {
                if (i > 0) prev = navPages[i - 1];
                if (i < navPages.Count - 1) next = navPages[i + 1];
            }
        }

        // Blog posts are generated outside the authored nav; sequence prev/next by date
        // (newest-first list): "previous" is the older post, "next" is the newer one.
        if (prev is null && next is null)
            (prev, next) = BlogSiblings(site, page);

        var breadcrumbs = Breadcrumbs(site.Navigation, page);

        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["config"] = site.Config,
            ["page"] = page,
            ["nav"] = site.Navigation,
            ["pages"] = site.Pages,
            ["base_url"] = BaseUrl(page.Url),
            ["is_homepage"] = string.IsNullOrEmpty(page.Url),
            ["features"] = new HashSet<string>(site.Config.Theme.Features, StringComparer.OrdinalIgnoreCase),
            ["extra"] = site.Config.Extra,
            ["stylesheets"] = ResolveHrefs(site.Config.ExtraCss, assets.Stylesheets),
            ["scripts"] = assets.Scripts.Select(s => s.Src).Concat(site.Config.ExtraJavaScript).ToList(),
            ["inline_scripts"] = assets.InlineScripts,
            ["content"] = page.HtmlContent,
            ["toc"] = page.Toc,
            ["palette_scheme"] = Sanitize(palette?.Scheme ?? "default"),
            ["palette_primary"] = Sanitize(palette?.Primary ?? "indigo"),
            ["palette_accent"] = Sanitize(palette?.Accent ?? "indigo"),
            ["palettes"] = BuildPalettes(site.Config.Theme.Palette),
            ["font_text"] = font.TryGetValue("text", out var ft) ? ft?.ToString() ?? "Roboto" : "Roboto",
            ["font_code"] = font.TryGetValue("code", out var fc) ? fc?.ToString() ?? "Roboto Mono" : "Roboto Mono",
            ["updated_display"] = page.Updated?.ToString("MMMM d, yyyy"),
            ["created_display"] = page.Created?.ToString("MMMM d, yyyy"),
            ["show_source_meta"] = !page.IsGenerated && page.Updated.HasValue,
            ["prev_page"] = prev,
            ["next_page"] = next,
            ["breadcrumbs"] = breadcrumbs,
            ["page_description"] = description,
            ["og_image"] = siteUrl.Length > 0 ? $"{siteUrl}/{socialPath}" : "/" + socialPath,
            ["og_url"] = siteUrl.Length > 0 ? $"{siteUrl}/{page.Url}" : "/" + page.Url,
        };

        return engine.Render(template, model);
    }

    private static string Sanitize(string value) => value.Replace(' ', '-').ToLowerInvariant();

    /// <summary>Projects the configured palettes into a template-friendly list. Only palettes that
    /// declare a <c>toggle</c> render a switcher button (mirroring Material's palette component,
    /// which the vendored bundle.js drives via the <c>__palette</c> radio inputs).</summary>
    private static List<Dictionary<string, object?>> BuildPalettes(IReadOnlyList<PaletteConfig> palettes)
    {
        var result = new List<Dictionary<string, object?>>();
        for (var i = 0; i < palettes.Count; i++)
        {
            var p = palettes[i];
            var hasToggle = !string.IsNullOrEmpty(p.ToggleIcon);
            result.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["index"] = i + 1,
                ["media"] = p.Media ?? "",
                ["scheme"] = Sanitize(p.Scheme ?? "default"),
                ["primary"] = Sanitize(p.Primary ?? "indigo"),
                ["accent"] = Sanitize(p.Accent ?? "indigo"),
                ["has_toggle"] = hasToggle,
                ["toggle_name"] = p.ToggleName ?? "Switch color scheme",
                ["toggle_svg"] = hasToggle ? BrightnessIcon(p.ToggleIcon!) : "",
            });
        }
        return result;
    }

    private static string BrightnessIcon(string name)
    {
        var d = name.ToLowerInvariant() switch
        {
            var n when n.Contains("brightness-7") || n.Contains("weather-sunny") =>
                "M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5M2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1m18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1M11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1m0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1M5.99 4.58c-.39-.39-1.03-.39-1.41 0-.39.39-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41L5.99 4.58m12.37 12.37c-.39-.39-1.03-.39-1.41 0-.39.39-.39 1.03 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0 .39-.39.39-1.03 0-1.41l-1.06-1.06m1.06-10.96c.39-.39.39-1.03 0-1.41-.39-.39-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06M7.05 18.36c.39-.39.39-1.03 0-1.41-.39-.39-1.03-.39-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06Z",
            var n when n.Contains("brightness-auto") =>
                "m14.3 16-1.1-3h-2.4l-1.1 3H7.8L11 7h2l3.2 9h-1.9M20 8.69V4h-4.69L12 .69 8.69 4H4v4.69L.69 12 4 15.31V20h4.69L12 23.31 15.31 20H20v-4.69L23.31 12 20 8.69M11.5 10.5h1l.9 2.5h-2.8l.9-2.5Z",
            _ =>
                "M12 3a9 9 0 0 0-9 9 9 9 0 0 0 9 9 9 9 0 0 0 9-9 9 9 0 0 0-9-9m0 2c.83 0 1.5.67 1.5 1.5S12.83 8 12 8s-1.5-.67-1.5-1.5S11.17 5 12 5M10 9.5c.83 0 1.5.67 1.5 1.5s-.67 1.5-1.5 1.5S8.5 11.83 8.5 11s.67-1.5 1.5-1.5m5.5 2c.83 0 1.5.67 1.5 1.5s-.67 1.5-1.5 1.5-1.5-.67-1.5-1.5.67-1.5 1.5-1.5M11 15c.83 0 1.5.67 1.5 1.5S11.83 18 11 18s-1.5-.67-1.5-1.5S10.17 15 11 15Z",
        };
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"{d}\"/></svg>";
    }

    /// <summary>Newest-first blog sequence: previous = older post, next = newer post.</summary>
    private static (Page? prev, Page? next) BlogSiblings(SiteContext site, Page page)
    {
        if (site.State.GetValueOrDefault("blog_posts") is not System.Collections.IList posts) return (null, null);

        for (var i = 0; i < posts.Count; i++)
        {
            if (PostPage(posts[i]) != page) continue;
            var newer = i > 0 ? PostPage(posts[i - 1]) : null;
            var older = i < posts.Count - 1 ? PostPage(posts[i + 1]) : null;
            return (older, newer);
        }
        return (null, null);
    }

    private static Page? PostPage(object? post) =>
        post?.GetType().GetProperty("Page")?.GetValue(post) as Page;

    /// <summary>Ancestor trail (sections + section-index pages) from the nav root to the current page.</summary>
    private static List<Dictionary<string, object?>> Breadcrumbs(IReadOnlyList<NavNode> nav, Page page)
    {
        var trail = new List<Dictionary<string, object?>>();

        bool Walk(IReadOnlyList<NavNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Page == page) return true;

                if (node.IsSection)
                {
                    var crumb = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = node.Title,
                        ["url"] = node.SectionIndex?.Url,
                    };
                    trail.Add(crumb);
                    if (node.SectionIndex == page) return true;
                    if (Walk(node.Children)) return true;
                    trail.RemoveAt(trail.Count - 1);
                }
            }
            return false;
        }

        Walk(nav);
        return trail;
    }

    private static List<string> ResolveHrefs(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Concat(b).ToList();

    /// <summary>Relative prefix from a page URL back to the site root (e.g. "blog/post/" -> "../../").</summary>
    public static string BaseUrl(string url)
    {
        var trimmed = url.Trim('/');
        if (trimmed.Length == 0) return "";
        var segments = trimmed.Split('/');
        var depth = segments.Length;
        if (segments[^1].Contains('.')) depth -= 1;
        return depth == 0 ? "" : string.Concat(Enumerable.Repeat("../", depth));
    }
}
