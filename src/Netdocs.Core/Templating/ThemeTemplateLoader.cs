using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Netdocs.Core.Templating;

/// <summary>Resolves Scriban <c>include</c> targets across custom_dir + theme dirs.</summary>
public sealed class ThemeTemplateLoader(IReadOnlyList<string> searchDirs) : ITemplateLoader
{
    public bool TryResolvePath(string templateName, out string path)
    {
        foreach (var dir in searchDirs)
        {
            var candidate = Path.Combine(dir, templateName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) { path = candidate; return true; }
        }
        path = "";
        return false;
    }

    public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        if (TryResolvePath(templateName, out var path)) return path;
        throw new FileNotFoundException($"Included template '{templateName}' not found.");
    }

    public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        => File.ReadAllText(templatePath);

    public async ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
        => await File.ReadAllTextAsync(templatePath);
}

/// <summary>Helper functions exposed to templates (url shaping, social icons, etc.).</summary>
public static class TemplateFunctions
{
    public static void Register(ScriptObject globals, IDictionary<string, object?>? model = null)
    {
        globals.Import("url", static (string? value) => value ?? "");
        globals.Import("strip_slash", static (string? value) => value?.TrimStart('/') ?? "");
        globals.Import("is_active", static (string? current, string? target) =>
            !string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(target) &&
            string.Equals(current.Trim('/'), target.Trim('/'), StringComparison.OrdinalIgnoreCase));

        var overrides = ExtractIconOverrides(model);
        globals.Import("social_icon", (string? name) => SocialIcon(name ?? "", overrides));
    }

    /// <summary>Reads custom <c>extra.social_icons</c> ({ "icon-name": "&lt;svg path d&gt;" }) so sites can add
    /// or replace brand glyphs without editing the theme.</summary>
    private static IReadOnlyDictionary<string, string> ExtractIconOverrides(IDictionary<string, object?>? model)
    {
        if (model is null || !model.TryGetValue("extra", out var extraObj))
            return EmptyOverrides;

        if (extraObj is not IDictionary<string, object?> extra ||
            !extra.TryGetValue("social_icons", out var iconsObj) ||
            iconsObj is not IDictionary<string, object?> pairs)
            return EmptyOverrides;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
            if (value?.ToString() is { Length: > 0 } path)
                result[key] = path;
        return result;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyOverrides =
        new Dictionary<string, string>();

    private static string SocialIcon(string name, IReadOnlyDictionary<string, string> overrides)
    {
        // Exact override match first (by full icon name), then a substring/brand match.
        if (overrides.TryGetValue(name, out var exact))
            return Svg(exact);
        foreach (var (key, path) in overrides)
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
                return Svg(path);

        var key2 = name.ToLowerInvariant();
        var d =
            key2.Contains("github") ? "M12 2A10 10 0 0 0 2 12c0 4.42 2.87 8.17 6.84 9.5.5.08.66-.23.66-.5v-1.69c-2.77.6-3.36-1.34-3.36-1.34-.46-1.16-1.11-1.47-1.11-1.47-.91-.62.07-.6.07-.6 1 .07 1.53 1.03 1.53 1.03.87 1.52 2.34 1.07 2.91.83.09-.65.35-1.09.63-1.34-2.22-.25-4.55-1.11-4.55-4.92 0-1.11.38-2 1.03-2.71-.1-.25-.45-1.29.1-2.64 0 0 .84-.27 2.75 1.02.79-.22 1.65-.33 2.5-.33.85 0 1.71.11 2.5.33 1.91-1.29 2.75-1.02 2.75-1.02.55 1.35.2 2.39.1 2.64.65.71 1.03 1.6 1.03 2.71 0 3.82-2.34 4.66-4.57 4.91.36.31.69.92.69 1.85V21c0 .27.16.59.67.5C19.14 20.16 22 16.42 22 12A10 10 0 0 0 12 2Z"
          : key2.Contains("discord") ? "M19.27 5.33C17.94 4.71 16.5 4.26 15 4a.09.09 0 0 0-.07.03c-.18.33-.39.76-.53 1.09a16.09 16.09 0 0 0-4.8 0c-.14-.34-.35-.76-.54-1.09-.01-.02-.04-.03-.07-.03-1.5.26-2.93.71-4.27 1.33-.01 0-.02.01-.03.02-2.72 4.07-3.47 8.03-3.1 11.95 0 .02.01.04.03.05 1.8 1.32 3.53 2.12 5.24 2.65.03.01.06 0 .07-.02.4-.55.76-1.13 1.07-1.74.02-.04 0-.08-.04-.09-.57-.22-1.11-.48-1.64-.78-.04-.02-.04-.08-.01-.11.11-.08.22-.17.33-.25.02-.02.05-.02.07-.01 3.44 1.57 7.15 1.57 10.55 0 .02-.01.05-.01.07.01.11.09.22.17.33.26.04.03.04.09-.01.11-.52.31-1.07.56-1.64.78-.04.01-.05.06-.04.09.32.61.68 1.19 1.07 1.74.03.02.06.03.09.02 1.72-.53 3.45-1.33 5.25-2.65.02-.01.03-.03.03-.05.44-4.53-.73-8.46-3.1-11.95-.01-.01-.02-.02-.04-.02M8.52 14.91c-1.03 0-1.89-.95-1.89-2.12s.84-2.12 1.89-2.12c1.06 0 1.9.96 1.89 2.12 0 1.17-.84 2.12-1.89 2.12m6.97 0c-1.03 0-1.89-.95-1.89-2.12s.84-2.12 1.89-2.12c1.06 0 1.9.96 1.89 2.12 0 1.17-.83 2.12-1.89 2.12Z"
          : key2.Contains("reddit") ? "M22 11.5a2.5 2.5 0 0 0-4.11-1.9 12.34 12.34 0 0 0-6.19-1.85l1.05-4.95 3.46.73a1.75 1.75 0 1 0 .18-1.06l-3.87-.82a.53.53 0 0 0-.63.41L10.5 7.75a12.36 12.36 0 0 0-6.4 1.85 2.5 2.5 0 1 0-2.75 4.06 4.6 4.6 0 0 0-.05.63c0 3.72 4.26 6.71 9.5 6.71s9.5-3 9.5-6.71a4.6 4.6 0 0 0-.05-.63A2.5 2.5 0 0 0 22 11.5M7 13a1.5 1.5 0 1 1 3 0 1.5 1.5 0 0 1-3 0m9.34 4.28a6.4 6.4 0 0 1-4.34 1.35 6.4 6.4 0 0 1-4.34-1.35.42.42 0 1 1 .59-.59A5.68 5.68 0 0 0 12 17.79a5.68 5.68 0 0 0 3.75-1.1.42.42 0 1 1 .59.59M15.5 14.5a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Z"
          : key2.Contains("rss") ? "M6.18 15.64a2.18 2.18 0 0 1 2.18 2.18C8.36 19 7.38 20 6.18 20 5 20 4 19 4 17.82a2.18 2.18 0 0 1 2.18-2.18M4 4.44A15.56 15.56 0 0 1 19.56 20h-2.83A12.73 12.73 0 0 0 4 7.27V4.44m0 5.66a9.9 9.9 0 0 1 9.9 9.9h-2.83A7.07 7.07 0 0 0 4 12.93V10.1Z"
          : (key2.Contains("x-twitter") || key2.Contains("twitter")) ? "M18.9 1.15h3.68l-8.04 9.19L24 22.85h-7.41l-5.8-7.58-6.64 7.58H.47l8.6-9.83L0 1.15h7.59l5.24 6.93 6.07-6.93Zm-1.29 19.5h2.04L6.49 3.24H4.3L17.61 20.65Z"
          : key2.Contains("mastodon") ? "M21.58 13.9c-.3 1.54-2.69 3.23-5.43 3.56-1.43.17-2.83.32-4.33.25-2.45-.11-4.39-.58-4.39-.58 0 .24.01.47.04.68.32 2.42 2.4 2.56 4.37 2.63 1.99.07 3.76-.49 3.76-.49l.08 1.8s-1.39.75-3.87.89c-1.37.07-3.07-.04-5.05-.56C2.6 20.85 1.86 16.28 1.75 11.64c-.03-1.38-.01-2.68-.01-3.76 0-4.75 3.11-6.14 3.11-6.14C6.42 1.02 9.12.7 11.93.68h.07c2.81.02 5.51.34 7.08 1.06 0 0 3.11 1.39 3.11 6.14 0 0 .04 3.5-.61 6.02"
          : key2.Contains("linkedin") ? "M19 3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h14m-.5 15.5v-5.3a3.26 3.26 0 0 0-3.26-3.26c-.85 0-1.84.52-2.32 1.3v-1.11h-2.79v8.37h2.79v-4.93c0-.77.62-1.4 1.39-1.4a1.4 1.4 0 0 1 1.4 1.4v4.93h2.79M6.88 8.56a1.68 1.68 0 0 0 1.68-1.68c0-.93-.75-1.69-1.68-1.69a1.69 1.69 0 0 0-1.69 1.69c0 .93.76 1.68 1.69 1.68m1.39 9.94v-8.37H5.5v8.37h2.77Z"
          : key2.Contains("youtube") ? "M10 15l5.19-3L10 9v6m11.56-7.83c.13.47.22 1.1.28 1.9.07.8.1 1.49.1 2.09L22 12c0 2.19-.16 3.8-.44 4.83-.25.9-.83 1.48-1.73 1.73-.47.13-1.33.22-2.65.28-1.3.07-2.49.1-3.59.1L12 19c-4.19 0-6.8-.16-7.83-.44-.9-.25-1.48-.83-1.73-1.73-.13-.47-.22-1.1-.28-1.9-.07-.8-.1-1.49-.1-2.09L2 12c0-2.19.16-3.8.44-4.83.25-.9.83-1.48 1.73-1.73.47-.13 1.33-.22 2.65-.28 1.3-.07 2.49-.1 3.59-.1L12 5c4.19 0 6.8.16 7.83.44.9.25 1.48.83 1.73 1.73Z"
          : key2.Contains("mail") || key2.Contains("email") || key2.Contains("envelope") ? "M20 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2m0 4-8 5-8-5V6l8 5 8-5Z"
          : "M12 2A10 10 0 0 0 2 12a10 10 0 0 0 10 10 10 10 0 0 0 10-10A10 10 0 0 0 12 2m6.9 6h-2.95a15.7 15.7 0 0 0-1.38-3.56A8 8 0 0 1 18.9 8M12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96M4.26 14a7.8 7.8 0 0 1 0-4h3.38a16.6 16.6 0 0 0 0 4H4.26m.82 2h2.95a15.7 15.7 0 0 0 1.38 3.56A8 8 0 0 1 5.08 16m2.95-8H5.08a8 8 0 0 1 4.33-3.56A15.7 15.7 0 0 0 8.03 8M12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96M14.34 14H9.66a14.8 14.8 0 0 1 0-4h4.68a14.8 14.8 0 0 1 0 4m.25 5.56A15.7 15.7 0 0 0 15.97 16h2.95a8 8 0 0 1-4.33 3.56M16.36 14a16.6 16.6 0 0 0 0-4h3.38a7.8 7.8 0 0 1 0 4h-3.38Z";
        return Svg(d);
    }

    private static string Svg(string path) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"{path}\"/></svg>";
}
