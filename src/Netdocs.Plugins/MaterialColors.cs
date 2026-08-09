using SixLabors.ImageSharp;

namespace Netdocs.Plugins;

/// <summary>
/// The Material palette names the theme accepts (<c>theme.palette.primary</c> / <c>accent</c>),
/// mapped to the hex values the stylesheet uses. Social cards read from the same table so a
/// generated card matches the site it belongs to.
/// </summary>
public static class MaterialColors
{
    private static readonly Dictionary<string, string> Primaries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "ef5350",
        ["pink"] = "e91e63",
        ["purple"] = "ab47bc",
        ["deep-purple"] = "7e57c2",
        ["indigo"] = "3f51b5",
        ["blue"] = "2196f3",
        ["light-blue"] = "03a9f4",
        ["cyan"] = "00bcd4",
        ["teal"] = "009688",
        ["green"] = "4caf50",
        ["light-green"] = "7cb342",
        ["lime"] = "c0ca33",
        ["yellow"] = "f9a825",
        ["amber"] = "ffb300",
        ["orange"] = "ff9800",
        ["deep-orange"] = "ff7043",
        ["brown"] = "795548",
        ["grey"] = "42464e",
        ["gray"] = "42464e",
        ["blue-grey"] = "546e7a",
        ["black"] = "1f2129",
        ["white"] = "ffffff",
    };

    private static readonly Dictionary<string, string> Accents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "ff5252",
        ["pink"] = "ff4081",
        ["purple"] = "e040fb",
        ["deep-purple"] = "7c4dff",
        ["indigo"] = "536dfe",
        ["blue"] = "448aff",
        ["light-blue"] = "0091ea",
        ["cyan"] = "18ffff",
        ["teal"] = "64ffda",
        ["green"] = "69f0ae",
        ["light-green"] = "b2ff59",
        ["lime"] = "eeff41",
        ["yellow"] = "ffd740",
        ["amber"] = "ffd740",
        ["orange"] = "ffab40",
        ["deep-orange"] = "ff6e40",
    };

    /// <summary>Background color for a <c>theme.palette.primary</c> name (grey when unknown).</summary>
    public static Color Primary(string? name) =>
        Color.ParseHex(Primaries.GetValueOrDefault(name?.Trim() ?? "", "42464e"));

    /// <summary>Accent color for a <c>theme.palette.accent</c> name (orange when unknown).</summary>
    public static Color Accent(string? name) =>
        Color.ParseHex(Accents.GetValueOrDefault(name?.Trim() ?? "", "ff9800"));

    /// <summary>Looks a name up in either table, or null when it is not a Material palette name.</summary>
    public static Color? ByName(string name)
    {
        var key = name.Trim();
        if (Primaries.TryGetValue(key, out var primary)) return Color.ParseHex(primary);
        if (Accents.TryGetValue(key, out var accent)) return Color.ParseHex(accent);
        return null;
    }
}
