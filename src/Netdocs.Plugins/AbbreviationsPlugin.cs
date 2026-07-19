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

        // Default-on: only the first occurrence of each term per page gets a tooltip. Opt out with
        // `first_instance_only: false` to mark every occurrence (the classic behaviour).
        if (ctx.PluginOptions.TryGetValue("first_instance_only", out var fio) && fio is not null)
            ctx.Config.Abbreviations.FirstInstanceOnly = ToBool(fio, true);

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

    private static bool ToBool(object value, bool fallback) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var r) => r,
        string s => !(s.Equals("no", StringComparison.OrdinalIgnoreCase)
            || s.Equals("off", StringComparison.OrdinalIgnoreCase) || s == "0"),
        _ => fallback,
    };
}
