using System.Text.Json;
using System.Text.Json.Nodes;
using Netdocs.Abstractions;

namespace Netdocs.Core.Configuration;

/// <summary>
/// Converts an <c>mkdocs.yml</c> into an equivalent Netdocs <c>appsettings.json</c>, reusing
/// <see cref="ConfigLoader"/> to parse the YAML into a <see cref="SiteConfig"/> and then emitting
/// the explicit Netdocs JSON schema (camelCase keys under a top-level <c>Netdocs</c> section).
/// </summary>
public static class MkDocsImporter
{
    /// <summary>Parses <paramref name="mkdocsYamlPath"/> and returns pretty-printed appsettings.json text.</summary>
    public static string ConvertToJson(string mkdocsYamlPath)
    {
        var config = ConfigLoader.Load(mkdocsYamlPath);
        var root = new JsonObject
        {
            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject { ["Default"] = "Information", ["Netdocs"] = "Information" },
            },
            ["Netdocs"] = BuildNetdocs(config),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildNetdocs(SiteConfig c)
    {
        var o = new JsonObject { ["siteName"] = c.SiteName };
        AddIfSet(o, "siteUrl", c.SiteUrl);
        AddIfSet(o, "siteAuthor", c.SiteAuthor);
        AddIfSet(o, "siteDescription", c.SiteDescription);
        AddIfSet(o, "copyright", c.Copyright);
        AddIfSet(o, "repoUrl", c.RepoUrl);
        AddIfSet(o, "repoName", c.RepoName);
        o["docsDir"] = c.DocsDir;
        o["siteDir"] = c.SiteDir;

        o["theme"] = BuildTheme(c.Theme);

        if (c.Nav.Count > 0) o["nav"] = BuildNav(c.Nav);
        if (c.Plugins.Count > 0) o["plugins"] = BuildNamedList(c.Plugins.Select(p => (p.Name, p.Options)));
        if (c.MarkdownExtensions.Count > 0)
            o["markdownExtensions"] = BuildNamedList(c.MarkdownExtensions.Select(kv => (kv.Key, kv.Value)));
        if (c.ExtraCss.Count > 0) o["extraCss"] = ToArray(c.ExtraCss);
        if (c.ExtraJavaScript.Count > 0) o["extraJavaScript"] = ToArray(c.ExtraJavaScript);
        if (c.Extra.Count > 0) o["extra"] = (JsonNode?)ToNode(c.Extra) ?? new JsonObject();
        return o;
    }

    private static JsonObject BuildTheme(ThemeConfig t)
    {
        var o = new JsonObject { ["name"] = t.Name, ["language"] = t.Language };
        AddIfSet(o, "logo", t.Logo);
        AddIfSet(o, "favicon", t.Favicon);
        AddIfSet(o, "customDir", t.CustomDir);
        if (t.Font.Count > 0) o["font"] = (JsonNode?)ToNode(t.Font) ?? new JsonObject();
        if (t.Icon.Count > 0) o["icon"] = (JsonNode?)ToNode(t.Icon) ?? new JsonObject();
        if (t.Palette.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var p in t.Palette)
            {
                var po = new JsonObject();
                AddIfSet(po, "media", p.Media);
                AddIfSet(po, "scheme", p.Scheme);
                AddIfSet(po, "primary", p.Primary);
                AddIfSet(po, "accent", p.Accent);
                arr.Add(po);
            }
            o["palette"] = arr;
        }
        if (t.Features.Count > 0) o["features"] = ToArray(t.Features);
        return o;
    }

    private static JsonArray BuildNav(IEnumerable<NavItem> items)
    {
        var arr = new JsonArray();
        foreach (var item in items)
        {
            var o = new JsonObject();
            AddIfSet(o, "title", item.Title);
            if (item.Children.Count > 0)
                o["children"] = BuildNav(item.Children);
            else
                AddIfSet(o, "path", item.Path);
            arr.Add(o);
        }
        return arr;
    }

    private static JsonArray BuildNamedList(IEnumerable<(string Name, IReadOnlyDictionary<string, object?> Options)> items)
    {
        var arr = new JsonArray();
        foreach (var (name, options) in items)
        {
            var o = new JsonObject { ["name"] = name };
            if (options.Count > 0) o["options"] = (JsonNode?)ToNode(options) ?? new JsonObject();
            arr.Add(o);
        }
        return arr;
    }

    private static void AddIfSet(JsonObject o, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) o[key] = value;
    }

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }

    /// <summary>Converts the loosely-typed config object tree (maps/lists/scalars) into JSON nodes.</summary>
    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        IReadOnlyDictionary<string, object?> map => MapToObject(map),
        IEnumerable<object?> list => ListToArray(list),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonObject MapToObject(IReadOnlyDictionary<string, object?> map)
    {
        var o = new JsonObject();
        foreach (var (k, v) in map) o[k] = ToNode(v);
        return o;
    }

    private static JsonArray ListToArray(IEnumerable<object?> list)
    {
        var arr = new JsonArray();
        foreach (var v in list) arr.Add(ToNode(v));
        return arr;
    }
}
