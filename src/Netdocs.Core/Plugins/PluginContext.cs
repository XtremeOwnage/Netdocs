using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Plugins;

/// <summary>Assets/scripts/styles a plugin injected during configuration.</summary>
public sealed class PluginAssets
{
    public List<string> Stylesheets { get; } = [];
    public List<(string Src, bool Defer)> Scripts { get; } = [];
    public List<string> InlineScripts { get; } = [];
    public List<(string Source, string Dest)> Files { get; } = [];
}

internal sealed class PluginContext(
    SiteConfig config,
    BuildOptions options,
    ILogger logger,
    IServiceCollection services,
    IReadOnlyDictionary<string, object?> pluginOptions,
    PluginAssets assets) : IPluginContext
{
    public SiteConfig Config => config;
    public BuildOptions Options => options;
    public ILogger Logger => logger;
    public IServiceCollection Services => services;
    public IReadOnlyDictionary<string, object?> PluginOptions => pluginOptions;

    public void AddStylesheet(string href) => assets.Stylesheets.Add(href);
    public void AddScript(string src, bool defer = true) => assets.Scripts.Add((src, defer));
    public void AddInlineScript(string javascript) => assets.InlineScripts.Add(javascript);
    public void AddAsset(string sourcePath, string destRelative) => assets.Files.Add((sourcePath, destRelative));
}

/// <summary>Maps plugin names (from mkdocs.yml) to their implementing types.</summary>
public sealed class PluginRegistry
{
    private readonly Dictionary<string, Type> _map = new(StringComparer.OrdinalIgnoreCase);

    public PluginRegistry Register<TPlugin>(params string[] names) where TPlugin : IPlugin
    {
        foreach (var name in names) _map[name] = typeof(TPlugin);
        return this;
    }

    /// <summary>Registers a plugin type discovered at runtime (e.g. an external assembly).</summary>
    public PluginRegistry Register(Type pluginType, params string[] names)
    {
        if (!typeof(IPlugin).IsAssignableFrom(pluginType))
            throw new ArgumentException($"{pluginType} does not implement IPlugin", nameof(pluginType));
        foreach (var name in names) _map[name] = pluginType;
        return this;
    }

    public bool Contains(string name) => _map.ContainsKey(name);

    public bool TryResolve(string name, out Type type) => _map.TryGetValue(name, out type!);
}
