using Markdig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Netdocs.Abstractions;

/// <summary>Base contract every plugin implements. Hooks are opt-in via the other interfaces.</summary>
public interface IPlugin
{
    string Name { get; }

    /// <summary>Called once at startup to register services, options, and assets.</summary>
    void Configure(IPluginContext ctx);
}

/// <summary>Services exposed to a plugin during <see cref="IPlugin.Configure"/>.</summary>
public interface IPluginContext
{
    SiteConfig Config { get; }
    BuildOptions Options { get; }
    ILogger Logger { get; }
    IServiceCollection Services { get; }

    /// <summary>Raw option map for this plugin as parsed from mkdocs.yml.</summary>
    IReadOnlyDictionary<string, object?> PluginOptions { get; }

    /// <summary>Register a CSS href to be injected into every page head.</summary>
    void AddStylesheet(string href);

    /// <summary>Register a JS src to be injected before &lt;/body&gt;.</summary>
    void AddScript(string src, bool defer = true);

    /// <summary>Copy an extra static asset (absolute source path) into the site output at destRelative.</summary>
    void AddAsset(string sourcePath, string destRelative);
}

/// <summary>Transforms raw markdown before parsing (snippets, abbreviations, macros).</summary>
public interface IMarkdownPreprocessor
{
    int Order { get; }
    Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct);
}

/// <summary>Contributes Markdig extensions to the shared pipeline.</summary>
public interface IMarkdigContributor
{
    void Extend(MarkdownPipelineBuilder builder, SiteContext site);
}

/// <summary>Produces virtual pages (blog lists, tag pages, archives).</summary>
public interface IContentGenerator
{
    IAsyncEnumerable<Page> GenerateAsync(SiteContext site, CancellationToken ct);
}

/// <summary>Lifecycle hooks across the build.</summary>
public interface IBuildHook
{
    Task OnBuildStartAsync(SiteContext site, CancellationToken ct) => Task.CompletedTask;
    Task OnPageRenderedAsync(Page page, SiteContext site, CancellationToken ct) => Task.CompletedTask;
    Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Decides whether a discovered page is included (file-filter, shadow tags, prune).</summary>
public interface INavigationFilter
{
    bool ShouldInclude(Page page, SiteContext site);
}
