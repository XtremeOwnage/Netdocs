using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Plugins;

/// <summary>
/// Discovers and registers external plugins shipped as <c>./plugins/*.dll</c> next to a site.
/// Each assembly is loaded into its own <see cref="AssemblyLoadContext"/> (with dependency
/// resolution) while shared contracts (Netdocs.Abstractions) fall back to the host so plugin
/// types satisfy the host's <see cref="IPlugin"/> interface.
/// </summary>
public static class ExternalPluginLoader
{
    public static void Load(PluginRegistry registry, IServiceProvider services, string projectRoot, ILogger log)
    {
        var pluginsDir = Path.Combine(projectRoot, "plugins");
        if (!Directory.Exists(pluginsDir))
            return;

        foreach (var dll in Directory.EnumerateFiles(pluginsDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            Assembly assembly;
            try
            {
                var context = new PluginLoadContext(dll);
                assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Skipping plugin assembly '{Dll}' - failed to load", Path.GetFileName(dll));
                continue;
            }

            IEnumerable<Type> pluginTypes;
            try
            {
                pluginTypes = assembly.GetTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t));
            }
            catch (ReflectionTypeLoadException ex)
            {
                log.LogWarning(ex, "Skipping plugin assembly '{Dll}' - could not enumerate types", Path.GetFileName(dll));
                continue;
            }

            foreach (var type in pluginTypes)
            {
                try
                {
                    // Construct once to read the plugin's declared name for registry keying.
                    if (ActivatorUtilities.CreateInstance(services, type) is not IPlugin probe)
                    {
                        log.LogWarning("Type '{Type}' in '{Dll}' could not be constructed", type.FullName, Path.GetFileName(dll));
                        continue;
                    }

                    if (registry.Contains(probe.Name))
                    {
                        log.LogWarning("External plugin '{Name}' ({Type}) shadows a built-in plugin - ignoring", probe.Name, type.FullName);
                        continue;
                    }

                    registry.Register(type, probe.Name);
                    log.LogInformation("Registered external plugin '{Name}' from {Dll}", probe.Name, Path.GetFileName(dll));
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to register plugin type '{Type}' from '{Dll}'", type.FullName, Path.GetFileName(dll));
                }
            }
        }
    }

    /// <summary>Isolating load context that resolves a plugin's own dependencies but defers
    /// shared contracts to the default (host) context to preserve type identity.</summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base($"Netdocs-plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: false)
            => _resolver = new AssemblyDependencyResolver(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Shared contracts must come from the host so IPlugin & friends are the same Type.
            if (assemblyName.Name is "Netdocs.Abstractions")
                return null;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
