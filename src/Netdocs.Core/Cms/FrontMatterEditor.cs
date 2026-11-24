using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Netdocs.Core.Configuration;
using Netdocs.Core.Content;

namespace Netdocs.Core.Cms;

/// <summary>
/// Reads and rewrites a markdown file's YAML front matter one top-level key at a time. Keys the
/// caller does not change keep their exact original text (comments, quoting, list style), and the
/// markdown body is never touched, so a metadata edit produces the smallest possible diff.
/// </summary>
public sealed partial class FrontMatterEditor
{
    private readonly List<Chunk> _chunks = [];
    private readonly string _preamble;
    private readonly string _newline;
    private string _listIndent = "  ";

    private FrontMatterEditor(bool hadFrontMatter, string preamble, string body, string newline)
    {
        HadFrontMatter = hadFrontMatter;
        _preamble = preamble;
        Body = body;
        _newline = newline;
    }

    /// <summary>True when the file started with a <c>---</c> front matter block.</summary>
    public bool HadFrontMatter { get; }

    /// <summary>The markdown after the front matter, exactly as read.</summary>
    public string Body { get; set; }

    /// <summary>Top-level keys in file order.</summary>
    public IEnumerable<string> Keys => _chunks.Where(c => c.Key is not null).Select(c => c.Key!);

    public static FrontMatterEditor Parse(string raw)
    {
        var newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var (meta, body) = FrontMatter.Split(raw);
        _ = meta;
        if (body.Length == raw.Length || !raw.StartsWith("---", StringComparison.Ordinal))
            return new FrontMatterEditor(false, "", raw, newline);

        // The YAML text is every line between the opening delimiter and the closing one.
        var lines = raw[..(raw.Length - body.Length)].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var yamlLines = new List<string>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() is "---" or "...") break;
            yamlLines.Add(lines[i]);
        }
        var yaml = string.Join('\n', yamlLines) + (yamlLines.Count > 0 ? "\n" : "");

        var editor = new FrontMatterEditor(true, "", body, newline);
        editor.ReadChunks(yaml);
        return editor;
    }

    private void ReadChunks(string yaml)
    {
        var lines = SplitLines(yaml);
        Chunk? current = null;
        var pending = new StringBuilder();
        foreach (var line in lines)
        {
            var match = TopLevelKey().Match(line);
            if (match.Success)
            {
                current = new Chunk { Key = Unquote(match.Groups["key"].Value.Trim()) };
                // Comments and blank lines just above a key belong to that key.
                current.Text.Append(pending);
                pending.Clear();
                current.Text.Append(line).Append('\n');
                _chunks.Add(current);
                continue;
            }

            var isListItem = ListItem().Match(line);
            if (isListItem.Success && current is not null)
                _listIndent = isListItem.Groups["indent"].Value;

            var trimmed = line.TrimStart();
            if (current is not null && (line.Length == 0 || line[0] is ' ' or '\t' or '-' || trimmed.StartsWith('#')))
            {
                if (line.Length == 0 || trimmed.StartsWith('#')) pending.Append(line).Append('\n');
                else { current.Text.Append(pending).Append(line).Append('\n'); pending.Clear(); }
                continue;
            }

            // Anything else before the first key (or unrecognised) is kept verbatim.
            _chunks.Add(new Chunk { Key = null, Text = new StringBuilder(pending + line + "\n") });
            pending.Clear();
        }
        if (pending.Length > 0) _chunks.Add(new Chunk { Key = null, Text = pending });
    }

    /// <summary>The parsed value of every key, using the same rules as the build.</summary>
    public IReadOnlyDictionary<string, object?> Values
    {
        get
        {
            var yaml = string.Concat(_chunks.Select(c => c.Text.ToString()));
            return yaml.Trim().Length == 0 ? YamlAccess.EmptyMap : YamlTree.Parse(yaml).AsMap();
        }
    }

    /// <summary>Sets a key, replacing only that key's text. A null value removes the key.</summary>
    public void Set(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new CmsException("Front matter keys cannot be empty.");
        var existing = _chunks.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            if (existing is not null) _chunks.Remove(existing);
            return;
        }

        if (existing is not null && Values.TryGetValue(key, out var old) && DeepEquals(old, value))
            return;

        var text = Emit(existing?.Key ?? key, value, _listIndent);
        if (existing is not null)
        {
            // Keep the comment lines that sat above the key.
            var leading = LeadingComments(existing.Text.ToString());
            existing.Text = new StringBuilder(leading + text);
        }
        else
        {
            _chunks.Add(new Chunk { Key = key, Text = new StringBuilder(text) });
        }
    }

    public void Remove(string key) => Set(key, null);

    /// <summary>Applies a whole metadata map: changed keys are rewritten, new keys appended, and
    /// keys missing from <paramref name="values"/> removed.</summary>
    public void ReplaceAll(IReadOnlyDictionary<string, object?> values)
    {
        foreach (var key in Keys.ToList())
            if (!values.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                Remove(key);
        foreach (var (key, value) in values)
            Set(key, value);
    }

    /// <summary>The full file text.</summary>
    public override string ToString()
    {
        var yaml = string.Concat(_chunks.Select(c => c.Text.ToString()));
        if (yaml.Trim().Length == 0 && !HadFrontMatter) return Body;
        if (yaml.Length > 0 && !yaml.EndsWith('\n')) yaml += "\n";
        var header = "---\n" + _preamble + yaml + "---\n";
        if (_newline != "\n") header = header.Replace("\n", _newline, StringComparison.Ordinal);
        var body = Body;
        if (!HadFrontMatter && body.Length > 0 && !body.StartsWith('\n') && !body.StartsWith("\r\n", StringComparison.Ordinal))
            body = _newline + body;
        return header + body;
    }

    private static string LeadingComments(string text)
    {
        var sb = new StringBuilder();
        foreach (var line in SplitLines(text))
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) { sb.Append(line).Append('\n'); continue; }
            break;
        }
        return sb.ToString();
    }

    private static List<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string Unquote(string key) =>
        key.Length >= 2 && (key[0] == '"' && key[^1] == '"' || key[0] == '\'' && key[^1] == '\'') ? key[1..^1] : key;

    // --- YAML emission -------------------------------------------------------------------

    /// <summary>Emits <c>key: value</c> as block YAML (lists one item per line).</summary>
    internal static string Emit(string key, object? value, string listIndent = "  ")
    {
        var sb = new StringBuilder();
        sb.Append(EmitKey(key)).Append(':');
        EmitValue(sb, value, 0, listIndent);
        return sb.ToString();
    }

    private static void EmitValue(StringBuilder sb, object? value, int indent, string listIndent)
    {
        var pad = new string(' ', indent);
        switch (value)
        {
            case null:
                sb.Append(" null\n");
                break;
            case IDictionary<string, object?> map when map.Count == 0:
                sb.Append(" {}\n");
                break;
            case IDictionary<string, object?> map:
                sb.Append('\n');
                foreach (var (k, v) in map)
                {
                    sb.Append(pad).Append("  ").Append(EmitKey(k)).Append(':');
                    EmitValue(sb, v, indent + 2, listIndent);
                }
                break;
            case IReadOnlyDictionary<string, object?> rmap:
                EmitValue(sb, rmap.ToDictionary(p => p.Key, p => p.Value), indent, listIndent);
                break;
            case string s:
                sb.Append(' ').Append(EmitScalar(s)).Append('\n');
                break;
            case System.Collections.IEnumerable list:
                var items = list.Cast<object?>().ToList();
                if (items.Count == 0) { sb.Append(" []\n"); break; }
                sb.Append('\n');
                foreach (var item in items)
                {
                    sb.Append(pad).Append(listIndent).Append('-');
                    if (item is IDictionary<string, object?> or IReadOnlyDictionary<string, object?>)
                    {
                        var m = item as IDictionary<string, object?> ?? ((IReadOnlyDictionary<string, object?>)item).ToDictionary(p => p.Key, p => p.Value);
                        var first = true;
                        foreach (var (k, v) in m)
                        {
                            if (first) { sb.Append(' '); first = false; }
                            else sb.Append(pad).Append(listIndent).Append("  ");
                            sb.Append(EmitKey(k)).Append(':');
                            EmitValue(sb, v, indent + listIndent.Length + 2, listIndent);
                        }
                        if (first) sb.Append(" {}\n");
                    }
                    else
                    {
                        EmitValue(sb, item, indent + listIndent.Length + 2, listIndent);
                    }
                }
                break;
            default:
                sb.Append(' ').Append(EmitScalar(value)).Append('\n');
                break;
        }
    }

    private static string EmitKey(string key) => PlainKey().IsMatch(key) ? key : Quote(key);

    internal static string EmitScalar(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        long or int or short => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s => NeedsQuotes(s) ? Quote(s) : s,
        _ => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static bool NeedsQuotes(string s)
    {
        if (s.Length == 0) return true;
        if (!PlainScalar().IsMatch(s)) return true;
        if (s.Contains(": ", StringComparison.Ordinal) || s.Contains(" #", StringComparison.Ordinal) || s.EndsWith(':')) return true;
        if (s is "null" or "Null" or "NULL" or "~" or "true" or "True" or "TRUE" or "false" or "False" or "FALSE"
            or "yes" or "Yes" or "no" or "No" or "on" or "On" or "off" or "Off") return true;
        // A string that would read back as a number must stay a string.
        if (long.TryParse(s, out _) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return true;
        return s != s.Trim();
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Compares two front matter values, treating numbers and lists loosely so a value
    /// that round-trips through the browser unchanged is recognised as unchanged.</summary>
    internal static bool DeepEquals(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is string sa && b is string sb) return sa == sb;
        if (IsNumber(a) && IsNumber(b))
            return Convert.ToDouble(a, CultureInfo.InvariantCulture) == Convert.ToDouble(b, CultureInfo.InvariantCulture);
        if (a is bool ba && b is bool bb) return ba == bb;
        if (AsMap(a) is { } ma && AsMap(b) is { } mb)
            return ma.Count == mb.Count && ma.All(p => mb.TryGetValue(p.Key, out var v) && DeepEquals(p.Value, v));
        if (a is System.Collections.IEnumerable la && a is not string && b is System.Collections.IEnumerable lb && b is not string)
        {
            var x = la.Cast<object?>().ToList();
            var y = lb.Cast<object?>().ToList();
            return x.Count == y.Count && x.Zip(y).All(p => DeepEquals(p.First, p.Second));
        }
        return Equals(a, b);
    }

    private static bool IsNumber(object o) => o is long or int or short or double or float or decimal;

    private static Dictionary<string, object?>? AsMap(object o) => o switch
    {
        IDictionary<string, object?> d => new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase),
        IReadOnlyDictionary<string, object?> r => r.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
        _ => null,
    };

    [GeneratedRegex(@"^(?<key>(""[^""]*""|'[^']*'|[^\s#\-'""][^:]*?)):(\s|$)")]
    private static partial Regex TopLevelKey();

    [GeneratedRegex(@"^(?<indent>\s*)-\s")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex PlainKey();

    [GeneratedRegex(@"^[A-Za-z0-9_\p{L}(./][^\n\r""'`{}\[\]&*!|>%@]*$")]
    private static partial Regex PlainScalar();

    private sealed class Chunk
    {
        public string? Key { get; init; }
        public StringBuilder Text { get; set; } = new();
    }
}
