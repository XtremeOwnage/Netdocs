using Netdocs.Abstractions;
using Netdocs.Core.Configuration;

namespace Netdocs.Plugins;

/// <summary>
/// Env-driven label include/exclude filter, mirroring mkdocs-file-filter. Reads
/// <c>.file-filter.yml</c> from the project root and prunes pages whose front-matter
/// labels (default property <c>labels</c>) match an <c>exclude_tag</c> and not an
/// <c>include_tag</c>. Honors <c>enabled</c> / <c>enabled_on_serve</c>.
/// </summary>
public sealed class FileFilterPlugin : IPlugin, INavigationFilter
{
    private bool _active;
    private string _metadataProperty = "labels";
    private readonly HashSet<string> _excludeTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _includeTags = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "file-filter";

    public void Configure(IPluginContext ctx)
    {
        var path = Path.Combine(ctx.Config.ProjectRoot, ".file-filter.yml");
        if (!File.Exists(path)) { _active = false; return; }

        var root = YamlTree.Parse(File.ReadAllText(path)).AsMap();

        var enabled = root.Get("enabled").AsBool(true);
        var enabledOnServe = root.TryGetValue("enabled_on_serve", out var eos) ? eos.AsBool(true) : true;
        _active = ctx.Options.IsServe ? enabled && enabledOnServe : enabled;

        if (root.Get("metadata_property").AsString() is { Length: > 0 } prop) _metadataProperty = prop;
        foreach (var t in root.Get("exclude_tag").AsList())
            if (t.AsString() is { Length: > 0 } s) _excludeTags.Add(s);
        foreach (var t in root.Get("include_tag").AsList())
            if (t.AsString() is { Length: > 0 } s) _includeTags.Add(s);

        if (_excludeTags.Count == 0) _active = false;
    }

    public bool ShouldInclude(Page page, SiteContext site)
    {
        if (!_active) return true;

        var labels = ReadLabels(page);
        if (labels.Count == 0) return true;

        // An explicit include label always wins; otherwise any exclude label prunes the page.
        if (_includeTags.Count > 0 && labels.Any(_includeTags.Contains)) return true;
        return !labels.Any(_excludeTags.Contains);
    }

    private List<string> ReadLabels(Page page)
    {
        if (page.FrontMatter.TryGetValue(_metadataProperty, out var v) && v is IEnumerable<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        return [];
    }
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

public sealed class MacrosPlugin : IPlugin
{
    public string Name => "macros";
    public void Configure(IPluginContext ctx) { }
}
