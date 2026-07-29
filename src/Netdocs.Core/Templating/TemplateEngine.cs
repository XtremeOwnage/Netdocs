using Scriban;
using Scriban.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Netdocs.Core.Templating;

/// <summary>
/// Loads and renders Scriban theme templates, with custom_dir overrides taking
/// precedence over the built-in theme directory.
/// </summary>
public sealed class TemplateEngine
{
    private readonly IReadOnlyList<string> _searchDirs;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Template> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThemeTemplateLoader _loader;
    private readonly TemplateBlockValidator _blockValidator;

    public TemplateEngine(IEnumerable<string> searchDirsHighestPriorityFirst, ILogger? logger = null)
    {
        _searchDirs = searchDirsHighestPriorityFirst.Where(Directory.Exists).ToList();
        _loader = new ThemeTemplateLoader(_searchDirs);
        _blockValidator = new TemplateBlockValidator(logger ?? NullLogger.Instance);
    }

    public bool TryResolve(string templateName, out string path) => _loader.TryResolvePath(templateName, out path);

    public string Render(string templateName, IDictionary<string, object?> model)
    {
        var template = GetTemplate(templateName);
        var context = new TemplateContext
        {
            TemplateLoader = _loader,
            EnableRelaxedMemberAccess = true,
        };
        var globals = new ScriptObject();
        foreach (var (key, value) in model)
            globals[key] = value;
        TemplateFunctions.Register(globals, model);
        context.PushGlobal(globals);
        return template.Render(context);
    }

    private Template GetTemplate(string name)
    {
        return _cache.GetOrAdd(name, key =>
        {
            if (!_loader.TryResolvePath(key, out var path))
                throw new FileNotFoundException($"Template '{key}' not found in: {string.Join(", ", _searchDirs)}");
            var content = File.ReadAllText(path);
            _blockValidator.Validate(path, content);
            var template = Template.Parse(content, path);
            if (template.HasErrors)
                throw new InvalidOperationException($"Template '{key}' has errors:\n{string.Join('\n', template.Messages)}");
            return template;
        });
    }
}
