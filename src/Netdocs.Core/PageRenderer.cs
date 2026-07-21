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
