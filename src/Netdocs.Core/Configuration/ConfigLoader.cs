using Netdocs.Abstractions;

namespace Netdocs.Core.Configuration;

/// <summary>Loads mkdocs.yml into a <see cref="SiteConfig"/>.</summary>
public static class ConfigLoader
{
    public static SiteConfig Load(string configPath)
    {
        var projectRoot = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var yaml = File.ReadAllText(configPath);
        var root = YamlTree.Parse(yaml).AsMap();

        var config = new SiteConfig
        {
            ProjectRoot = projectRoot,
            SiteName = root.Get("site_name").AsString() ?? "Documentation",
            SiteUrl = root.Get("site_url").AsString(),
            SiteAuthor = root.Get("site_author").AsString(),
            SiteDescription = root.Get("site_description").AsString(),
            Copyright = root.Get("copyright").AsString(),
            RepoUrl = root.Get("repo_url").AsString(),
            RepoName = root.Get("repo_name").AsString(),
            EditUri = root.Get("edit_uri").AsString(),
            DocsDir = root.Get("docs_dir").AsString() ?? "docs",
            SiteDir = root.Get("site_dir").AsString() ?? "site",
            Theme = ParseTheme(root.Get("theme")),
            Nav = ParseNav(root.Get("nav")),
            MarkdownExtensions = ParseExtensionMap(root.Get("markdown_extensions")),
            Plugins = ParsePlugins(root.Get("plugins")),
            ExtraCss = root.Get("extra_css").AsList().Select(x => x.AsString() ?? "").Where(s => s.Length > 0).ToList(),
            ExtraJavaScript = root.Get("extra_javascript").AsList().Select(x => x.AsString() ?? "").Where(s => s.Length > 0).ToList(),
            Extra = root.Get("extra").AsMap(),
        };
        return config;
    }

    private static ThemeConfig ParseTheme(object? node)
    {
        var m = node.AsMap();
        return new ThemeConfig
        {
            Name = m.Get("name").AsString() ?? "material",
            Language = m.Get("language").AsString() ?? "en",
            Logo = m.Get("logo").AsString(),
            Favicon = m.Get("favicon").AsString(),
            CustomDir = m.Get("custom_dir").AsString(),
            Highlight = m.Get("highlight").AsString() ?? "highlightjs",
            Features = m.Get("features").AsList().Select(x => x.AsString() ?? "").Where(s => s.Length > 0).ToList(),
            Font = m.Get("font").AsMap(),
            Icon = m.Get("icon").AsMap(),
            Palette = ParsePalette(m.Get("palette")),
        };
    }

    private static List<PaletteConfig> ParsePalette(object? node)
    {
        var result = new List<PaletteConfig>();
        // palette may be a single map or a list of maps.
        if (node is IReadOnlyDictionary<string, object?> single)
            node = new List<object?> { single };
        foreach (var item in node.AsList())
        {
            var m = item.AsMap();
            var toggle = m.Get("toggle").AsMap();
            result.Add(new PaletteConfig
            {
                Media = m.Get("media").AsString(),
                Scheme = m.Get("scheme").AsString(),
                Primary = m.Get("primary").AsString(),
                Accent = m.Get("accent").AsString(),
                ToggleIcon = toggle.Get("icon").AsString(),
                ToggleName = toggle.Get("name").AsString(),
            });
        }
        return result;
    }

    private static List<NavItem> ParseNav(object? node)
    {
        var result = new List<NavItem>();
        foreach (var entry in node.AsList())
        {
            if (entry is string s)
            {
                result.Add(new NavItem { Path = s });
            }
            else if (entry is IReadOnlyDictionary<string, object?> map)
            {
                foreach (var (title, value) in map)
                {
                    if (value is string path)
                        result.Add(new NavItem { Title = title, Path = path });
                    else
                        result.Add(new NavItem { Title = title, Children = ParseNav(value) });
                }
            }
        }
        return result;
    }

    /// <summary>Parses a list whose items are either "name" or {name: {options}}.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, object?>> ParseExtensionMap(object? node)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, opts) in EnumerateNamedList(node))
            result[name] = opts;
        return result;
    }

    private static List<PluginConfig> ParsePlugins(object? node)
    {
        var result = new List<PluginConfig>();
        foreach (var (name, opts) in EnumerateNamedList(node))
        {
            var order = opts.Get("order") is { } orderNode ? orderNode.AsInt() : (int?)null;
            result.Add(new PluginConfig { Name = name, Options = opts, Order = order });
        }
        return result;
    }

    private static IEnumerable<(string Name, IReadOnlyDictionary<string, object?> Options)> EnumerateNamedList(object? node)
    {
        foreach (var entry in node.AsList())
        {
            if (entry is string s)
            {
                yield return (s, YamlAccess.EmptyMap);
            }
            else if (entry is IReadOnlyDictionary<string, object?> map)
            {
                foreach (var (name, value) in map)
                    yield return (name, value.AsMap());
            }
        }
    }
}
