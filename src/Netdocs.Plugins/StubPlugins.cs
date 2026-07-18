using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>Includes all pages (env/label filtering via .file-filter.yml is a future enhancement).</summary>
public sealed class FileFilterPlugin : IPlugin, INavigationFilter
{
    public string Name => "file-filter";
    public void Configure(IPluginContext ctx) { }
    public bool ShouldInclude(Page page, SiteContext site) => true;
}

/// <summary>Injects the GLightbox assets used for image lightboxes.</summary>
public sealed class GlightboxPlugin : IPlugin
{
    public string Name => "glightbox";

    public void Configure(IPluginContext ctx)
    {
        ctx.AddStylesheet("https://cdn.jsdelivr.net/npm/glightbox/dist/css/glightbox.min.css");
        ctx.AddScript("https://cdn.jsdelivr.net/npm/glightbox/dist/js/glightbox.min.js");
        ctx.AddInlineScript("""
            document.addEventListener("DOMContentLoaded", function () {
              if (!window.GLightbox) return;
              document.querySelectorAll(".md-content img").forEach(function (img) {
                if (img.closest("a")) return;
                var a = document.createElement("a");
                a.href = img.src; a.className = "glightbox";
                img.parentNode.insertBefore(a, img); a.appendChild(img);
              });
              GLightbox({ selector: ".glightbox", touchNavigation: true, zoomable: true });
            });
            """);
    }
}

/// <summary>No-op passthrough plugins (behavior handled elsewhere or deferred).</summary>
public sealed class NoopPlugin(string name) : IPlugin
{
    public string Name { get; } = name;
    public void Configure(IPluginContext ctx) { }
}

public sealed class TypesetPlugin : IPlugin
{
    public string Name => "typeset";
    public void Configure(IPluginContext ctx) { }
}

public sealed class TableReaderPlugin : IPlugin
{
    public string Name => "table-reader";
    public void Configure(IPluginContext ctx) { }
}

public sealed class SocialPlugin : IPlugin
{
    public string Name => "social";
    public void Configure(IPluginContext ctx) { }
}

public sealed class MacrosPlugin : IPlugin
{
    public string Name => "macros";
    public void Configure(IPluginContext ctx) { }
}
