using Markdig;
using Netdocs.Abstractions;
using Netdocs.Core.Configuration;
using Netdocs.Core.Content;

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
        // Path-based `.mkdocsignore` pruning is handled during content discovery; this plugin owns
        // the front-matter label filter. Both share the same enabled gate via FileFilterSettings.
        var settings = FileFilterSettings.Load(ctx.Config.ProjectRoot);
        _active = settings.AppliesLabelFilter(ctx.Options.IsServe);
        if (!_active) return;

        _metadataProperty = settings.MetadataProperty;
        foreach (var t in settings.ExcludeTags) _excludeTags.Add(t);
        foreach (var t in settings.IncludeTags) _includeTags.Add(t);
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

/// <summary>
/// Smart typography: enables Markdig's SmartyPants so straight quotes become curly,
/// <c>--</c>/<c>---</c> become en/em dashes, and <c>...</c> becomes an ellipsis. Code
/// spans and fenced blocks are left untouched.
/// </summary>
public sealed class TypesetPlugin : IPlugin, IMarkdigContributor
{
    public string Name => "typeset";
    public void Configure(IPluginContext ctx) { }

    public void Extend(Markdig.MarkdownPipelineBuilder builder, SiteContext site)
        => builder.UseSmartyPants();
}
