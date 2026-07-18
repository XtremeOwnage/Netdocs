using System.Text;
using System.Text.RegularExpressions;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Implements pymdownx.snippets: <c>--8&lt;-- "file"</c> includes (with optional
/// <c>file:section</c> ranges) and <c>auto_append</c> files added to every page.
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

    [GeneratedRegex(@"^[ \t]*(?:;\s*)?--8<--(?:-)?[ \t]+""(?<spec>[^""]+)""[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex IncludeRegex();
}
