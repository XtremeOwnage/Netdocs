using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Netdocs.Core.Configuration;

/// <summary>
/// Parses YAML into a plain object tree (Dictionary/List/scalar) while resolving
/// MkDocs-specific tags: <c>!ENV [VAR, default]</c> and python object/name tags.
/// </summary>
public static class YamlTree
{
    public static object? Parse(string yaml)
    {
        using var reader = new StringReader(yaml);
        var parser = new Parser(reader);
        parser.Consume<StreamStart>();
        if (parser.Accept<StreamEnd>(out _))
            return null;
        parser.Consume<DocumentStart>();
        var result = ParseNode(parser);
        parser.Consume<DocumentEnd>();
        return result;
    }

    private static object? ParseNode(IParser parser)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
            return ScalarValue(scalar);

        if (parser.TryConsume<SequenceStart>(out var seqStart))
        {
            var list = new List<object?>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                list.Add(ParseNode(parser));
            return ResolveTag(seqStart.Tag, list);
        }

        if (parser.TryConsume<MappingStart>(out var mapStart))
        {
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value;
                map[key] = ParseNode(parser);
            }
            return ResolveTag(mapStart.Tag, map);
        }

        // Alias / anchor handling not needed for mkdocs.yml; skip gracefully.
        parser.SkipThisAndNestedEvents();
        return null;
    }

    private static object? ScalarValue(Scalar scalar)
    {
        var tag = scalar.Tag;
        if (!tag.IsEmpty && tag.Value.StartsWith("!!python/", StringComparison.Ordinal))
            return new PythonRef(tag.Value.Replace("tag:yaml.org,2002:", ""), scalar.Value);

        // Only infer types for plain (unquoted) scalars.
        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted)
            return scalar.Value;

        var v = scalar.Value;
        if (v.Length == 0) return "";
        if (v is "null" or "~" or "Null" or "NULL") return null;
        if (v is "true" or "True" or "TRUE") return true;
        if (v is "false" or "False" or "FALSE") return false;
        if (long.TryParse(v, out var l)) return l;
        if (double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return v;
    }

    private static object? ResolveTag(TagName tag, object node)
    {
        if (tag.IsEmpty) return node;
        var t = tag.Value;

        // !ENV [VAR, default] or !ENV [VAR1, VAR2, default]
        if (t is "!ENV" or "!!ENV" && node is List<object?> envArgs && envArgs.Count > 0)
        {
            for (var i = 0; i < envArgs.Count - 1; i++)
            {
                var name = envArgs[i]?.ToString();
                if (name is not null)
                {
                    var val = Environment.GetEnvironmentVariable(name);
                    if (!string.IsNullOrEmpty(val)) return CoerceEnv(val);
                }
            }
            return envArgs[^1];
        }

        if (t.StartsWith("!!python/", StringComparison.Ordinal))
            return new PythonRef(t, node);

        return node;
    }

    private static object? CoerceEnv(string v)
    {
        if (v is "true" or "True") return true;
        if (v is "false" or "False") return false;
        if (long.TryParse(v, out var l)) return l;
        return v;
    }
}

/// <summary>Marker for unresolved python tags (e.g. slugify functions) preserved for plugins.</summary>
public sealed record PythonRef(string Tag, object? Value);
