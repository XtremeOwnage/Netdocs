using System.Text.Json;
using Netdocs.Abstractions;

namespace Netdocs.Core.Configuration;

/// <summary>
/// Loads a <see cref="SiteConfig"/> from the <c>Netdocs</c> section of an appsettings.json file.
/// Clean explicit schema: nav/plugins/markdownExtensions are arrays of {name, path, options, ...}.
/// </summary>
public static class JsonConfigLoader
{
    public static SiteConfig Load(string appSettingsPath)
    {
        var projectRoot = Path.GetDirectoryName(Path.GetFullPath(appSettingsPath))!;
        using var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        if (!doc.RootElement.TryGetProperty("Netdocs", out var site))
            throw new InvalidOperationException($"'{appSettingsPath}' is missing the required 'Netdocs' section.");

        var root = JsonTree.ToMap(site);
        return new SiteConfig
        {
            ProjectRoot = projectRoot,
            SiteName = root.Get("siteName").AsString() ?? "Documentation",
            SiteUrl = root.Get("siteUrl").AsString(),
            SiteAuthor = root.Get("siteAuthor").AsString(),
            SiteDescription = root.Get("siteDescription").AsString(),
            Copyright = root.Get("copyright").AsString(),
            RepoUrl = root.Get("repoUrl").AsString(),
            RepoName = root.Get("repoName").AsString(),
            DocsDir = root.Get("docsDir").AsString() ?? "docs",
            SiteDir = root.Get("siteDir").AsString() ?? "site",
            Theme = ParseTheme(root.Get("theme").AsMap()),
            Nav = ParseNav(root.Get("nav")),
            MarkdownExtensions = ParseNamed(root.Get("markdownExtensions")),
            Plugins = ParsePlugins(root.Get("plugins")),
            ExtraCss = StringList(root.Get("extraCss")),
            ExtraJavaScript = StringList(root.Get("extraJavaScript")),
            Extra = root.Get("extra").AsMap(),
        };
    }

    private static ThemeConfig ParseTheme(IReadOnlyDictionary<string, object?> m) => new()
    {
        Name = m.Get("name").AsString() ?? "material",
        Language = m.Get("language").AsString() ?? "en",
        Logo = m.Get("logo").AsString(),
        Favicon = m.Get("favicon").AsString(),
        CustomDir = m.Get("customDir").AsString(),
        Features = StringList(m.Get("features")),
        Font = m.Get("font").AsMap(),
        Icon = m.Get("icon").AsMap(),
        Palette = m.Get("palette").AsList().Select(p => p.AsMap()).Select(pm => new PaletteConfig
        {
            Media = pm.Get("media").AsString(),
            Scheme = pm.Get("scheme").AsString(),
            Primary = pm.Get("primary").AsString(),
            Accent = pm.Get("accent").AsString(),
        }).ToList(),
    };

    private static List<NavItem> ParseNav(object? node)
    {
        var result = new List<NavItem>();
        foreach (var entry in node.AsList())
        {
            var m = entry.AsMap();
            var title = m.Get("title").AsString();
            var path = m.Get("path").AsString();
            var children = m.Get("children");
            result.Add(children is not null
                ? new NavItem { Title = title, Children = ParseNav(children) }
                : new NavItem { Title = title, Path = path });
        }
        return result;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> ParseNamed(object? node)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in node.AsList())
        {
            var m = entry.AsMap();
            var name = m.Get("name").AsString();
            if (name is not null) result[name] = m.Get("options").AsMap();
        }
        return result;
    }

    private static List<PluginConfig> ParsePlugins(object? node)
    {
        var result = new List<PluginConfig>();
        foreach (var entry in node.AsList())
        {
            var m = entry.AsMap();
            var name = m.Get("name").AsString();
            if (name is null) continue;
            if (m.Get("enabled").AsBool(true) == false) continue;
            result.Add(new PluginConfig { Name = name, Options = m.Get("options").AsMap() });
        }
        return result;
    }

    private static List<string> StringList(object? node) =>
        node.AsList().Select(x => x.AsString() ?? "").Where(s => s.Length > 0).ToList();
}
