using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Appends abbreviation definition files (<c>*[ABBR]: definition</c>) to every page so the
/// Markdig abbreviations extension renders them as <c>&lt;abbr&gt;</c> tooltips. A purpose-built
/// alternative to relying on snippets <c>auto_append</c>.
/// </summary>
public sealed class AbbreviationsPlugin : IPlugin, IMarkdownPreprocessor
{
    private string _combined = "";

    public string Name => "abbreviations";
    public int Order => 20; // after snippets (10) so includes are already expanded

    public void Configure(IPluginContext ctx)
    {
        var docsDir = ctx.Config.AbsoluteDocsDir;
        var files = new List<string>();

        if (ctx.PluginOptions.TryGetValue("files", out var f) && f is IEnumerable<object?> list)
            files.AddRange(list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0));
        if (files.Count == 0)
            files.Add("_include/abbv.md"); // sensible default

        var builder = new System.Text.StringBuilder();
        foreach (var rel in files)
        {
            var path = Path.IsPathRooted(rel) ? rel : Path.Combine(docsDir, rel);
            if (File.Exists(path))
            {
                builder.AppendLine();
                builder.AppendLine(File.ReadAllText(path));
            }
            else
            {
                ctx.Logger.LogWarning("abbreviations: file not found: {Path}", path);
            }
        }
        _combined = builder.ToString();
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (_combined.Length == 0) return Task.FromResult(markdown);
        return Task.FromResult(markdown + "\n" + _combined);
    }
}
