using System.Text;
using System.Text.RegularExpressions;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Implements pymdownx.snippets: <c>--8&lt;-- "file"</c> includes (with optional
/// <c>file:section</c> ranges) and <c>auto_append</c> files added to every page. Also
/// searches a conventional <c>&lt;root&gt;/snippets</c> directory by default, and supports an
/// inline parameterized form <c>--8&lt;-- "file" key="value" ...</c> that substitutes
/// <c>${key}</c> placeholders in the included file (values HTML-escaped) and can be used
/// inline (e.g. inside a table cell).
/// </summary>
public sealed partial class SnippetsPlugin : IPlugin, IMarkdownPreprocessor
{
    private readonly List<string> _basePaths = [];
    private readonly List<string> _autoAppend = [];
    private string _projectRoot = "";

    public string Name => "snippets";
    public int Order => 10;

    public void Configure(IPluginContext ctx)
    {
        _projectRoot = ctx.Config.ProjectRoot;

        var basePath = ctx.PluginOptions.TryGetValue("base_path", out var bp) ? bp : null;
        foreach (var p in AsStringList(basePath))
            _basePaths.Add(Path.GetFullPath(Path.Combine(_projectRoot, p)));
        if (_basePaths.Count == 0)
            _basePaths.Add(_projectRoot);

        // Always search a conventional <root>/snippets directory so `--8<-- "name"` resolves
        // there by default even when base_path is not configured for it.
        var defaultSnippets = Path.GetFullPath(Path.Combine(_projectRoot, "snippets"));
        if (!_basePaths.Contains(defaultSnippets))
            _basePaths.Add(defaultSnippets);

        var autoAppend = ctx.PluginOptions.TryGetValue("auto_append", out var aa) ? aa : null;
        foreach (var p in AsStringList(autoAppend))
            _autoAppend.Add(p);
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        var sb = new StringBuilder(Expand(markdown, 0));
        foreach (var append in _autoAppend)
        {
            var resolved = Resolve(append);
            if (resolved is not null && File.Exists(resolved))
            {
                sb.AppendLine();
                sb.AppendLine(Expand(File.ReadAllText(resolved), 0));
            }
        }
        return Task.FromResult(sb.ToString());
    }

    private string Expand(string markdown, int depth)
    {
        if (depth > 10) return markdown;

        // 1. Inline parameterized includes: `--8<-- "file" key="value" ...`. These may appear
        //    mid-line (e.g. inside a markdown table cell); `${key}` placeholders in the included
        //    file are replaced with the (HTML-escaped) argument values and the result is trimmed
        //    to a single inline fragment.
        markdown = ParamIncludeRegex().Replace(markdown, match =>
        {
            var path = match.Groups["spec"].Value.Trim();
            var resolved = Resolve(path);
            if (resolved is null || !File.Exists(resolved))
                return $"<!-- snippet not found: {path} -->";

            var args = ParseArgs(match.Groups["args"].Value);
            var content = File.ReadAllText(resolved);
            foreach (var (key, value) in args)
                content = content.Replace("${" + key + "}", HtmlEscape(value), StringComparison.Ordinal);
            return Expand(content, depth + 1).Trim();
        });

        // 2. Block includes: `--8<-- "file"` (optionally `file:section`) on their own line.
        return IncludeRegex().Replace(markdown, match =>
        {
            var spec = match.Groups["spec"].Value.Trim();
            string? section = null;
            var path = spec;
            var colon = spec.LastIndexOf(':');
            if (colon > 1 && !spec.Substring(colon).Contains('/'))
            {
                path = spec[..colon];
                section = spec[(colon + 1)..];
            }

            var resolved = Resolve(path);
            if (resolved is null || !File.Exists(resolved))
                return $"<!-- snippet not found: {path} -->";

            var content = File.ReadAllText(resolved);
            if (section is not null)
                content = ExtractSection(content, section);
            return Expand(content, depth + 1);
        });
    }

    /// <summary>Parses trailing <c>key="value"</c> pairs from a parameterized include.</summary>
    private static IEnumerable<(string Key, string Value)> ParseArgs(string args)
    {
        foreach (Match m in ArgRegex().Matches(args))
            yield return (m.Groups["k"].Value, m.Groups["v"].Value);
    }

    private static string HtmlEscape(string text) => text
        .Replace("&", "&amp;")
        .Replace("\"", "&quot;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("'", "&apos;");

    private static string ExtractSection(string content, string section)
    {
        var start = new Regex($@"--8<--\s*\[start:{Regex.Escape(section)}\]");
        var end = new Regex($@"--8<--\s*\[end:{Regex.Escape(section)}\]");
        var startMatch = start.Match(content);
        if (!startMatch.Success) return "";
        var from = content.IndexOf('\n', startMatch.Index);
        if (from < 0) return "";
        var endMatch = end.Match(content, from);
        var to = endMatch.Success ? endMatch.Index : content.Length;
        var slice = content[(from + 1)..to];
        return TrimTrailingMarkerLine(slice);
    }

    private static string TrimTrailingMarkerLine(string slice)
    {
        var lines = slice.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.Contains("--8<--")).ToList();
        return string.Join('\n', lines).Trim('\n');
    }

    private string? Resolve(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path)) return path;
        foreach (var basePath in _basePaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(basePath, path));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> AsStringList(object? node) => node switch
    {
        string s => [s],
        IEnumerable<object?> list => list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0),
        _ => []
    };

    [GeneratedRegex(@"^[ \t]*(?:;\s*)?--8<--(?:-)?[ \t]+""(?<spec>[^""]+)""[ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex IncludeRegex();

    // Inline parameterized include: `--8<-- "file" key="value" ...` (at least one argument).
    // Not line-anchored, so it can be used inside prose or table cells.
    [GeneratedRegex(@"--8<--(?:-)?[ \t]+""(?<spec>[^""]+)""(?<args>(?:[ \t]+[A-Za-z_][\w.-]*=""[^""]*"")+)")]
    private static partial Regex ParamIncludeRegex();

    [GeneratedRegex(@"(?<k>[A-Za-z_][\w.-]*)=""(?<v>[^""]*)""")]
    private static partial Regex ArgRegex();
}
