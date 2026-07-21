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

/// <summary>Helper functions exposed to templates (url shaping, etc.).</summary>
public static class TemplateFunctions
{
    public static void Register(ScriptObject globals)
    {
        globals.Import("url", static (string? value) => value ?? "");
        globals.Import("strip_slash", static (string? value) => value?.TrimStart('/') ?? "");
        globals.Import("is_active", static (string? current, string? target) =>
            !string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(target) &&
            string.Equals(current.Trim('/'), target.Trim('/'), StringComparison.OrdinalIgnoreCase));
        globals.Import("social_icon", static (string? name) => SocialIcon(name ?? ""));
    }

    private static string SocialIcon(string name)
    {
        var key = name.ToLowerInvariant();
        var path =
            key.Contains("github") ? "M12 2A10 10 0 0 0 2 12c0 4.42 2.87 8.17 6.84 9.5.5.08.66-.23.66-.5v-1.69c-2.77.6-3.36-1.34-3.36-1.34-.46-1.16-1.11-1.47-1.11-1.47-.91-.62.07-.6.07-.6 1 .07 1.53 1.03 1.53 1.03.87 1.52 2.34 1.07 2.91.83.09-.65.35-1.09.63-1.34-2.22-.25-4.55-1.11-4.55-4.92 0-1.11.38-2 1.03-2.71-.1-.25-.45-1.29.1-2.64 0 0 .84-.27 2.75 1.02.79-.22 1.65-.33 2.5-.33.85 0 1.71.11 2.5.33 1.91-1.29 2.75-1.02 2.75-1.02.55 1.35.2 2.39.1 2.64.65.71 1.03 1.6 1.03 2.71 0 3.82-2.34 4.66-4.57 4.91.36.31.69.92.69 1.85V21c0 .27.16.59.67.5C19.14 20.16 22 16.42 22 12A10 10 0 0 0 12 2Z"
          : key.Contains("discord") ? "M19.27 5.33C17.94 4.71 16.5 4.26 15 4a.09.09 0 0 0-.07.03c-.18.33-.39.76-.53 1.09a16.09 16.09 0 0 0-4.8 0c-.14-.34-.35-.76-.54-1.09-.01-.02-.04-.03-.07-.03-1.5.26-2.93.71-4.27 1.33-.01 0-.02.01-.03.02-2.72 4.07-3.47 8.03-3.1 11.95 0 .02.01.04.03.05 1.8 1.32 3.53 2.12 5.24 2.65.03.01.06 0 .07-.02.4-.55.76-1.13 1.07-1.74.02-.04 0-.08-.04-.09-.57-.22-1.11-.48-1.64-.78-.04-.02-.04-.08-.01-.11.11-.08.22-.17.33-.25.02-.02.05-.02.07-.01 3.44 1.57 7.15 1.57 10.55 0 .02-.01.05-.01.07.01.11.09.22.17.33.26.04.03.04.09-.01.11-.52.31-1.07.56-1.64.78-.04.01-.05.06-.04.09.32.61.68 1.19 1.07 1.74.03.02.06.03.09.02 1.72-.53 3.45-1.33 5.25-2.65.02-.01.03-.03.03-.05.44-4.53-.73-8.46-3.1-11.95-.01-.01-.02-.02-.04-.02M8.52 14.91c-1.03 0-1.89-.95-1.89-2.12s.84-2.12 1.89-2.12c1.06 0 1.9.96 1.89 2.12 0 1.17-.84 2.12-1.89 2.12m6.97 0c-1.03 0-1.89-.95-1.89-2.12s.84-2.12 1.89-2.12c1.06 0 1.9.96 1.89 2.12 0 1.17-.83 2.12-1.89 2.12Z"
          : key.Contains("reddit") ? "M22 11.5a2.5 2.5 0 0 0-4.11-1.9 12.34 12.34 0 0 0-6.19-1.85l1.05-4.95 3.46.73a1.75 1.75 0 1 0 .18-1.06l-3.87-.82a.53.53 0 0 0-.63.41L10.5 7.75a12.36 12.36 0 0 0-6.4 1.85 2.5 2.5 0 1 0-2.75 4.06 4.6 4.6 0 0 0-.05.63c0 3.72 4.26 6.71 9.5 6.71s9.5-3 9.5-6.71a4.6 4.6 0 0 0-.05-.63A2.5 2.5 0 0 0 22 11.5M7 13a1.5 1.5 0 1 1 3 0 1.5 1.5 0 0 1-3 0m9.34 4.28a6.4 6.4 0 0 1-4.34 1.35 6.4 6.4 0 0 1-4.34-1.35.42.42 0 1 1 .59-.59A5.68 5.68 0 0 0 12 17.79a5.68 5.68 0 0 0 3.75-1.1.42.42 0 1 1 .59.59M15.5 14.5a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Z"
          : key.Contains("rss") ? "M6.18 15.64a2.18 2.18 0 0 1 2.18 2.18C8.36 19 7.38 20 6.18 20 5 20 4 19 4 17.82a2.18 2.18 0 0 1 2.18-2.18M4 4.44A15.56 15.56 0 0 1 19.56 20h-2.83A12.73 12.73 0 0 0 4 7.27V4.44m0 5.66a9.9 9.9 0 0 1 9.9 9.9h-2.83A7.07 7.07 0 0 0 4 12.93V10.1Z"
          : "M12 2A10 10 0 0 0 2 12a10 10 0 0 0 10 10 10 10 0 0 0 10-10A10 10 0 0 0 12 2m6.9 6h-2.95a15.7 15.7 0 0 0-1.38-3.56A8 8 0 0 1 18.9 8M12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96M4.26 14a7.8 7.8 0 0 1 0-4h3.38a16.6 16.6 0 0 0 0 4H4.26m.82 2h2.95a15.7 15.7 0 0 0 1.38 3.56A8 8 0 0 1 5.08 16m2.95-8H5.08a8 8 0 0 1 4.33-3.56A15.7 15.7 0 0 0 8.03 8M12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96M14.34 14H9.66a14.8 14.8 0 0 1 0-4h4.68a14.8 14.8 0 0 1 0 4m.25 5.56A15.7 15.7 0 0 0 15.97 16h2.95a8 8 0 0 1-4.33 3.56M16.36 14a16.6 16.6 0 0 0 0-4h3.38a7.8 7.8 0 0 1 0 4h-3.38Z";
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"{path}\"/></svg>";
    }
}
