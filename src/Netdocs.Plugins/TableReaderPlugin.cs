using System.Text;
using System.Text.RegularExpressions;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Implements mkdocs-table-reader: expands <c>{{ read_csv("file.csv") }}</c> (and
/// <c>read_table</c>) directives into a Markdown pipe table. Paths are resolved relative
/// to the page's own directory first (matching mkdocs-table-reader), then the docs dir,
/// then the project root. A CSV uses <c>,</c>; a table file uses the delimiter given as a
/// second argument (default TAB).
/// </summary>
public sealed partial class TableReaderPlugin : IPlugin, IMarkdownPreprocessor
{
    private string _docsDir = "";
    private string _projectRoot = "";

    public string Name => "table-reader";
    public int Order => 20;

    public void Configure(IPluginContext ctx)
    {
        _projectRoot = ctx.Config.ProjectRoot;
        _docsDir = ctx.Config.AbsoluteDocsDir;
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (!markdown.Contains("read_csv", StringComparison.Ordinal) &&
            !markdown.Contains("read_table", StringComparison.Ordinal))
            return Task.FromResult(markdown);

        var result = DirectiveRegex().Replace(markdown, match =>
        {
            var fn = match.Groups["fn"].Value;
            var path = match.Groups["path"].Value;
            var delimiter = match.Groups["delim"].Success && match.Groups["delim"].Value.Length > 0
                ? Unescape(match.Groups["delim"].Value)
                : (fn == "read_csv" ? "," : "\t");

            var resolved = Resolve(path, page);
            if (resolved is null)
                return $"<!-- table-reader: file not found: {path} -->";

            try
            {
                return RenderTable(File.ReadAllText(resolved), delimiter);
            }
            catch (Exception ex)
            {
                return $"<!-- table-reader: {fn}('{path}') failed: {ex.Message} -->";
            }
        });

        return Task.FromResult(result);
    }

    private string? Resolve(string path, Page page)
    {
        if (Path.IsPathRooted(path) && File.Exists(path)) return path;

        // mkdocs-table-reader resolves relative to the page's own directory first, so a post
        // can reference a sibling CSV (e.g. "assets/foo.csv") without knowing the docs root.
        var pageDir = string.IsNullOrEmpty(page.SourcePath) ? null : Path.GetDirectoryName(page.SourcePath);

        var roots = pageDir is null
            ? new[] { _docsDir, _projectRoot }
            : new[] { pageDir, _docsDir, _projectRoot };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Renders delimited text as a Markdown pipe table (first row = header).</summary>
    private static string RenderTable(string content, string delimiter)
    {
        var rows = ParseDelimited(content, delimiter);
        if (rows.Count == 0) return "";

        var columns = rows.Max(r => r.Count);
        var sb = new StringBuilder();
        sb.Append('\n');

        WriteRow(sb, rows[0], columns);
        sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", columns))).Append(" |\n");
        for (var i = 1; i < rows.Count; i++)
            WriteRow(sb, rows[i], columns);

        sb.Append('\n');
        return sb.ToString();
    }

    private static void WriteRow(StringBuilder sb, List<string> cells, int columns)
    {
        sb.Append("| ");
        for (var c = 0; c < columns; c++)
        {
            var value = c < cells.Count ? cells[c].Replace("|", "\\|").Replace("\n", " ").Trim() : "";
            sb.Append(value).Append(" | ");
        }
        sb.Append('\n');
    }

    /// <summary>Parses delimited text, honoring double-quoted fields (RFC 4180-ish).</summary>
    private static List<List<string>> ParseDelimited(string content, string delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var text = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var d = delimiter.Length > 0 ? delimiter[0] : ',';

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == d) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                if (row.Any(c => c.Length > 0) || row.Count > 1) rows.Add(row);
                row = [];
            }
            else field.Append(ch);
        }
        row.Add(field.ToString());
        if (row.Any(c => c.Length > 0)) rows.Add(row);
        return rows;
    }

    private static string Unescape(string s) => s.Replace("\\t", "\t").Replace("\\n", "\n");

    [GeneratedRegex("""\{\{\s*(?<fn>read_csv|read_table)\s*\(\s*["'](?<path>[^"']+)["'](?:\s*,\s*["'](?<delim>[^"']*)["'])?\s*\)\s*\}\}""")]
    private static partial Regex DirectiveRegex();
}
