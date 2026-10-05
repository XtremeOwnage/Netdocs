using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Markdown;
using Netdocs.Core.Plugins;

namespace Netdocs.Core.Export;

/// <summary>A single markdown file rendered as a self-standing HTML document.</summary>
/// <param name="Html">The full HTML document.</param>
/// <param name="Title">The page title.</param>
/// <param name="BaseDirectory">Folder relative links in the page resolve against.</param>
public sealed record StandalonePage(string Html, string Title, string BaseDirectory);

/// <summary>
/// Renders one markdown file through the normal pipeline (front matter, plugin preprocessors,
/// Markdig extensions, theme palette) into the <c>export.html</c> template: the page content
/// alone, without the site chrome. Theme assets are referenced from the on-disk theme folder
/// and relative content links resolve against the docs folder, so the document renders from
/// any location without copying a site.
/// </summary>
public static class StandalonePageBuilder
{
    public const string TemplateName = "export.html";

    /// <param name="config">The site config, or a default one when no appsettings.json exists.
    /// When the source file sits inside <see cref="SiteConfig.AbsoluteDocsDir"/>, links resolve
    /// from the docs root exactly as in a site build; otherwise from the file's own folder.</param>
    /// <param name="scheme">Material color scheme to force (<c>default</c>/<c>slate</c>), or null
    /// for the site's first palette.</param>
    public static async Task<StandalonePage> BuildAsync(
        SiteConfig config,
        BuildOptions options,
        PluginRegistry registry,
        ILoggerFactory loggerFactory,
        string sourcePath,
        string? scheme,
        CancellationToken ct = default)
    {
        var log = loggerFactory.CreateLogger("Export");
        ThemeBootstrapper.EnsureExtracted();

        sourcePath = Path.GetFullPath(sourcePath);
        var docsDir = Path.GetFullPath(config.AbsoluteDocsDir);
        var baseDir = IsUnder(sourcePath, docsDir) ? docsDir : Path.GetDirectoryName(sourcePath)!;
        var relative = Path.GetRelativePath(baseDir, sourcePath).Replace('\\', '/');

        var (meta, body) = FrontMatter.Split(await File.ReadAllTextAsync(sourcePath, ct));
        var page = new Page
        {
            SourcePath = sourcePath,
            RelativePath = relative,
            RawMarkdown = body,
            FrontMatter = meta,
            Url = "",
        };
        if (meta.TryGetValue("title", out var t) && t is string title) page.Title = title;
        if (meta.TryGetValue("page_title", out var pt) && pt is string pageTitle && pageTitle.Length > 0) page.PageTitle = pageTitle;
        page.Meta["template"] = TemplateName;

        var site = new SiteContext { Config = config, Options = options, LoggerFactory = loggerFactory };
        site.Pages.Add(page);

        // Plugins: only the per-page parts of the build apply to a single file (markdown
        // preprocessors and Markdig contributors); site-wide generators and hooks are skipped.
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(options);
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        ExternalPluginLoader.Load(registry, provider, config.ProjectRoot, loggerFactory.CreateLogger("ExternalPlugins"));
        var host = PluginHost.Build(config, options, BuildEngine.BuildEffectivePluginList(config), registry, provider, services, loggerFactory);

        var markdown = page.RawMarkdown;
        foreach (var pre in host.Preprocessors)
        {
            if (pre is IPlugin p && !PagePluginGate.IsEnabled(page, p.Name)) continue;
            markdown = await pre.ProcessAsync(page, markdown, site, ct);
        }
        page.ProcessedMarkdown = markdown;

        var pipeline = MarkdownPipelineFactory.Build(site, host.MarkdigContributors);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [relative] = "" };
        new DocumentRenderer(pipeline, linkMap, config.Abbreviations.FirstInstanceOnly).Render(page);

        var engine = BuildEngine.CreateTemplateEngine(config, log);
        if (!engine.TryResolve(TemplateName, out _))
            throw new InvalidOperationException($"The theme has no '{TemplateName}' template.");

        var (paletteScheme, primary, accent) = ResolvePalette(config, scheme);
        var overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["base_url"] = DirectoryUri(ThemePaths.Root),
            ["export_base_href"] = DirectoryUri(baseDir),
            ["export_stylesheets"] = ResolveAssetUrls(config.ExtraCss.Concat(host.Assets.Stylesheets), host.Assets, baseDir),
            ["export_scripts"] = ResolveAssetUrls(
                host.Assets.Scripts.Select(s => s.Src).Concat(config.ExtraJavaScript), host.Assets, baseDir),
            ["palette_scheme"] = paletteScheme,
            ["palette_primary"] = primary,
            ["palette_accent"] = accent,
        };

        var html = PageRenderer.Render(engine, site, page, host.Assets, overrides);
        return new StandalonePage(html, page.DisplayTitle, baseDir);
    }

    /// <summary>Picks the scheme plus the primary/accent colors of the matching configured
    /// palette (so a dark export of a site with a light/dark toggle keeps the dark palette's
    /// colors), falling back to the first palette and then Material's defaults.</summary>
    internal static (string Scheme, string Primary, string Accent) ResolvePalette(SiteConfig config, string? scheme)
    {
        var palettes = config.Theme.Palette;
        var match = scheme is null
            ? palettes.FirstOrDefault()
            : palettes.FirstOrDefault(p => PageRenderer.Sanitize(p.Scheme ?? "default") == scheme) ?? palettes.FirstOrDefault();
        return (
            scheme ?? PageRenderer.Sanitize(match?.Scheme ?? "default"),
            PageRenderer.Sanitize(match?.Primary ?? "indigo"),
            PageRenderer.Sanitize(match?.Accent ?? "indigo"));
    }

    /// <summary>Turns site-relative stylesheet/script hrefs into URLs that load without a built
    /// site: remote URLs pass through, plugin-registered files point at their source on disk,
    /// and everything else resolves from the docs folder.</summary>
    private static List<string> ResolveAssetUrls(IEnumerable<string> hrefs, PluginAssets assets, string baseDir)
    {
        var result = new List<string>();
        foreach (var href in hrefs)
        {
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("//", StringComparison.Ordinal)
                || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(href);
                continue;
            }

            var path = href.TrimStart('/');
            var cut = path.IndexOfAny(['?', '#']);
            if (cut >= 0) path = path[..cut];
            var file = assets.Files.FirstOrDefault(f => f.Dest.Replace('\\', '/').TrimStart('/') == path).Source
                ?? Path.Combine(baseDir, path);
            result.Add(new Uri(Path.GetFullPath(file)).AbsoluteUri);
        }
        return result;
    }

    private static string DirectoryUri(string dir) =>
        new Uri(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar).AbsoluteUri;

    private static bool IsUnder(string file, string dir)
    {
        var rel = Path.GetRelativePath(dir, file);
        return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }
}
