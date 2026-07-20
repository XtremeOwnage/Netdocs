namespace Netdocs.Core.Configuration;

/// <summary>Convenience accessors over the loosely-typed YAML object tree.</summary>
public static class YamlAccess
{
    public static IReadOnlyDictionary<string, object?> AsMap(this object? node) =>
        node as IReadOnlyDictionary<string, object?>
        ?? (node as Dictionary<string, object?>)
        ?? EmptyMap;

    public static IReadOnlyList<object?> AsList(this object? node) =>
        node as IReadOnlyList<object?>
        ?? (node as List<object?>)
        ?? [];

    public static string? AsString(this object? node) => node switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        _ => Convert.ToString(node, System.Globalization.CultureInfo.InvariantCulture)
    };

    public static bool AsBool(this object? node, bool fallback = false) => node switch
    {
        bool b => b,
        string s => s is "true" or "True" or "1",
        long l => l != 0,
        _ => fallback
    };

    public static object? Get(this IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) ? v : null;

    public static readonly IReadOnlyDictionary<string, object?> EmptyMap =
        new Dictionary<string, object?>();
}
