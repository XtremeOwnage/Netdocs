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
    ILoggerFactory loggerFactory,
    Diagnostics.BuildProfiler? profiler = null)
{
    private readonly ILogger _log = loggerFactory.CreateLogger("Build");

    private static readonly IDisposable NoopScope = new NoopDisposable();
    private IDisposable Measure(string name) => profiler?.Measure(name) ?? NoopScope;

    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }


    public async Task<SiteContext> BuildAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _log.LogInformation("Building '{Site}' ({Mode})", config.SiteName, options.IsProduction ? "production" : "development");

        // Make sure the bundled theme is on disk (extracts embedded copy for single-file builds).
        ThemeBootstrapper.EnsureExtracted();

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
        List<Page> pages;
        using (Measure("1. content discovery"))
        {
            var discovery = new ContentDiscovery(config, loggerFactory.CreateLogger<ContentDiscovery>());
            pages = discovery.Discover().ToList();
        }

        // 2. Navigation filters (file-filter, shadow tags).
        using (Measure("2. navigation filters"))
        {
            var beforeFilter = pages.Count;
            pages = pages.Where(p => host.NavigationFilters.All(f => f.ShouldInclude(p, site))).ToList();
            _log.LogDebug("Navigation filters kept {Kept}/{Total} pages", pages.Count, beforeFilter);
            site.Pages.AddRange(pages);
        }

        // 3. OnBuildStart hooks.
        _log.LogDebug("Running OnBuildStart hooks ({Count})", host.BuildHooks.Count);
        using (Measure("3. OnBuildStart hooks"))
        foreach (var hook in host.BuildHooks)
        {
            _log.LogTrace("OnBuildStart: {Hook}", hook.GetType().Name);
            using (Measure(PluginLabel(hook)))
                await hook.OnBuildStartAsync(site, ct);
        }

        // 4. Content generators (blog lists, tags, archives).
        var generatedCount = 0;
        using (Measure("4. content generators"))
        foreach (var generator in host.ContentGenerators)
            using (Measure(PluginLabel(generator)))
            await foreach (var generated in generator.GenerateAsync(site, ct))
            {
                site.Pages.Add(generated);
                generatedCount++;
                _log.LogTrace("Generated page {Url} by {Generator}", generated.Url, generator.GetType().Name);
            }
        _log.LogDebug("Content generators produced {Count} pages", generatedCount);

        // 5. Preprocess markdown (snippets, abbreviations, macros).
        _log.LogDebug("Preprocessing {Count} pages with {Preprocessors} preprocessor(s)", site.Pages.Count, host.Preprocessors.Count);
        using (Measure("5. preprocess markdown"))
        foreach (var page in site.Pages)
        {
            var md = page.RawMarkdown;
            foreach (var pre in host.Preprocessors)
            {
                if (pre is IPlugin p && !PagePluginGate.IsEnabled(page, p.Name)) continue;
                using (Measure(PluginLabel(pre)))
                    md = await pre.ProcessAsync(page, md, site, ct);
            }
            page.ProcessedMarkdown = md;
        }

        // 6. Parse + render markdown in parallel (one pipeline per thread; Markdig state is not shared-safe).
        //    A content-hash cache reuses the (pure) render artifacts for pages whose markdown,
        //    pipeline, and link map are unchanged since the last build.
        _log.LogDebug("Rendering markdown for {Count} pages (parallel)", site.Pages.Count);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in site.Pages) linkMap.TryAdd(p.RelativePath.Replace('\\', '/'), p.Url);

        var cache = options.NoCache ? null : RenderCache.Load(config.ProjectRoot);
        var pipelineSalt = ComputePipelineSalt(host);
        var linkMapHash = ComputeLinkMapHash(linkMap);

        using (Measure("6. render markdown"))
        {
            using var pipelines = new ThreadLocal<MarkdownPipeline>(
                () => MarkdownPipelineFactory.Build(site, host.MarkdigContributors), trackAllValues: false);
            Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct }, page =>
            {
                var key = cache is null ? "" : RenderCache.ComputeKey(page, pipelineSalt, linkMapHash);
                if (cache is not null && cache.TryRestore(page, key)) return;

                var renderer = new DocumentRenderer(pipelines.Value!, linkMap, config.Abbreviations.FirstInstanceOnly);
                renderer.Render(page);
                cache?.Store(page, key);
            });
            if (cache is not null)
            {
                cache.Save();
                _log.LogInformation("Render cache: {Hits}/{Total} pages reused", cache.Hits, site.Pages.Count);
            }
        }

        // 7. Resolve navigation.
        using (Measure("7. resolve navigation"))
        {
            site.Navigation = NavigationBuilder.Build(config, site.Pages);
            site.State["nav_pages"] = NavigationBuilder.Flatten(site.Navigation);
            _log.LogDebug("Resolved navigation ({Count} top-level nodes)", site.Navigation.Count);
        }

        // 8. Template render (parallel) + emit.
        var templateEngine = CreateTemplateEngine();
        Directory.CreateDirectory(config.AbsoluteSiteDir);
        site.State["asset_versioner"] = new AssetVersioner(ThemePaths.AssetsDir, config.AbsoluteDocsDir);

        var rendered = new ConcurrentBag<(string Path, string Html)>();
        var renderedPages = new ConcurrentBag<Validation.RenderedPage>();
        var minify = config.Optimize.MinifyHtml;
        var webpWrap = config.Optimize.ConvertImagesToWebp;
        using (Measure("8. template render"))
        Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct }, page =>
        {
            var html = PageRenderer.Render(templateEngine, site, page, host.Assets);
            if (webpWrap) html = Optimization.WebpHtmlRewriter.Rewrite(html);
            if (minify) html = Optimization.HtmlMinifier.Minify(html);
            rendered.Add((page.OutputPath, html));
            renderedPages.Add(new Validation.RenderedPage(page, html));
        });
        var changed = 0;
        using (Measure("8. write output"))
        foreach (var (path, html) in rendered)
        {
            if (await OutputWriter.WriteTextIfChangedAsync(site, path, html, ct))
            {
                changed++;
                _log.LogTrace("Wrote {Path}", path);
            }
        }
        _log.LogInformation("Emitted {Count} HTML pages ({Changed} changed)", rendered.Count, changed);

        // 8b. 404 page.
        if (templateEngine.TryResolve("404.html", out _))
        {
            var notFound = new Page { SourcePath = "", RelativePath = "404.md", Url = "404.html", Title = "404" };
            notFound.Meta["template"] = "404.html";
            var html404 = PageRenderer.Render(templateEngine, site, notFound, host.Assets);
            if (config.Optimize.ConvertImagesToWebp) html404 = Optimization.WebpHtmlRewriter.Rewrite(html404);
            if (config.Optimize.MinifyHtml) html404 = Optimization.HtmlMinifier.Minify(html404);
            await OutputWriter.WriteTextIfChangedAsync(site, Path.Combine(config.AbsoluteSiteDir, "404.html"), html404, ct);
        }

        // 9. OnPageRendered hooks (search docs, etc.).
        using (Measure("9. OnPageRendered hooks"))
        foreach (var hook in host.BuildHooks)
            using (Measure(PluginLabel(hook)))
            foreach (var page in site.Pages)
            {
                if (hook is IPlugin p && !PagePluginGate.IsEnabled(page, p.Name)) continue;
                await hook.OnPageRenderedAsync(page, site, ct);
            }

        // 10. Copy assets (theme + docs static + plugin-registered).
        using (Measure("10. copy assets"))
            await AssetPipeline.CopyAllAsync(site, host.Assets, ct);
        _log.LogDebug("Copied theme, static, and plugin assets to {Dir}", config.AbsoluteSiteDir);

        // 11. OnBuildComplete hooks (search index, rss, sitemap).
        using (Measure("11. OnBuildComplete hooks"))
        foreach (var hook in host.BuildHooks)
        {
            _log.LogTrace("OnBuildComplete: {Hook}", hook.GetType().Name);
            using (Measure(PluginLabel(hook)))
                await hook.OnBuildCompleteAsync(site, ct);
        }

        // 12. Built-in sitemap.xml.
        using (Measure("12. sitemap"))
            await EmitSitemapAsync(site, ct);

        // 13. Prune stale files: anything in the output dir this build did not (re)produce.
        // This replaces an up-front wipe so unchanged files keep their bytes and timestamps,
        // which is what makes incremental republishing cheap for the watch daemon.
        using (Measure("13. prune stale"))
        {
            var pruned = OutputWriter.PruneStale(site, config.AbsoluteSiteDir);
            if (pruned > 0) _log.LogInformation("Pruned {Count} stale output files", pruned);
        }

        // 13b. Offline self-hosting: download external CDN assets and rewrite pages to local copies.
        // Tri-state: null => self-host on production builds only (not serve/dev); true/false forces it.
        var offline = config.Optimize.Offline ?? (options.IsProduction && !options.IsServe);
        if (offline)
            using (Measure("13b. self-host assets"))
                await Optimization.SelfHostAssets.RunAsync(site, _log, ct);

        // 14. Optional build-time validation (links, anchors, unused images, orphan pages).
        // Runs last so every page/asset/plugin output is materialized on disk. Problems are
        // logged as warnings; `--strict` (or MKDOCS_STRICT) turns them into a failing build.
        using (Measure("14. validation"))
            Validation.BuildValidator.Validate(site, renderedPages.ToList(), _log);

        sw.Stop();
        _log.LogInformation("Built {Count} pages in {Ms} ms", site.Pages.Count, sw.ElapsedMilliseconds);
        return site;
    }

    private static string PluginLabel(object component) =>
        component is IPlugin p ? p.Name : component.GetType().Name;

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
        await OutputWriter.WriteTextIfChangedAsync(site, Path.Combine(config.AbsoluteSiteDir, "sitemap.xml"), sb.ToString(), ct);
        _log.LogDebug("Wrote sitemap.xml ({Count} urls)", site.Pages.Count);
    }

    /// <summary>Salt that invalidates the render cache when the markdown pipeline changes:
    /// the engine assembly version, the configured markdown extensions, and the active
    /// Markdig contributors (e.g. typeset/SmartyPants).</summary>
    private string ComputePipelineSalt(PluginHost host)
    {
        var version = typeof(BuildEngine).Assembly.GetName().Version?.ToString() ?? "0";
        var extensions = string.Join(",", config.MarkdownExtensions.Keys.OrderBy(k => k, StringComparer.Ordinal));
        var contributors = string.Join(",", host.MarkdigContributors.Select(c => c.GetType().FullName).OrderBy(n => n, StringComparer.Ordinal));
        return $"{version}|{extensions}|{contributors}|abbr1st={config.Abbreviations.FirstInstanceOnly}";
    }

    private static string ComputeLinkMapHash(IReadOnlyDictionary<string, string> linkMap)
    {
        var payload = string.Join('\n', linkMap.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
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
}
