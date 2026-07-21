using Netdocs.Core.Configuration;

namespace Netdocs.Core.Content;

/// <summary>Splits a leading YAML front-matter block (<c>---</c> fenced) from the body.</summary>
public static class FrontMatter
{
    public static (IReadOnlyDictionary<string, object?> Meta, string Body) Split(string markdown)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
            return (YamlAccess.EmptyMap, markdown);

        // Read the opening delimiter line, tracking its exact length (including its newline).
        var (first, afterFirst) = ReadLine(markdown, 0);
        if (first.Trim() != "---")
            return (YamlAccess.EmptyMap, markdown);

        var yaml = new System.Text.StringBuilder();
        var pos = afterFirst;
        var closed = false;
        while (pos < markdown.Length)
        {
            var (line, next) = ReadLine(markdown, pos);
            pos = next;
            if (line.Trim() is "---" or "...") { closed = true; break; }
            yaml.AppendLine(line);
        }
        if (!closed) return (YamlAccess.EmptyMap, markdown);

        var meta = YamlTree.Parse(yaml.ToString()).AsMap();
        // Body is everything after the closing delimiter line (its newline already consumed).
        var body = pos <= markdown.Length ? markdown[pos..] : "";
        return (meta, body);
    }

    /// <summary>Reads a single line starting at <paramref name="start"/>, returning the line content
    /// (without its terminator) and the index of the first character after the line terminator.
    /// Handles <c>\n</c>, <c>\r\n</c> and lone <c>\r</c> line endings.</summary>
    private static (string Line, int Next) ReadLine(string s, int start)
    {
        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\n')
                return (s[start..i], i + 1);
            if (c == '\r')
            {
                var next = i + 1 < s.Length && s[i + 1] == '\n' ? i + 2 : i + 1;
                return (s[start..i], next);
            }
        }
        return (s[start..], s.Length);
    }
}
