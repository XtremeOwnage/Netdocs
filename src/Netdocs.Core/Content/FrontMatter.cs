using Netdocs.Core.Configuration;

namespace Netdocs.Core.Content;

/// <summary>Splits a leading YAML front-matter block (<c>---</c> fenced) from the body.</summary>
public static class FrontMatter
{
    public static (IReadOnlyDictionary<string, object?> Meta, string Body) Split(string markdown)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
            return (YamlAccess.EmptyMap, markdown);

        // Find the closing delimiter line.
        using var reader = new StringReader(markdown);
        var first = reader.ReadLine();
        if (first is null || first.Trim() != "---")
            return (YamlAccess.EmptyMap, markdown);

        var yaml = new System.Text.StringBuilder();
        string? line;
        var closed = false;
        var consumed = first.Length + 1;
        while ((line = reader.ReadLine()) is not null)
        {
            consumed += line.Length + 1;
            if (line.Trim() is "---" or "...") { closed = true; break; }
            yaml.AppendLine(line);
        }
        if (!closed) return (YamlAccess.EmptyMap, markdown);

        var meta = YamlTree.Parse(yaml.ToString()).AsMap();
        var body = consumed <= markdown.Length ? markdown[consumed..] : "";
        return (meta, body);
    }
}
