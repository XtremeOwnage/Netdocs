using System.Text.Json;

namespace Netdocs.Core.Configuration;

/// <summary>
/// Converts a <see cref="JsonElement"/> into the same loosely-typed object tree that
/// <see cref="YamlTree"/> produces (Dictionary/List/scalar), so plugins consume options
/// identically regardless of config source.
/// </summary>
public static class JsonTree
{
    public static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToMap(element),
        JsonValueKind.Array => ToList(element),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.ToString(),
    };

    public static Dictionary<string, object?> ToMap(JsonElement element)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var prop in element.EnumerateObject())
                map[prop.Name] = ToObject(prop.Value);
        return map;
    }

    private static List<object?> ToList(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
            list.Add(ToObject(item));
        return list;
    }
}
