using Netdocs.Abstractions;
using Netdocs.Core.Configuration;

namespace Netdocs.Plugins;

/// <summary>Applies per-directory <c>.meta.yml</c> front-matter defaults to pages beneath it.</summary>
public sealed class MetaPlugin : IPlugin, IBuildHook
{
    private string _metaFile = ".meta.yml";

    public string Name => "meta";

    public void Configure(IPluginContext ctx)
    {
        if (ctx.PluginOptions.TryGetValue("meta_file", out var m) && m is string s && s.Length > 0)
            _metaFile = s;
    }

    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        var docsDir = site.Config.AbsoluteDocsDir;
        var cache = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in site.Pages)
        {
            if (page.IsGenerated) continue;
            var defaults = CollectDefaults(docsDir, Path.GetDirectoryName(page.SourcePath)!, cache);
            if (defaults.Count == 0) continue;

            var merged = new Dictionary<string, object?>(page.FrontMatter, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in defaults)
                merged.TryAdd(key, value);
            page.FrontMatter = merged;

            if (string.IsNullOrEmpty(page.Title) && merged.TryGetValue("title", out var t) && t is string title)
                page.Title = title;
        }
        return Task.CompletedTask;
    }

    private IReadOnlyDictionary<string, object?> CollectDefaults(
        string docsDir, string dir, Dictionary<string, IReadOnlyDictionary<string, object?>> cache)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var chain = new List<string>();
        var current = dir;
        while (current.StartsWith(docsDir, StringComparison.OrdinalIgnoreCase))
        {
            chain.Add(current);
            if (string.Equals(current, docsDir, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current)!;
        }
        chain.Reverse();

        foreach (var folder in chain)
        {
            if (!cache.TryGetValue(folder, out var meta))
            {
                var file = Path.Combine(folder, _metaFile);
                meta = File.Exists(file)
                    ? YamlTree.Parse(File.ReadAllText(file)).AsMap()
                    : YamlAccess.EmptyMap;
                cache[folder] = meta;
            }
            foreach (var (key, value) in meta)
                merged[key] = value;
        }
        return merged;
    }
}
