using System.Globalization;

namespace Netdocs.Core.Export;

/// <summary>Output formats <c>netdocs export</c> can produce from a single page.</summary>
public enum ExportFormat
{
    Pdf,
    Png,
    Webp,
}

/// <summary>Options for <c>netdocs export &lt;file.md&gt;</c>.</summary>
public sealed class ExportOptions
{
    /// <summary>Absolute path of the markdown file to export.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Formats to write, in the order requested (duplicates removed).</summary>
    public IReadOnlyList<ExportFormat> Formats { get; init; } = [ExportFormat.Pdf];

    /// <summary>Material color scheme (<c>default</c> or <c>slate</c>), or null to use the
    /// site's first configured palette.</summary>
    public string? Scheme { get; init; }

    /// <summary>Output file or directory. Null writes next to the source file.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Viewport width in CSS pixels used for layout and image exports.</summary>
    public int Width { get; init; } = 1000;

    /// <summary>Device pixel ratio for image exports (2 = "retina").</summary>
    public double Scale { get; init; } = 1;

    /// <summary>PDF paper size name (<c>letter</c>, <c>legal</c>, <c>a4</c>, <c>a3</c>).</summary>
    public string Paper { get; init; } = "letter";

    /// <summary>Explicit Chrome/Chromium/Edge executable, overriding auto-detection.</summary>
    public string? BrowserPath { get; init; }

    /// <summary>Path to appsettings.json given with <c>--config</c>.</summary>
    public string? ConfigPath { get; init; }

    public bool Verbose { get; init; }

    /// <summary>Paper sizes in inches (width, height).</summary>
    internal static readonly IReadOnlyDictionary<string, (double Width, double Height)> PaperSizes =
        new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["letter"] = (8.5, 11),
            ["legal"] = (8.5, 14),
            ["tabloid"] = (11, 17),
            ["a3"] = (11.69, 16.54),
            ["a4"] = (8.27, 11.69),
            ["a5"] = (5.83, 8.27),
        };

    public const string Usage = """
        Usage: netdocs export <file.md> [options]

        Renders one markdown file as a standalone page (no navigation, search, header or footer)
        and writes it in each requested format.

        Options:
          --format <list>     Comma-separated formats: pdf, png, webp (default pdf)
          --theme <name>      light or dark (default: the site's first palette, else light)
          -o, --output <path> Output directory, or file path (default: next to the markdown file)
          --width <px>        Page width in CSS pixels (default 1000)
          --scale <n>         Pixel ratio for png/webp, e.g. 2 for high-DPI (default 1)
          --paper <size>      PDF paper size: letter, legal, tabloid, a3, a4, a5 (default letter)
          --browser <path>    Chrome, Chromium or Edge executable (default: auto-detect,
                              or the NETDOCS_BROWSER environment variable)
          -f, --config <path> appsettings.json to take markdown extensions, plugins and
                              palette from (default ./appsettings.json when present)

        Options may also be written with a single dash (-format pdf,png -theme dark).
        """;

    /// <summary>Parses the arguments that follow <c>export</c>. Returns null and sets
    /// <paramref name="error"/> when they are invalid.</summary>
    public static ExportOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        string? source = null, output = null, scheme = null, browser = null, config = null;
        var formats = new List<ExportFormat>();
        var width = 1000;
        var scale = 1.0;
        var paper = "letter";
        var verbose = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-') || arg.Length == 1)
            {
                if (source is not null) { error = $"Unexpected argument '{arg}'. Only one markdown file can be exported at a time."; return null; }
                source = arg;
                continue;
            }

            // Accept --name, -name, and --name=value.
            var name = arg.TrimStart('-').ToLowerInvariant();
            string? inline = null;
            var eq = name.IndexOf('=');
            if (eq >= 0) { inline = arg[(arg.IndexOf('=') + 1)..]; name = name[..eq]; }

            if (name is "v" or "verbose") { verbose = true; continue; }

            string? Value()
            {
                if (inline is not null) return inline;
                return i + 1 < args.Count ? args[++i] : null;
            }

            var value = Value();
            if (value is null) { error = $"Missing value for '{arg}'."; return null; }

            switch (name)
            {
                case "format" or "formats":
                    foreach (var part in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!TryParseFormat(part, out var format)) { error = $"Unknown format '{part}'. Use pdf, png or webp."; return null; }
                        if (!formats.Contains(format)) formats.Add(format);
                    }
                    break;
                case "theme" or "scheme":
                    scheme = SchemeFor(value);
                    if (scheme is null) { error = $"Unknown theme '{value}'. Use light or dark."; return null; }
                    break;
                case "o" or "out" or "output":
                    output = value;
                    break;
                case "width" or "w":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out width) || width < 200 || width > 10000)
                    { error = $"Invalid width '{value}'. Use a pixel width between 200 and 10000."; return null; }
                    break;
                case "scale":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) || scale <= 0 || scale > 4)
                    { error = $"Invalid scale '{value}'. Use a number greater than 0 and at most 4."; return null; }
                    break;
                case "paper":
                    if (!PaperSizes.ContainsKey(value)) { error = $"Unknown paper size '{value}'. Use one of: {string.Join(", ", PaperSizes.Keys)}."; return null; }
                    paper = value.ToLowerInvariant();
                    break;
                case "browser":
                    browser = value;
                    break;
                case "f" or "config":
                    config = value;
                    break;
                default:
                    error = $"Unknown option '{arg}'.";
                    return null;
            }
        }

        if (source is null) { error = "No markdown file given."; return null; }

        return new ExportOptions
        {
            SourcePath = Path.GetFullPath(source),
            Formats = formats.Count > 0 ? formats : [ExportFormat.Pdf],
            Scheme = scheme,
            OutputPath = output is null ? null : Path.GetFullPath(output),
            Width = width,
            Scale = scale,
            Paper = paper,
            BrowserPath = browser,
            ConfigPath = config,
            Verbose = verbose,
        };
    }

    private static bool TryParseFormat(string value, out ExportFormat format)
    {
        switch (value.Trim().TrimStart('.').ToLowerInvariant())
        {
            case "pdf": format = ExportFormat.Pdf; return true;
            case "png": format = ExportFormat.Png; return true;
            case "webp": format = ExportFormat.Webp; return true;
            default: format = default; return false;
        }
    }

    /// <summary>Maps a user-facing theme name to a Material color scheme.</summary>
    internal static string? SchemeFor(string theme) => theme.Trim().ToLowerInvariant() switch
    {
        "dark" or "slate" => "slate",
        "light" or "default" => "default",
        _ => null,
    };

    /// <summary>Where each format is written. A directory output (existing, or ending in a path
    /// separator) gets <c>&lt;name&gt;.&lt;ext&gt;</c> files; a file output keeps its name and
    /// takes each format's extension.</summary>
    public IReadOnlyDictionary<ExportFormat, string> ResolveOutputs()
    {
        var stem = Path.GetFileNameWithoutExtension(SourcePath);
        string dir, baseName;
        if (OutputPath is null)
        {
            dir = Path.GetDirectoryName(SourcePath)!;
            baseName = stem;
        }
        else if (Directory.Exists(OutputPath) || OutputPath.EndsWith(Path.DirectorySeparatorChar) || OutputPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            dir = OutputPath;
            baseName = stem;
        }
        else
        {
            dir = Path.GetDirectoryName(OutputPath)!;
            var ext = Path.GetExtension(OutputPath);
            baseName = TryParseFormat(ext, out _) ? Path.GetFileNameWithoutExtension(OutputPath) : Path.GetFileName(OutputPath);
        }

        return Formats.ToDictionary(f => f, f => Path.Combine(dir, $"{baseName}.{f.ToString().ToLowerInvariant()}"));
    }
}
