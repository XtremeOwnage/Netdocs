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

    public async ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
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
    }
}
