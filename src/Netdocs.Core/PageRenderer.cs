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
        };

        return engine.Render(template, model);
    }

    private static string Sanitize(string value) => value.Replace(' ', '-').ToLowerInvariant();

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
