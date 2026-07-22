using System.Text.RegularExpressions;
using Netdocs.Abstractions;
using Netdocs.Core;

namespace Netdocs.Plugins;

/// <summary>
/// Minimal mkdocs-macros port. Ships three example function macros to show the pattern:
/// <c>{{ fileuri("name") }}</c> resolves a doc or asset to its published URL (prefixed with
/// <c>site_url</c>), <c>{{ button("text", "url") }}</c> renders a Material-styled
/// call-to-action button, and <c>{{ download("file"[, "text"][, "mode"]) }}</c> renders a
/// download link (HTML5 <c>download</c> attribute) to a doc/asset — handy paired with a code
/// block that shows the same script. Sites can define their own simple text macros without
/// writing C# via the <c>variables</c> plugin option (each <c>key</c> becomes a <c>{{ key }}</c>
/// token); richer macros are added by writing a custom <see cref="IMarkdownPreprocessor"/> plugin.
/// Honors mkdocs-macros' <c>render_by_default</c> option and the per-page <c>render_macros</c>
/// / <c>ignore_macros</c> front-matter overrides.
/// </summary>
public sealed partial class MacrosPlugin : IPlugin, IMarkdownPreprocessor
{
    private string _projectRoot = "";
    private string _docsDir = "";
    private string _siteUrl = "";
    private bool _renderByDefault = true;
    private IReadOnlyDictionary<string, string> _variables = new Dictionary<string, string>();

    public string Name => "macros";
    public int Order => 25; // after snippets (10) / table-reader (20) so their output can use macros

    public void Configure(IPluginContext ctx)
    {
        _projectRoot = ctx.Config.ProjectRoot;
        _docsDir = ctx.Config.AbsoluteDocsDir;
        _siteUrl = (ctx.Config.SiteUrl ?? "").TrimEnd('/');

        if (ctx.PluginOptions.TryGetValue("render_by_default", out var rbd) && rbd is bool b)
            _renderByDefault = b;

        // User-defined variables: `variables: { key: value }` in the plugin config become
        // `{{ key }}` text macros, so a site can define its own macros without writing C#.
        if (ctx.PluginOptions.TryGetValue("variables", out var vars) && vars is IReadOnlyDictionary<string, object?> map)
        {
            _variables = map
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString() ?? "", StringComparer.Ordinal);
        }
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (!ShouldRender(page)) return Task.FromResult(markdown);
        if (!markdown.Contains("{{", StringComparison.Ordinal)) return Task.FromResult(markdown);

        var result = FileUriRegex().Replace(markdown, m =>
        {
            var url = ResolveFileUri(m.Groups["file"].Value, m.Groups["mode"].Value, page, site);
            return url ?? $"<!-- macros: fileuri('{m.Groups["file"].Value}') not found -->";
        });

        result = ButtonRegex().Replace(result, m =>
            RenderButton(m.Groups["text"].Value, m.Groups["url"].Value));

        result = DownloadRegex().Replace(result, m =>
        {
            var file = m.Groups["file"].Value;
            var url = ResolveFileUri(file, m.Groups["mode"].Value, page, site);
            if (url is null) return $"<!-- macros: download('{file}') not found -->";
            var text = m.Groups["text"].Success && m.Groups["text"].Value.Length > 0
                ? m.Groups["text"].Value
                : $"Download {BaseName(file)}";
            return RenderDownload(url, text, BaseName(file));
        });

        // Bare `{{ name }}` tokens expand to a user-defined variable when one exists;
        // unknown tokens are left untouched so they can be handled elsewhere (or shown literally).
        if (_variables.Count > 0)
        {
            result = VariableRegex().Replace(result, m =>
                _variables.TryGetValue(m.Groups["name"].Value, out var val) ? val : m.Value);
        }

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

    private string? ResolveFileUri(string filename, string mode, Page page, SiteContext site)
    {
        var currentDir = DirName(page.RelativePath);

        // Prefer a page in the same directory, then any page with a matching file name.
        var match = site.Pages.FirstOrDefault(p =>
                        BaseName(p.RelativePath) == filename && DirName(p.RelativePath) == currentDir)
                    ?? site.Pages.FirstOrDefault(p => BaseName(p.RelativePath) == filename);
        if (match is not null) return Format(match.Url, mode, page);

        // Fall back to a static asset that ships to the output verbatim.
        var assetRel = ResolveAsset(filename, currentDir);
        return assetRel is null ? null : Format(assetRel, mode, page);
    }

    /// <summary>
    /// Formats a resolved root-relative target URL according to the optional macro <c>mode</c>:
    /// <list type="bullet">
    ///   <item><c>""</c>/<c>absolute</c>/<c>url</c> — full URL prefixed with <c>site_url</c> when
    ///   configured (falls back to a root-absolute <c>/path</c>). This is the default.</item>
    ///   <item><c>path</c>/<c>root</c> — a root-absolute <c>/path</c>, never including the host.</item>
    ///   <item><c>relative</c>/<c>rel</c> — a page-relative URI (<c>../</c> back to the site root),
    ///   which stays correct when the site is served under a base path.</item>
    /// </list>
    /// </summary>
    private string Format(string url, string mode, Page page)
    {
        var raw = url.TrimStart('/');
        return (mode?.Trim().ToLowerInvariant()) switch
        {
            "path" or "root" => "/" + raw,
            "relative" or "rel" => PageRenderer.BaseUrl(page.Url) + raw,
            _ => Join(raw),
        };
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

    /// <summary>Renders a download link for a resolved file URL, using the HTML5 <c>download</c>
    /// attribute so the browser saves the file (with <paramref name="fileName"/> as the suggested
    /// name) instead of navigating to it. Styled as a Material button with a download icon.</summary>
    private static string RenderDownload(string url, string text, string fileName) =>
        $"""<a class="md-button md-button--download" href="{Attr(url)}" download="{Attr(fileName)}">{Attr(text)}</a>""";

    private static string Attr(string text) => text
        .Replace("&", "&amp;")
        .Replace("\"", "&quot;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("'", "&apos;");

    [GeneratedRegex("""\{\{\s*fileuri\s*\(\s*["'](?<file>[^"']+)["']\s*(?:,\s*["'](?<mode>[^"']*)["']\s*)?\)\s*\}\}""")]
    private static partial Regex FileUriRegex();

    [GeneratedRegex("""\{\{\s*button\s*\(\s*["'](?<text>[^"']*)["']\s*,\s*["'](?<url>[^"']+)["']\s*\)\s*\}\}""")]
    private static partial Regex ButtonRegex();

    [GeneratedRegex("""\{\{\s*download\s*\(\s*["'](?<file>[^"']+)["']\s*(?:,\s*["'](?<text>[^"']*)["']\s*)?(?:,\s*["'](?<mode>[^"']*)["']\s*)?\)\s*\}\}""")]
    private static partial Regex DownloadRegex();

    [GeneratedRegex("""\{\{\s*(?<name>[A-Za-z_][\w.]*)\s*\}\}""")]
    private static partial Regex VariableRegex();
}
