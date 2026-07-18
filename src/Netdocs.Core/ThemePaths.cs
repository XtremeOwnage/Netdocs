namespace Netdocs.Core;

/// <summary>Runtime locations of the bundled Material theme (copied next to the executable).</summary>
public static class ThemePaths
{
    public static string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "theme");
    public static string TemplatesDir => Path.Combine(Root, "templates");
    public static string AssetsDir => Path.Combine(Root, "assets");
}
