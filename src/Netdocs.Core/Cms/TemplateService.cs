using System.Text.Json;
using System.Text.Json.Nodes;

namespace Netdocs.Core.Cms;

/// <summary>A theme template, and whether the site overrides it.</summary>
public sealed record TemplateInfo(string Name, bool Overridden, bool InTheme);

/// <summary>
/// Theme customisation. Templates are customised the Material way: a copy goes into the site's
/// <c>customDir</c> (created as <c>overrides</c> and written to <c>appsettings.json</c> on first
/// use), which the build searches before the bundled theme. Reverting deletes the copy.
/// Theme and site settings are edited key by key in <c>appsettings.json</c>.
/// </summary>
public sealed class TemplateService(CmsWorkspace workspace)
{
    public const string DefaultCustomDir = "overrides";

    /// <summary>Theme features Netdocs supports, for the settings screen.</summary>
    public static readonly string[] KnownFeatures =
    [
        "announce.dismiss", "content.action.edit", "content.action.view", "content.code.annotate", "content.code.copy",
        "content.code.select", "content.footnote.tooltips", "content.tabs.link", "content.tooltips", "header.autohide",
        "navigation.expand", "navigation.footer", "navigation.indexes", "navigation.instant", "navigation.instant.prefetch",
        "navigation.instant.progress", "navigation.path", "navigation.prune", "navigation.sections", "navigation.tabs",
        "navigation.tabs.sticky", "navigation.top", "navigation.tracking", "search.highlight", "search.share",
        "search.suggest", "toc.follow", "toc.integrate",
    ];

    public static readonly string[] Colors =
    [
        "red", "pink", "purple", "deep-purple", "indigo", "blue", "light-blue", "cyan", "teal", "green", "light-green",
        "lime", "yellow", "amber", "orange", "deep-orange", "brown", "grey", "blue-grey", "black", "white", "custom",
    ];

    /// <summary>Top-level site keys the settings form edits.</summary>
    public static readonly string[] SiteKeys =
        ["siteName", "siteUrl", "siteDescription", "siteAuthor", "copyright", "repoUrl", "repoName", "editUri"];

    public string? CustomDir => string.IsNullOrWhiteSpace(workspace.Config.Theme.CustomDir)
        ? null
        : workspace.ResolveProjectPath(workspace.Config.Theme.CustomDir);

    public IReadOnlyList<TemplateInfo> List()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var theme = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(ThemePaths.TemplatesDir))
            foreach (var f in Directory.EnumerateFiles(ThemePaths.TemplatesDir, "*.html", SearchOption.AllDirectories))
            {
                var n = Path.GetRelativePath(ThemePaths.TemplatesDir, f).Replace('\\', '/');
                names.Add(n);
                theme.Add(n);
            }
        var overridden = new HashSet<string>(StringComparer.Ordinal);
        if (CustomDir is { } dir && Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories))
            {
                var n = Path.GetRelativePath(dir, f).Replace('\\', '/');
                names.Add(n);
                overridden.Add(n);
            }
        return [.. names.Select(n => new TemplateInfo(n, overridden.Contains(n), theme.Contains(n)))];
    }

    /// <summary>The template's current text (the override when there is one) and the theme original.</summary>
    public (string Text, string? Original, bool Overridden) Read(string name)
    {
        var original = ThemeFile(name);
        var overrideFile = OverrideFile(name, create: false);
        var hasOverride = overrideFile is not null && File.Exists(overrideFile);
        var originalText = original is not null && File.Exists(original) ? File.ReadAllText(original) : null;
        if (!hasOverride && originalText is null) throw new CmsNotFoundException($"Template '{name}' was not found.");
        return (hasOverride ? File.ReadAllText(overrideFile!) : originalText!, originalText, hasOverride);
    }

    /// <summary>Writes an override, setting up <c>customDir</c> first when the site has none.</summary>
    public void Save(string name, string text)
    {
        ValidateName(name);
        // Refuse templates that do not parse, rather than breaking every page.
        var parsed = Scriban.Template.Parse(text, name);
        if (parsed.HasErrors)
            throw new CmsException("The template has errors: " + string.Join("; ", parsed.Messages.Select(m => m.ToString())));
        var file = OverrideFile(name, create: true)!;
        CmsWorkspace.WriteText(file, text);
    }

    /// <summary>Deletes an override so the theme's own template is used again.</summary>
    public void Revert(string name)
    {
        ValidateName(name);
        var file = OverrideFile(name, create: false);
        if (file is not null && File.Exists(file)) File.Delete(file);
    }

    public JsonObject ReadSettings()
    {
        var json = File.ReadAllText(workspace.ConfigPath);
        var site = JsonSpanEditor.Get(json, "Netdocs") as JsonObject ?? [];
        var result = new JsonObject();
        foreach (var key in SiteKeys)
            result[key] = site.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.DeepClone();
        result["theme"] = site.FirstOrDefault(p => p.Key.Equals("theme", StringComparison.OrdinalIgnoreCase)).Value?.DeepClone() ?? new JsonObject();
        return result;
    }

    /// <summary>Applies changed site keys and theme keys. Each key is replaced on its own so the
    /// rest of the file keeps its comments and layout.</summary>
    public void SaveSettings(JsonObject settings)
    {
        var json = File.ReadAllText(workspace.ConfigPath);
        var current = ReadSettings();
        foreach (var key in SiteKeys)
        {
            if (!settings.ContainsKey(key)) continue;
            var value = settings[key];
            if (value is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length == 0) value = null;
            if (JsonNode.DeepEquals(value, current[key])) continue;
            json = JsonSpanEditor.Set(json, value?.DeepClone(), "Netdocs", key);
        }
        if (settings["theme"] is JsonObject theme && current["theme"] is JsonObject currentTheme)
        {
            foreach (var (key, value) in theme)
            {
                var existing = currentTheme.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (JsonNode.DeepEquals(value, existing.Value)) continue;
                var clean = value is JsonValue tv && tv.TryGetValue<string>(out var ts) && ts.Trim().Length == 0 ? null : value?.DeepClone();
                json = JsonSpanEditor.Set(json, clean, "Netdocs", "theme", existing.Key ?? key);
            }
            foreach (var (key, _) in currentTheme)
                if (!theme.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    json = JsonSpanEditor.Set(json, null, "Netdocs", "theme", key);
        }
        Validate(json);
        CmsWorkspace.WriteText(workspace.ConfigPath, json);
        workspace.Reload();
    }

    /// <summary>The raw <c>appsettings.json</c> text.</summary>
    public string ReadConfigFile() => File.ReadAllText(workspace.ConfigPath);

    /// <summary>Replaces <c>appsettings.json</c>, after checking it still loads.</summary>
    public void SaveConfigFile(string text)
    {
        Validate(text);
        CmsWorkspace.WriteText(workspace.ConfigPath, text);
        workspace.Reload();
    }

    private void Validate(string json)
    {
        var temp = Path.Combine(Path.GetDirectoryName(workspace.ConfigPath)!, $".appsettings.cms-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(temp, json);
            Configuration.JsonConfigLoader.Load(temp);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new CmsException("The settings are not valid: " + ex.Message);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private static string? ThemeFile(string name)
    {
        ValidateName(name);
        var full = Path.GetFullPath(Path.Combine(ThemePaths.TemplatesDir, name));
        return CmsWorkspace.IsUnder(full, ThemePaths.TemplatesDir) ? full : null;
    }

    private string? OverrideFile(string name, bool create)
    {
        var dir = CustomDir;
        if (dir is null)
        {
            if (!create) return null;
            var json = File.ReadAllText(workspace.ConfigPath);
            json = JsonSpanEditor.Set(json, JsonValue.Create(DefaultCustomDir), "Netdocs", "theme", "customDir");
            CmsWorkspace.WriteText(workspace.ConfigPath, json);
            workspace.Reload();
            dir = CustomDir!;
        }
        var full = Path.GetFullPath(Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!CmsWorkspace.IsUnder(full, dir)) throw new CmsException("Invalid template name.");
        return full;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || name.Contains("..", StringComparison.Ordinal))
            throw new CmsException("Template names must be relative .html paths, such as 'partials/footer.html'.");
    }
}
