using System.Text;
using System.Text.RegularExpressions;

namespace Netdocs.Core.Cms;

/// <summary>
/// Rewrites link and image targets in markdown source: inline links <c>[t](x)</c>, images
/// <c>![a](x)</c>, reference definitions <c>[id]: x</c> and HTML <c>src</c>/<c>href</c>
/// attributes. Fenced code blocks are left alone. Used when pages or media move or are converted,
/// so references in other pages keep working.
/// </summary>
public static partial class MarkdownLinkRewriter
{
    /// <summary>Rewrites every relative link target. <paramref name="map"/> receives the target
    /// path (no <c>#fragment</c> or <c>?query</c>) and returns the replacement, or null to keep it.</summary>
    public static string Rewrite(string markdown, Func<string, string?> map)
    {
        var sb = new StringBuilder(markdown.Length);
        var inFence = false;
        string? fence = null;
        var lines = markdown.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var fenceMatch = Fence().Match(trimmed);
            if (fenceMatch.Success)
            {
                if (!inFence) { inFence = true; fence = fenceMatch.Value[..3]; }
                else if (trimmed.StartsWith(fence!, StringComparison.Ordinal)) { inFence = false; fence = null; }
                sb.Append(line);
            }
            else if (inFence)
            {
                sb.Append(line);
            }
            else
            {
                var rewritten = InlineLink().Replace(line, m => Replace(m, map));
                rewritten = ReferenceDefinition().Replace(rewritten, m => Replace(m, map));
                rewritten = HtmlAttribute().Replace(rewritten, m => Replace(m, map));
                sb.Append(rewritten);
            }
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Every relative link target in the markdown (fragments and queries removed).</summary>
    public static IEnumerable<string> Targets(string markdown)
    {
        var found = new List<string>();
        Rewrite(markdown, t => { found.Add(t); return null; });
        return found;
    }

    private static string Replace(Match m, Func<string, string?> map)
    {
        var url = m.Groups["url"].Value;
        var bracketed = url.StartsWith('<') && url.EndsWith('>');
        if (bracketed) url = url[1..^1];
        if (url.Length == 0 || Markdown.DocumentRenderer.IsAbsoluteOrExternal(url)) return m.Value;
        var cut = url.IndexOfAny(['#', '?']);
        var path = cut >= 0 ? url[..cut] : url;
        var suffix = cut >= 0 ? url[cut..] : "";
        if (path.Length == 0) return m.Value;
        var decoded = Uri.UnescapeDataString(path);
        var replacement = map(decoded);
        if (replacement is null) return m.Value;
        if (decoded != path || replacement.Contains(' ')) replacement = replacement.Replace(" ", "%20", StringComparison.Ordinal);
        var newUrl = replacement + suffix;
        if (bracketed) newUrl = "<" + newUrl + ">";
        var group = m.Groups["url"];
        return m.Value[..(group.Index - m.Index)] + newUrl + m.Value[(group.Index - m.Index + group.Length)..];
    }

    /// <summary>Resolves <paramref name="target"/> (relative to the docs-relative file
    /// <paramref name="fromFile"/>) to a docs-relative path, or null if it escapes the docs folder.</summary>
    public static string? Resolve(string fromFile, string target)
    {
        var dir = Path.GetDirectoryName(fromFile.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        var combined = dir.Length == 0 ? target : dir + "/" + target;
        var parts = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(segment);
        }
        return string.Join('/', parts);
    }

    /// <summary>The relative link from the docs-relative file <paramref name="fromFile"/> to the
    /// docs-relative path <paramref name="target"/>.</summary>
    public static string Relative(string fromFile, string target)
    {
        var fromDir = (Path.GetDirectoryName(fromFile.Replace('\\', '/'))?.Replace('\\', '/') ?? "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < fromDir.Length && common < to.Length - 1 && fromDir[common] == to[common]) common++;
        var up = Enumerable.Repeat("..", fromDir.Length - common);
        return string.Join('/', up.Concat(to.Skip(common)));
    }

    [GeneratedRegex(@"^(`{3,}|~{3,})")]
    private static partial Regex Fence();

    // ](url) or ](url "title") — the url may be wrapped in <>.
    [GeneratedRegex(@"\]\((?<url><[^>]*>|[^)\s]+)(\s+(""[^""]*""|'[^']*'))?\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"^(?<pre>\s{0,3}\[[^\]]+\]:\s*)(?<url><[^>]*>|\S+)")]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"\b(src|href)\s*=\s*""(?<url>[^""]+)""")]
    private static partial Regex HtmlAttribute();
}
