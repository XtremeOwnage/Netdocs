using System.Collections.Concurrent;
using Markdig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Markdown;
using Netdocs.Core.Plugins;
using Netdocs.Core.Templating;

namespace Netdocs.Core;

/// <summary>Orchestrates the full site build pipeline.</summary>
public sealed class BuildEngine(
    SiteConfig config,
    BuildOptions options,
    PluginRegistry registry,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _log = loggerFactory.CreateLogger("Build");

    public async Task<SiteContext> BuildAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _log.LogInformation("Building '{Site}' ({Mode})", config.SiteName, options.IsProduction ? "production" : "development");

        var site = new SiteContext { Config = config, Options = options, LoggerFactory = loggerFactory };

        // Plugin host (built on a lightweight provider so plugins can take core services).
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(options);
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        // Discover external plugins shipped as ./plugins/*.dll before resolving the effective set.
        ExternalPluginLoader.Load(registry, provider, config.ProjectRoot, loggerFactory.CreateLogger("ExternalPlugins"));

        var effectivePlugins = BuildEffectivePluginList();
        var host = PluginHost.Build(config, options, effectivePlugins, registry, provider, services, loggerFactory);
        _log.LogDebug("Loaded {Count} plugins: {Plugins}", host.Plugins.Count, string.Join(", ", host.Plugins.Select(p => p.Name)));

        // 1. Discover source content.
        var discovery = new ContentDiscovery(config, loggerFactory.CreateLogger<ContentDiscovery>());
        var pages = discovery.Discover().ToList();

        // 2. Navigation filters (file-filter, shadow tags).
        var beforeFilter = pages.Count;
        pages = pages.Where(p => host.NavigationFilters.All(f => f.ShouldInclude(p, site))).ToList();
        _log.LogDebug("Navigation filters kept {Kept}/{Total} pages", pages.Count, beforeFilter);
        site.Pages.AddRange(pages);

        // 3. OnBuildStart hooks.
        _log.LogDebug("Running OnBuildStart hooks ({Count})", host.BuildHooks.Count);
        foreach (var hook in host.BuildHooks)
        {
            _log.LogTrace("OnBuildStart: {Hook}", hook.GetType().Name);
            await hook.OnBuildStartAsync(site, ct);
        }

        // 4. Content generators (blog lists, tags, archives).
        var generatedCount = 0;
        foreach (var generator in host.ContentGenerators)
            await foreach (var generated in generator.GenerateAsync(site, ct))
            {
                site.Pages.Add(generated);
                generatedCount++;
                _log.LogTrace("Generated page {Url} by {Generator}", generated.Url, generator.GetType().Name);
            }
        _log.LogDebug("Content generators produced {Count} pages", generatedCount);

        // 5. Preprocess markdown (snippets, abbreviations, macros).
        _log.LogDebug("Preprocessing {Count} pages with {Preprocessors} preprocessor(s)", site.Pages.Count, host.Preprocessors.Count);
        foreach (var page in site.Pages)
        {
            var md = page.RawMarkdown;
            foreach (var pre in host.Preprocessors)
                md = await pre.ProcessAsync(page, md, site, ct);
            page.ProcessedMarkdown = md;
        }

        // 6. Parse + render markdown in parallel (one pipeline per thread; Markdig state is not shared-safe).
        _log.LogDebug("Rendering markdown for {Count} pages (parallel)", site.Pages.Count);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in site.Pages) linkMap.TryAdd(p.RelativePath.Replace('\\', '/'), p.Url);
        using var pipelines = new ThreadLocal<MarkdownPipeline>(
            () => MarkdownPipelineFactory.Build(site, host.MarkdigContributors), trackAllValues: false);
        Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct }, page =>
        {
            var renderer = new DocumentRenderer(pipelines.Value!, linkMap);
            renderer.Render(page);
        });

        // 7. Resolve navigation.
        site.Navigation = NavigationBuilder.Build(config, site.Pages);
        site.State["nav_pages"] = NavigationBuilder.Flatten(site.Navigation);
        _log.LogDebug("Resolved navigation ({Count} top-level nodes)", site.Navigation.Count);

        // 8. Template render (parallel) + emit.
        var templateEngine = CreateTemplateEngine();
        Directory.CreateDirectory(config.AbsoluteSiteDir);
        if (options.Clean) { CleanOutput(config.AbsoluteSiteDir); _log.LogDebug("Cleaned output directory {Dir}", config.AbsoluteSiteDir); }

        var rendered = new ConcurrentBag<(string Path, string Html)>();
        Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct }, page =>
        {
            var html = PageRenderer.Render(templateEngine, site, page, host.Assets);
            rendered.Add((page.OutputPath, html));
        });
        foreach (var (path, html) in rendered)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, html, ct);
            _log.LogTrace("Wrote {Path}", path);
        }
        _log.LogDebug("Emitted {Count} HTML pages", rendered.Count);

        // 8b. 404 page.
        if (templateEngine.TryResolve("404.html", out _))
        {
            var notFound = new Page { SourcePath = "", RelativePath = "404.md", Url = "404.html", Title = "404" };
            notFound.Meta["template"] = "404.html";
            var html404 = PageRenderer.Render(templateEngine, site, notFound, host.Assets);
            await File.WriteAllTextAsync(Path.Combine(config.AbsoluteSiteDir, "404.html"), html404, ct);
        }

        // 9. OnPageRendered hooks (search docs, etc.).
        foreach (var hook in host.BuildHooks)
            foreach (var page in site.Pages)
                await hook.OnPageRenderedAsync(page, site, ct);

        // 10. Copy assets (theme + docs static + plugin-registered).
        await AssetPipeline.CopyAllAsync(config, host.Assets, ct);
        _log.LogDebug("Copied theme, static, and plugin assets to {Dir}", config.AbsoluteSiteDir);

        // 11. OnBuildComplete hooks (search index, rss, sitemap).
        foreach (var hook in host.BuildHooks)
        {
            _log.LogTrace("OnBuildComplete: {Hook}", hook.GetType().Name);
            await hook.OnBuildCompleteAsync(site, ct);
        }

        // 12. Built-in sitemap.xml.
        await EmitSitemapAsync(site, ct);

        sw.Stop();
        _log.LogInformation("Built {Count} pages in {Ms} ms", site.Pages.Count, sw.ElapsedMilliseconds);
        return site;
    }

    private async Task EmitSitemapAsync(SiteContext site, CancellationToken ct)
    {
        var baseUrl = (config.SiteUrl ?? "").TrimEnd('/');
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var page in site.Pages.OrderBy(p => p.Url, StringComparer.Ordinal))
        {
            var loc = baseUrl.Length > 0 ? $"{baseUrl}/{page.Url}" : "/" + page.Url;
            sb.Append("  <url><loc>").Append(System.Security.SecurityElement.Escape(loc)).Append("</loc>");
            if (page.Updated is { } updated)
                sb.Append("<lastmod>").Append(updated.ToString("yyyy-MM-dd")).Append("</lastmod>");
            sb.AppendLine("</url>");
        }
        sb.AppendLine("</urlset>");
        await File.WriteAllTextAsync(Path.Combine(config.AbsoluteSiteDir, "sitemap.xml"), sb.ToString(), ct);
        _log.LogDebug("Wrote sitemap.xml ({Count} urls)", site.Pages.Count);
    }

    /// <summary>Merges config plugins with plugins backed by markdown_extensions (e.g. snippets).</summary>
    private List<Abstractions.PluginConfig> BuildEffectivePluginList()
    {
        var list = new List<Abstractions.PluginConfig>();
        if (config.MarkdownExtensions.TryGetValue("pymdownx.snippets", out var snippetOptions))
            list.Add(new Abstractions.PluginConfig { Name = "snippets", Options = snippetOptions });
        list.AddRange(config.Plugins);
        return list;
    }

    private TemplateEngine CreateTemplateEngine()
    {
        var dirs = new List<string>();
        if (!string.IsNullOrEmpty(config.Theme.CustomDir))
        {
            var customDir = Path.Combine(config.ProjectRoot, config.Theme.CustomDir);
            if (Directory.Exists(customDir))
            {
                // Netdocs overrides use Scriban. Material's Jinja2 overrides are not compatible;
                // only include the override dir when it is not a Jinja2 theme override set.
                if (LooksLikeScribanOverrides(customDir))
                    dirs.Add(customDir);
                else
                    _log.LogWarning("Ignoring custom_dir '{Dir}' - it contains Jinja2 templates. Port overrides to Scriban to enable them.", config.Theme.CustomDir);
            }
        }
        dirs.Add(ThemePaths.TemplatesDir);
        return new TemplateEngine(dirs);
    }

    private static bool LooksLikeScribanOverrides(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("{%") || text.Contains("lang.t") || text.Contains("{{-") || text.Contains("super()"))
                return false;
        }
        return true;
    }

    private static void CleanOutput(string siteDir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(siteDir))
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
    }
}
