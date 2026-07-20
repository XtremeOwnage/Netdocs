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
        var effectivePlugins = BuildEffectivePluginList();
        var host = PluginHost.Build(config, options, effectivePlugins, registry, provider, services, loggerFactory);

        // 1. Discover source content.
        var discovery = new ContentDiscovery(config, loggerFactory.CreateLogger<ContentDiscovery>());
        var pages = discovery.Discover().ToList();

        // 2. Navigation filters (file-filter, shadow tags).
        pages = pages.Where(p => host.NavigationFilters.All(f => f.ShouldInclude(p, site))).ToList();
        site.Pages.AddRange(pages);

        // 3. OnBuildStart hooks.
        foreach (var hook in host.BuildHooks)
            await hook.OnBuildStartAsync(site, ct);

        // 4. Content generators (blog lists, tags, archives).
        foreach (var generator in host.ContentGenerators)
            await foreach (var generated in generator.GenerateAsync(site, ct))
                site.Pages.Add(generated);

        // 5. Preprocess markdown (snippets, abbreviations, macros).
        foreach (var page in site.Pages)
        {
            var md = page.RawMarkdown;
            foreach (var pre in host.Preprocessors)
                md = await pre.ProcessAsync(page, md, site, ct);
            page.ProcessedMarkdown = md;
        }

        // 6. Parse + render markdown in parallel (one pipeline per thread; Markdig state is not shared-safe).
        using var pipelines = new ThreadLocal<MarkdownPipeline>(
            () => MarkdownPipelineFactory.Build(site, host.MarkdigContributors), trackAllValues: false);
        Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct }, page =>
        {
            var renderer = new DocumentRenderer(pipelines.Value!);
            renderer.Render(page);
        });

        // 7. Resolve navigation.
        site.Navigation = NavigationBuilder.Build(config, site.Pages);

        // 8. Template render (parallel) + emit.
        var templateEngine = CreateTemplateEngine();
        Directory.CreateDirectory(config.AbsoluteSiteDir);
        if (options.Clean) CleanOutput(config.AbsoluteSiteDir);

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
        }

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

        // 11. OnBuildComplete hooks (search index, rss, sitemap).
        foreach (var hook in host.BuildHooks)
            await hook.OnBuildCompleteAsync(site, ct);

        sw.Stop();
        _log.LogInformation("Built {Count} pages in {Ms} ms", site.Pages.Count, sw.ElapsedMilliseconds);
        return site;
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
