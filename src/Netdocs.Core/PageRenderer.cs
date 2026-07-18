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

        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["config"] = site.Config,
            ["page"] = page,
            ["nav"] = site.Navigation,
            ["pages"] = site.Pages,
            ["base_url"] = BaseUrl(page.Url),
            ["features"] = new HashSet<string>(site.Config.Theme.Features, StringComparer.OrdinalIgnoreCase),
            ["extra"] = site.Config.Extra,
            ["stylesheets"] = ResolveHrefs(site.Config.ExtraCss, assets.Stylesheets),
            ["scripts"] = assets.Scripts.Select(s => s.Src).Concat(site.Config.ExtraJavaScript).ToList(),
            ["content"] = page.HtmlContent,
            ["toc"] = page.Toc,
        };

        return engine.Render(template, model);
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
