using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Plugins;

/// <summary>Instantiates configured plugins and exposes their hook implementations.</summary>
public sealed class PluginHost
{
    public IReadOnlyList<IPlugin> Plugins { get; }
    public PluginAssets Assets { get; }

    public IReadOnlyList<IMarkdownPreprocessor> Preprocessors { get; }
    public IReadOnlyList<IMarkdigContributor> MarkdigContributors { get; }
    public IReadOnlyList<IContentGenerator> ContentGenerators { get; }
    public IReadOnlyList<IBuildHook> BuildHooks { get; }
    public IReadOnlyList<INavigationFilter> NavigationFilters { get; }

    private PluginHost(List<IPlugin> plugins, PluginAssets assets, IReadOnlyDictionary<IPlugin, int?> orderOverrides)
    {
        Plugins = plugins;
        Assets = assets;
        Preprocessors = plugins.OfType<IMarkdownPreprocessor>()
            .OrderBy(p => orderOverrides.TryGetValue((IPlugin)p, out var o) && o.HasValue ? o.Value : p.Order)
            .ToList();
        MarkdigContributors = plugins.OfType<IMarkdigContributor>().ToList();
        ContentGenerators = plugins.OfType<IContentGenerator>().ToList();
        BuildHooks = plugins.OfType<IBuildHook>().ToList();
        NavigationFilters = plugins.OfType<INavigationFilter>().ToList();
    }

    public static PluginHost Build(
        SiteConfig config,
        BuildOptions options,
        IEnumerable<PluginConfig> pluginConfigs,
        PluginRegistry registry,
        IServiceProvider services,
        IServiceCollection serviceCollection,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("PluginHost");
        var plugins = new List<IPlugin>();
        var assets = new PluginAssets();
        var orderOverrides = new Dictionary<IPlugin, int?>();

        foreach (var entry in pluginConfigs)
        {
            if (!registry.TryResolve(entry.Name, out var type))
            {
                log.LogWarning("No implementation for plugin '{Name}' - skipping", entry.Name);
                continue;
            }

            if (ActivatorUtilities.CreateInstance(services, type) is not IPlugin plugin)
            {
                log.LogWarning("Plugin '{Name}' could not be created", entry.Name);
                continue;
            }

            var ctx = new PluginContext(config, options, loggerFactory.CreateLogger(plugin.Name),
                serviceCollection, entry.Options, assets);
            try
            {
                plugin.Configure(ctx);
                plugins.Add(plugin);
                orderOverrides[plugin] = entry.Order;
                log.LogDebug("Loaded plugin {Name}", plugin.Name);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Plugin '{Name}' failed to configure", entry.Name);
                if (options.Strict) throw;
            }
        }

        return new PluginHost(plugins, assets, orderOverrides);
    }
}
