using Netdocs.Abstractions;
using Netdocs.Core.Configuration;

namespace Netdocs.Core.Plugins;

/// <summary>
/// Decides whether a given plugin should run for a specific page, based on the page's front matter.
/// Every configured plugin is enabled for every page by default; front matter only expresses
/// per-page <em>overrides</em>. Two forms are supported (the map form wins over the list form):
/// <code>
/// ---
/// plugins:            # map: plugin-name -> bool
///   macros: false
///   snippets: true
/// disable_plugins:    # list: plugin-names to turn off
///   - table-reader
/// ---
/// </code>
/// Only per-page hook types are gated (Markdown preprocessors and page build hooks). Global
/// contributions such as Markdig extensions and content generators are not page-scoped.
/// </summary>
public static class PagePluginGate
{
    public static bool IsEnabled(Page page, string pluginName)
    {
        if (string.IsNullOrEmpty(pluginName)) return true;

        var enabled = true;

        // List form: disable_plugins: [a, b]
        var disableList = page.FrontMatter.Get("disable_plugins").AsList();
        foreach (var item in disableList)
        {
            if (string.Equals(item.AsString(), pluginName, StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                break;
            }
        }

        // Map form: plugins: { name: bool } — takes precedence, so it can re-enable too.
        var map = page.FrontMatter.Get("plugins").AsMap();
        foreach (var kv in map)
        {
            if (string.Equals(kv.Key, pluginName, StringComparison.OrdinalIgnoreCase))
            {
                enabled = kv.Value.AsBool(true);
                break;
            }
        }

        return enabled;
    }
}
