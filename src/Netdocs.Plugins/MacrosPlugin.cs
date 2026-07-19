using System.Text.RegularExpressions;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Minimal mkdocs-macros port. Expands two example macros to show the pattern users can
/// extend: <c>{{ fileuri("name") }}</c> resolves a doc or asset to its published URL
/// (prefixed with <c>site_url</c>), and <c>{{ button("text", "url") }}</c> renders a
/// Material-styled call-to-action button. Honors mkdocs-macros' <c>render_by_default</c>
/// option and the per-page <c>render_macros</c> / <c>ignore_macros</c> front-matter overrides.
/// </summary>
public sealed partial class MacrosPlugin : IPlugin, IMarkdownPreprocessor
{
    private string _projectRoot = "";
    private string _docsDir = "";
    private string _siteUrl = "";
    private bool _renderByDefault = true;

    public string Name => "macros";
    public int Order => 25; // after snippets (10) / table-reader (20) so their output can use macros

    public void Configure(IPluginContext ctx)
    {
        _projectRoot = ctx.Config.ProjectRoot;
        _docsDir = ctx.Config.AbsoluteDocsDir;
        _siteUrl = (ctx.Config.SiteUrl ?? "").TrimEnd('/');

        if (ctx.PluginOptions.TryGetValue("render_by_default", out var rbd) && rbd is bool b)
            _renderByDefault = b;
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (!ShouldRender(page)) return Task.FromResult(markdown);
        if (!markdown.Contains("{{", StringComparison.Ordinal)) return Task.FromResult(markdown);

        var result = FileUriRegex().Replace(markdown, m =>
        {
            var url = ResolveFileUri(m.Groups["file"].Value, page, site);
            return url ?? $"<!-- macros: fileuri('{m.Groups["file"].Value}') not found -->";
        });

        result = ButtonRegex().Replace(result, m =>
            RenderButton(m.Groups["text"].Value, m.Groups["url"].Value));

        return Task.FromResult(result);
    }

    /// <summary>
    /// mkdocs-macros gating: when <c>render_by_default</c> is true, every page renders unless
    /// it opts out with <c>render_macros: false</c> or <c>ignore_macros: true</c>. When false,
    /// only pages with <c>render_macros: true</c> render.
    /// </summary>
    private bool ShouldRender(Page page)
    {
        if (TryBool(page, "ignore_macros") == true) return false;
        var explicitFlag = TryBool(page, "render_macros");
        return explicitFlag ?? _renderByDefault;
    }

    private static bool? TryBool(Page page, string key) =>
        page.FrontMatter.TryGetValue(key, out var v) && v is bool b ? b : null;

    private string? ResolveFileUri(string filename, Page page, SiteContext site)
    {
        var currentDir = DirName(page.RelativePath);

        // Prefer a page in the same directory, then any page with a matching file name.
        var match = site.Pages.FirstOrDefault(p =>
                        BaseName(p.RelativePath) == filename && DirName(p.RelativePath) == currentDir)
                    ?? site.Pages.FirstOrDefault(p => BaseName(p.RelativePath) == filename);
        if (match is not null) return Join(match.Url);

        // Fall back to a static asset that ships to the output verbatim.
        var assetRel = ResolveAsset(filename, currentDir);
        return assetRel is null ? null : Join(assetRel);
    }

    private string? ResolveAsset(string filename, string currentDir)
    {
        var sameDir = Path.Combine(_docsDir, currentDir.Replace('/', Path.DirectorySeparatorChar), filename);
        if (File.Exists(sameDir))
            return CombineUrl(currentDir, filename);

        if (!Directory.Exists(_docsDir)) return null;
        foreach (var path in Directory.EnumerateFiles(_docsDir, filename, SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_docsDir, path).Replace('\\', '/');
            return rel;
        }
        return null;
    }

    private string Join(string url) =>
        _siteUrl.Length > 0 ? $"{_siteUrl}/{url.TrimStart('/')}" : $"/{url.TrimStart('/')}";

    private static string CombineUrl(string dir, string file) =>
        dir.Length == 0 ? file : $"{dir}/{file}";

    private static string BaseName(string relative) =>
        Path.GetFileName(relative.Replace('\\', '/'));

    private static string DirName(string relative)
    {
        var d = Path.GetDirectoryName(relative.Replace('\\', '/')) ?? "";
        return d.Replace('\\', '/');
    }

    private static string RenderButton(string text, string url) =>
        $"""<a class="md-button" href="{Attr(url)}">{Attr(text)}</a>""";

    private static string Attr(string text) => text
        .Replace("&", "&amp;")
        .Replace("\"", "&quot;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("'", "&apos;");

    [GeneratedRegex("""\{\{\s*fileuri\s*\(\s*["'](?<file>[^"']+)["']\s*\)\s*\}\}""")]
    private static partial Regex FileUriRegex();

    [GeneratedRegex("""\{\{\s*button\s*\(\s*["'](?<text>[^"']*)["']\s*,\s*["'](?<url>[^"']+)["']\s*\)\s*\}\}""")]
    private static partial Regex ButtonRegex();
}
