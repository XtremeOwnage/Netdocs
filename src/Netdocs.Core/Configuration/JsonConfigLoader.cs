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
            EditUri = root.Get("editUri").AsString(),
            DocsDir = root.Get("docsDir").AsString() ?? "docs",
            SiteDir = root.Get("siteDir").AsString() ?? "site",
            Theme = ParseTheme(root.Get("theme").AsMap()),
            Nav = ParseNav(root.Get("nav")),
            MarkdownExtensions = ParseNamed(root.Get("markdownExtensions")),
            Plugins = ParsePlugins(root.Get("plugins")),
            ExtraCss = StringList(root.Get("extraCss")),
            ExtraJavaScript = StringList(root.Get("extraJavaScript")),
            Exclude = StringList(root.Get("exclude")),
            Extra = root.Get("extra").AsMap(),
            Slugify = ParseSlugify(root.Get("slugify").AsMap()),
            Deploy = ParseDeploy(root.Get("deploy").AsMap()),
            Optimize = ParseOptimize(root.Get("optimize").AsMap()),
            Validation = ParseValidation(root.Get("validation").AsMap()),
        };
    }

    private static ValidationConfig ParseValidation(IReadOnlyDictionary<string, object?> m) => new()
    {
        Links = m.Get("links").AsBool(false),
        Anchors = m.Get("anchors").AsBool(false),
        UnusedImages = m.Get("unusedImages").AsBool(false),
        OrphanPages = m.Get("orphanPages").AsBool(false),
    };

    private static DeployConfig ParseDeploy(IReadOnlyDictionary<string, object?> m) => new()
    {
        Target = m.Get("target").AsString() ?? "none",
        Path = m.Get("path").AsString(),
        Clean = m.Get("clean").AsBool(true),
        Branch = m.Get("branch").AsString() ?? "gh-pages",
        Remote = m.Get("remote").AsString() ?? "origin",
        Message = m.Get("message").AsString() ?? "Deploy docs",
        Push = m.Get("push").AsBool(true),
        Bucket = m.Get("bucket").AsString(),
        Prefix = m.Get("prefix").AsString(),
        Region = m.Get("region").AsString(),
    };

    private static OptimizeConfig ParseOptimize(IReadOnlyDictionary<string, object?> m) => new()
    {
        MinifyHtml = m.Get("minifyHtml").AsBool(false),
        MinifyCss = m.Get("minifyCss").AsBool(false),
        MinifyJs = m.Get("minifyJs").AsBool(false),
        ConvertImagesToWebp = m.Get("convertImagesToWebp").AsBool(false),
        WebpQuality = m.Get("webpQuality").AsInt(80),
        Offline = m.Get("offline").AsBool(false),
    };

    private static SlugifyConfig ParseSlugify(IReadOnlyDictionary<string, object?> m) => new()
    {
        Case = m.Get("case").AsString() ?? "lower",
        Separator = m.Get("separator").AsString() ?? "-",
        Ascii = m.Get("ascii").AsBool(false),
    };

    private static ThemeConfig ParseTheme(IReadOnlyDictionary<string, object?> m) => new()
    {
        Name = m.Get("name").AsString() ?? "material",
        Language = m.Get("language").AsString() ?? "en",
        Logo = m.Get("logo").AsString(),
        Favicon = m.Get("favicon").AsString(),
        CustomDir = m.Get("customDir").AsString(),
        Highlight = m.Get("highlight").AsString() ?? "highlightjs",
        Features = StringList(m.Get("features")),
        Font = m.Get("font").AsMap(),
        Icon = m.Get("icon").AsMap(),
        Palette = m.Get("palette").AsList().Select(p => p.AsMap()).Select(pm =>
        {
            var toggle = pm.Get("toggle").AsMap();
            return new PaletteConfig
            {
                Media = pm.Get("media").AsString(),
                Scheme = pm.Get("scheme").AsString(),
                Primary = pm.Get("primary").AsString(),
                Accent = pm.Get("accent").AsString(),
                ToggleIcon = toggle.Get("icon").AsString(),
                ToggleName = toggle.Get("name").AsString(),
            };
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
            var icon = m.Get("icon").AsString();
            var children = m.Get("children");
            result.Add(children is not null
                ? new NavItem { Title = title, Icon = icon, Children = ParseNav(children) }
                : new NavItem { Title = title, Path = path, Icon = icon });
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
            var order = m.Get("order") is { } orderNode ? orderNode.AsInt() : (int?)null;
            result.Add(new PluginConfig { Name = name, Options = m.Get("options").AsMap(), Order = order });
        }
        return result;
    }

    private static List<string> StringList(object? node) =>
        node.AsList().Select(x => x.AsString() ?? "").Where(s => s.Length > 0).ToList();
}
