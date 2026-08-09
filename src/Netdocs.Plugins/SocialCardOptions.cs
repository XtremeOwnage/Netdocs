using Netdocs.Abstractions;
using SixLabors.ImageSharp;

namespace Netdocs.Plugins;

/// <summary>
/// Resolved configuration for the <c>social</c> plugin: the plugin's own switches plus the
/// <c>cards_layout_options</c> block that controls how a card is drawn.
///
/// <para>Everything has a default derived from the theme palette, so an empty
/// <c>{ "name": "social" }</c> keeps the previous behaviour. Values are parsed once at configure
/// time; parsing is deliberately separate from drawing so it can be tested without a font.</para>
/// </summary>
public sealed class SocialCardOptions
{
    /// <summary>Master switch. <c>false</c> skips generation (and suppresses <c>og:image</c>).</summary>
    public bool Cards { get; init; } = true;

    /// <summary>Reuse a card that is already on disk instead of redrawing it.</summary>
    public bool Cache { get; init; } = true;

    /// <summary>Generate cards during <c>serve</c>. Build/production always generates.</summary>
    public bool EnabledOnServe { get; init; } = true;

    /// <summary>Output directory, relative to the site root.</summary>
    public string CardsDir { get; init; } = "assets/social";

    /// <summary>Output image format: <c>png</c> (default), <c>jpeg</c>, or <c>webp</c>.</summary>
    public SocialCardFormat Format { get; init; } = SocialCardFormat.Png;

    /// <summary>Encoder quality (1-100) for the lossy formats. Ignored for png.</summary>
    public int Quality { get; init; } = 90;

    public int Width { get; init; } = 1200;
    public int Height { get; init; } = 630;

    public Color BackgroundColor { get; init; } = Color.ParseHex("42464e");
    public Color TitleColor { get; init; } = Color.WhiteSmoke;
    public Color DescriptionColor { get; init; } = Color.ParseHex("c9ccd1");
    public Color AccentColor { get; init; } = Color.ParseHex("ff9800");

    /// <summary>Width in pixels of the accent bar down the left edge. <c>0</c> hides it.</summary>
    public int AccentWidth { get; init; } = 12;

    /// <summary>Padding between the card edge and its text.</summary>
    public int Padding { get; init; } = 70;

    public float TitleFontSize { get; init; } = 58;
    public float DescriptionFontSize { get; init; } = 30;
    public float SiteNameFontSize { get; init; } = 28;

    /// <summary>Preferred font family name; falls back to the first usable system font.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Explicit font file (ttf/otf) to load, relative to the project root. Wins over
    /// <see cref="FontFamily"/> and removes the dependency on installed system fonts.</summary>
    public string? FontPath { get; init; }

    /// <summary>Full-bleed background image, relative to the project root. Drawn beneath the text
    /// and scaled to cover the card.</summary>
    public string? BackgroundImage { get; init; }

    /// <summary>Logo drawn in the top-right corner, relative to the project root.</summary>
    public string? Logo { get; init; }

    /// <summary>Height (and max width) of the logo in pixels.</summary>
    public int LogoSize { get; init; } = 96;

    /// <summary>Static title override for every card (a page's own front matter still wins).</summary>
    public string? Title { get; init; }

    /// <summary>Static description override for every card.</summary>
    public string? Description { get; init; }

    /// <summary>Cut the description at this many characters.</summary>
    public int DescriptionLength { get; init; } = 180;

    /// <summary>File extension implied by <see cref="Format"/>.</summary>
    public string Extension => Format switch
    {
        SocialCardFormat.Jpeg => ".jpg",
        SocialCardFormat.Webp => ".webp",
        _ => ".png",
    };

    /// <summary>Where the renderer should point <c>og:image</c>, matching what this plugin writes.</summary>
    public Netdocs.Core.SocialCardSettings PathSettings => new(CardsDir, Extension);

    /// <summary>
    /// Parses plugin options over palette-derived defaults. Unknown keys are ignored and
    /// unparseable values fall back to the default, so a typo degrades the card instead of
    /// failing the build.
    /// </summary>
    public static SocialCardOptions Parse(IReadOnlyDictionary<string, object?> options, PaletteConfig? palette)
    {
        var layout = Map(options, "cards_layout_options");

        var background = MaterialColors.Primary(palette?.Primary);
        var accent = MaterialColors.Accent(palette?.Accent);

        return new SocialCardOptions
        {
            Cards = Bool(options, "cards", true),
            Cache = Bool(options, "cache", true),
            EnabledOnServe = Bool(options, "enabled_on_serve", true),
            CardsDir = Text(options, "cards_dir") ?? "assets/social",
            Format = ParseFormat(Text(options, "format")),
            Quality = Clamp(Int(options, "quality", 90), 1, 100),

            Width = Clamp(Int(layout, "width", 1200), 200, 4000),
            Height = Clamp(Int(layout, "height", 630), 200, 4000),

            BackgroundColor = ColorOf(layout, "background_color", background),
            TitleColor = ColorOf(layout, "color", Color.WhiteSmoke),
            DescriptionColor = ColorOf(layout, "description_color", Color.ParseHex("c9ccd1")),
            AccentColor = ColorOf(layout, "accent_color", accent),
            AccentWidth = Clamp(Int(layout, "accent_width", 12), 0, 400),
            Padding = Clamp(Int(layout, "padding", 70), 0, 400),

            TitleFontSize = Clamp(Int(layout, "title_font_size", 58), 8, 400),
            DescriptionFontSize = Clamp(Int(layout, "description_font_size", 30), 8, 400),
            SiteNameFontSize = Clamp(Int(layout, "site_name_font_size", 28), 8, 400),

            FontFamily = Text(layout, "font_family"),
            FontPath = Text(layout, "font_path"),
            BackgroundImage = Text(layout, "background_image"),
            Logo = Text(layout, "logo"),
            LogoSize = Clamp(Int(layout, "logo_size", 96), 8, 1000),

            Title = Text(layout, "title"),
            Description = Text(layout, "description"),
            DescriptionLength = Clamp(Int(layout, "description_length", 180), 10, 2000),
        };
    }

    /// <summary>
    /// Per-page overrides from front matter, mirroring Material's shape:
    /// <code>
    /// social:
    ///   cards_layout_options:
    ///     title: Custom title
    ///     description: Custom description
    /// </code>
    /// Returns (title, description); either may be null when not overridden.
    /// </summary>
    public static (string? Title, string? Description) PageOverrides(IReadOnlyDictionary<string, object?> frontMatter)
    {
        if (frontMatter.GetValueOrDefault("social") is not IDictionary<string, object?> social)
            return (null, null);

        var layout = social.TryGetValue("cards_layout_options", out var l) && l is IDictionary<string, object?> map
            ? map
            : social;

        return (Str(Value(layout, "title")), Str(Value(layout, "description")));
    }

    private static object? Value(IDictionary<string, object?> source, string key) =>
        source.TryGetValue(key, out var v) ? v : null;

    private static SocialCardFormat ParseFormat(string? name) => (name ?? "png").ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => SocialCardFormat.Jpeg,
        "webp" => SocialCardFormat.Webp,
        _ => SocialCardFormat.Png,
    };

    private static IReadOnlyDictionary<string, object?> Map(IReadOnlyDictionary<string, object?> source, string key) =>
        source.GetValueOrDefault(key) is IDictionary<string, object?> map
            ? new Dictionary<string, object?>(map, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    private static bool Bool(IReadOnlyDictionary<string, object?> source, string key, bool fallback) =>
        source.GetValueOrDefault(key) switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => fallback,
        };

    private static int Int(IReadOnlyDictionary<string, object?> source, string key, int fallback) =>
        source.GetValueOrDefault(key) switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => fallback,
        };

    private static string? Text(IReadOnlyDictionary<string, object?> source, string key) =>
        Str(source.GetValueOrDefault(key));

    private static string? Str(object? value) =>
        value is string s && s.Trim().Length > 0 ? s.Trim() : null;

    /// <summary>Accepts anything ImageSharp understands (<c>#rrggbb</c>, <c>rgb(...)</c>, CSS
    /// names) and falls back to the Material palette names used elsewhere in the theme.</summary>
    private static Color ColorOf(IReadOnlyDictionary<string, object?> source, string key, Color fallback)
    {
        var value = Text(source, key);
        if (value is null) return fallback;
        if (Color.TryParse(value, out var parsed)) return parsed;
        return MaterialColors.ByName(value) ?? fallback;
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}

public enum SocialCardFormat { Png, Jpeg, Webp }
