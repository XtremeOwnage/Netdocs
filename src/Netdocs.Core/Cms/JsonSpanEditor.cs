using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Netdocs.Core.Cms;

/// <summary>
/// Edits one value inside a JSON file in place. Only the bytes of the target value change, so
/// comments, key order and formatting elsewhere in <c>appsettings.json</c> (including the
/// annotated file <c>netdocs new</c> writes) survive a CMS edit.
/// </summary>
public static class JsonSpanEditor
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads the value at <paramref name="path"/> (e.g. <c>["Netdocs", "nav"]</c>), or null when absent.</summary>
    public static JsonNode? Get(string json, params string[] path)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var key in path)
        {
            if (node is not JsonObject obj) return null;
            node = obj.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            if (node is null) return null;
        }
        return node;
    }

    /// <summary>
    /// Returns <paramref name="json"/> with the value at <paramref name="path"/> replaced by
    /// <paramref name="value"/>. Missing objects along the path are created. A null
    /// <paramref name="value"/> removes the property.
    /// </summary>
    public static string Set(string json, JsonNode? value, params string[] path)
    {
        if (path.Length == 0) throw new ArgumentException("A path is required.", nameof(path));
        var bytes = Encoding.UTF8.GetBytes(json);

        // Walk down the path, creating any missing parents one level at a time.
        for (var depth = 1; depth <= path.Length; depth++)
        {
            var prefix = path[..depth];
            var span = FindValue(bytes, prefix);
            if (span is null)
            {
                var parentPath = path[..(depth - 1)];
                var parent = parentPath.Length == 0 ? FindRoot(bytes) : FindValue(bytes, parentPath);
                if (parent is null) throw new CmsException($"Cannot edit '{string.Join('.', path)}' in the config file.");
                if (value is null && depth == path.Length) return json;
                var newValue = depth == path.Length ? value : new JsonObject();
                return Set(Insert(bytes, parent.Value, path[depth - 1], newValue), value, path);
            }
            if (depth == path.Length)
            {
                if (value is null) return Remove(bytes, span.Value);
                var indent = LineIndent(bytes, span.Value.PropertyStart);
                var text = Serialize(value, indent);
                return Splice(bytes, span.Value.ValueStart, span.Value.ValueEnd, text);
            }
        }
        return json;
    }

    private readonly record struct ValueSpan(int PropertyStart, int ValueStart, int ValueEnd, bool IsObject);

    private static ValueSpan? FindRoot(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
        var start = (int)reader.TokenStartIndex;
        reader.Skip();
        return new ValueSpan(start, start, (int)reader.BytesConsumed, true);
    }

    private static ValueSpan? FindValue(byte[] bytes, string[] path)
    {
        var reader = new Utf8JsonReader(bytes, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
        var level = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return null;
            if (reader.TokenType != JsonTokenType.PropertyName) { reader.Skip(); continue; }
            var propStart = (int)reader.TokenStartIndex;
            var name = reader.GetString();
            reader.Read();
            if (!string.Equals(name, path[level], StringComparison.OrdinalIgnoreCase))
            {
                reader.Skip();
                continue;
            }
            if (level == path.Length - 1)
            {
                var valueStart = (int)reader.TokenStartIndex;
                var isObject = reader.TokenType == JsonTokenType.StartObject;
                reader.Skip();
                return new ValueSpan(propStart, valueStart, (int)reader.BytesConsumed, isObject);
            }
            if (reader.TokenType != JsonTokenType.StartObject) return null;
            level++;
        }
        return null;
    }

    private static string Insert(byte[] bytes, ValueSpan parent, string key, JsonNode? value)
    {
        // The parent object's closing brace is the last byte of its span.
        var close = parent.ValueEnd - 1;
        var parentIndent = LineIndent(bytes, parent.PropertyStart);
        var childIndent = parentIndent + "  ";

        // Is the object empty? Look back past whitespace and comments for the last significant byte.
        var reader = new Utf8JsonReader(bytes.AsSpan(parent.ValueStart, parent.ValueEnd - parent.ValueStart), ReaderOptions);
        reader.Read();
        var empty = true;
        var lastEnd = 1;
        while (reader.Read() && reader.CurrentDepth > 0)
        {
            empty = false;
            if (reader.TokenType == JsonTokenType.PropertyName) { reader.Read(); reader.Skip(); lastEnd = (int)reader.BytesConsumed; }
        }

        var sb = new StringBuilder();
        var insertAt = empty ? close : parent.ValueStart + lastEnd;
        if (!empty) sb.Append(',');
        sb.Append('\n').Append(childIndent).Append(JsonSerializer.Serialize(key)).Append(": ")
          .Append(Serialize(value, childIndent));
        if (empty) sb.Append('\n').Append(parentIndent);
        return Splice(bytes, insertAt, insertAt, sb.ToString());
    }

    private static string Remove(byte[] bytes, ValueSpan span)
    {
        var start = span.PropertyStart;
        var end = span.ValueEnd;
        // Swallow one trailing comma, or else a leading one.
        var after = end;
        while (after < bytes.Length && bytes[after] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') after++;
        if (after < bytes.Length && bytes[after] == (byte)',')
        {
            end = after + 1;
        }
        else
        {
            var before = start - 1;
            while (before >= 0 && bytes[before] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') before--;
            if (before >= 0 && bytes[before] == (byte)',') start = before;
        }
        // Remove the now-empty line the property sat on.
        while (start > 0 && bytes[start - 1] is (byte)' ' or (byte)'\t') start--;
        if (start > 0 && bytes[start - 1] == (byte)'\n' && bytes[start] != (byte)',') start--;
        return Splice(bytes, start, end, "");
    }

    private static string Splice(byte[] bytes, int start, int end, string replacement)
    {
        var sb = new StringBuilder();
        sb.Append(Encoding.UTF8.GetString(bytes, 0, start));
        sb.Append(replacement);
        sb.Append(Encoding.UTF8.GetString(bytes, end, bytes.Length - end));
        return sb.ToString();
    }

    private static string LineIndent(byte[] bytes, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && bytes[lineStart - 1] != (byte)'\n') lineStart--;
        var indent = new StringBuilder();
        for (var i = lineStart; i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t'; i++)
            indent.Append((char)bytes[i]);
        return indent.ToString();
    }

    /// <summary>Serializes with two-space indentation, re-indented to sit at <paramref name="indent"/>.
    /// Small objects of plain values stay on one line (<c>{ "title": "Home", "path": "index.md" }</c>),
    /// the way hand-written <c>appsettings.json</c> nav entries usually look.</summary>
    internal static string Serialize(JsonNode? value, string indent)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent);
        return sb.ToString();
    }

    private const int InlineLimit = 100;

    private static void Write(StringBuilder sb, JsonNode? node, string indent)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count == 0:
                sb.Append("{}");
                break;
            case JsonObject obj when obj.All(p => p.Value is JsonValue or null) && InlineObject(obj) is { Length: <= InlineLimit } inline:
                sb.Append(inline);
                break;
            case JsonObject obj:
                {
                    sb.Append("{\n");
                    var i = 0;
                    foreach (var (key, value) in obj)
                    {
                        sb.Append(indent).Append("  ").Append(JsonSerializer.Serialize(key, WriteOptions)).Append(": ");
                        Write(sb, value, indent + "  ");
                        sb.Append(++i < obj.Count ? ",\n" : "\n");
                    }
                    sb.Append(indent).Append('}');
                    break;
                }
            case JsonArray arr when arr.Count == 0:
                sb.Append("[]");
                break;
            case JsonArray arr:
                {
                    sb.Append("[\n");
                    for (var i = 0; i < arr.Count; i++)
                    {
                        sb.Append(indent).Append("  ");
                        Write(sb, arr[i], indent + "  ");
                        sb.Append(i + 1 < arr.Count ? ",\n" : "\n");
                    }
                    sb.Append(indent).Append(']');
                    break;
                }
            default:
                sb.Append(node is null ? "null" : node.ToJsonString(WriteOptions));
                break;
        }
    }

    private static string InlineObject(JsonObject obj) =>
        "{ " + string.Join(", ", obj.Select(p => JsonSerializer.Serialize(p.Key, WriteOptions) + ": " + (p.Value is null ? "null" : p.Value.ToJsonString(WriteOptions)))) + " }";
}
