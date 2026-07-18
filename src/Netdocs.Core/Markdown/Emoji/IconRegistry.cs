using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace Netdocs.Core.Markdown.Emoji;

/// <summary>
/// Resolves mkdocs-material icon shortcodes (<c>:material-*:</c>, <c>:octicons-*:</c>,
/// <c>:fontawesome-*:</c>) to inline SVG. The icon set (Material Design Icons, Octicons,
/// FontAwesome Free) is bundled as a gzipped JSON embedded resource and decompressed
/// once on first use.
/// </summary>
public static class IconRegistry
{
    public sealed record Icon(string Path, string ViewBox);

    private static readonly Lazy<IReadOnlyDictionary<string, Icon>> Icons = new(Load);

    /// <summary>Number of icons available (for diagnostics/tests).</summary>
    public static int Count => Icons.Value.Count;

    /// <summary>Look up an icon by shortcode name (without the surrounding colons),
    /// e.g. <c>material-application</c> or <c>octicons-arrow-right-24</c>.</summary>
    public static Icon? Get(string name) =>
        Icons.Value.TryGetValue(name, out var icon) ? icon : null;

    /// <summary>True when the name looks like an icon shortcode we might resolve.</summary>
    public static bool IsIconName(string name) =>
        name.StartsWith("material-", StringComparison.Ordinal)
        || name.StartsWith("octicons-", StringComparison.Ordinal)
        || name.StartsWith("fontawesome-", StringComparison.Ordinal)
        || name.StartsWith("simple-", StringComparison.Ordinal);

    /// <summary>Render an icon as an inline SVG string, or null when unknown.</summary>
    public static string? RenderSvg(string name)
    {
        var icon = Get(name);
        if (icon is null) return null;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{icon.ViewBox}\"><path d=\"{icon.Path}\"></path></svg>";
    }

    private static IReadOnlyDictionary<string, Icon> Load()
    {
        var assembly = typeof(IconRegistry).Assembly;
        using var stream = assembly.GetManifestResourceStream("icons/icons.json.gz");
        if (stream is null)
            return new Dictionary<string, Icon>();

        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);

        var map = new Dictionary<string, Icon>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var p = prop.Value.GetProperty("p").GetString() ?? "";
            var v = prop.Value.GetProperty("v").GetString() ?? "0 0 24 24";
            map[prop.Name] = new Icon(p, v);
        }
        return map;
    }
}
