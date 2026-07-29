using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Netdocs.Core.Templating;

/// <summary>
/// Validates template files for duplicate block definitions, which silently override
/// and can cause subtle bugs. Emits warnings for each duplicate found.
/// </summary>
public sealed class TemplateBlockValidator
{
    private readonly ILogger _logger;

    public TemplateBlockValidator(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Validates a template file for duplicate block definitions.</summary>
    /// <param name="templatePath">Absolute path to the template file.</param>
    /// <param name="templateContent">Content of the template.</param>
    public void Validate(string templatePath, string templateContent)
    {
        var blocks = ExtractBlockNames(templateContent);
        var duplicates = blocks
            .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count == 0)
            return;

        var relPath = Path.GetFileName(templatePath);
        foreach (var blockName in duplicates)
        {
            var count = blocks.Count(b => string.Equals(b, blockName, StringComparison.OrdinalIgnoreCase));
            _logger.LogWarning("Template file '{TemplatePath}' has {Count} definitions of block '{BlockName}' " +
                               "(only the last one will be used). Consolidate them into a single block.",
                               relPath, count, blockName);
        }
    }

    /// <summary>
    /// Extracts all block names from a Scriban template using regex.
    /// Matches `{% block name %}...{% endblock %}` patterns (case-insensitive).
    /// </summary>
    private static List<string> ExtractBlockNames(string templateContent)
    {
        var result = new List<string>();
        // Match {% block <name> %} — capture the block name
        var pattern = @"{%\s*block\s+(\w+)\s*%}";
        var matches = Regex.Matches(templateContent, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Groups[1].Value is { Length: > 0 } blockName)
                result.Add(blockName);
        }

        return result;
    }
}
