using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Netdocs.Plugins;

/// <summary>Generates Material-style social (Open Graph) cards for each page.</summary>
public sealed class SocialPlugin : IPlugin, IBuildHook
{
    private const int Width = 1200;
    private const int Height = 630;

    private ILogger _log = null!;
    private bool _cache = true;
    private Color _background = Color.ParseHex("42464e");
    private Color _accent = Color.ParseHex("ff9800");

    public string Name => "social";

    public void Configure(IPluginContext ctx)
    {
        _log = ctx.Logger;
        if (ctx.PluginOptions.TryGetValue("cache", out var c) && c is bool cb) _cache = cb;

        var palette = ctx.Config.Theme.Palette.Count > 0 ? ctx.Config.Theme.Palette[0] : null;
        _background = PrimaryColor(palette?.Primary);
        _accent = AccentColor(palette?.Accent);
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        // Card generation is expensive; only run for real (non-serve) builds.
        if (site.Options.IsServe) return;

        var family = ResolveFontFamily();
        if (family is null)
        {
            _log.LogWarning("social: no usable system font found; skipping card generation");
            return;
        }

        var outDir = Path.Combine(site.Config.AbsoluteSiteDir, "assets", "social");
        Directory.CreateDirectory(outDir);

        var titleFont = family.Value.CreateFont(58, FontStyle.Bold);
        var siteFont = family.Value.CreateFont(28, FontStyle.Regular);
        var descFont = family.Value.CreateFont(30, FontStyle.Regular);

        var count = 0;
        Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, page =>
        {
            var relative = SocialImagePath.For(page);
            var dest = Path.Combine(site.Config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar));
            site.TrackOutput(dest);
            if (_cache && File.Exists(dest)) return;

            var title = string.IsNullOrWhiteSpace(page.Title) ? site.Config.SiteName : page.Title;
            var description = page.FrontMatter.TryGetValue("description", out var d) && d is string ds && ds.Length > 0
                ? ds : site.Config.SiteDescription ?? "";

            using var image = RenderCard(title, site.Config.SiteName, description, titleFont, siteFont, descFont);
            image.SaveAsPng(dest);
            Interlocked.Increment(ref count);
        });

        _log.LogInformation("social: generated {Count} card(s)", count);
        await Task.CompletedTask;
    }

    private Image<Rgba32> RenderCard(string title, string siteName, string description,
        Font titleFont, Font siteFont, Font descFont)
    {
        var image = new Image<Rgba32>(Width, Height);
        const int pad = 70;
        var textWidth = Width - pad * 2;

        image.Mutate(ctx =>
        {
            ctx.Fill(_background);
            // Accent bar on the left edge.
            ctx.Fill(_accent, new SixLabors.ImageSharp.Drawing.RectangularPolygon(0, 0, 12, Height));

            var white = Color.WhiteSmoke;
            var muted = Color.ParseHex("c9ccd1");

            // Site name (top).
            ctx.DrawText(siteName.ToUpperInvariant(), siteFont, muted, new PointF(pad, pad));

            // Title (wrapped), vertically centered-ish.
            var titleOptions = new RichTextOptions(titleFont)
            {
                Origin = new PointF(pad, 190),
                WrappingLength = textWidth,
                LineSpacing = 1.1f,
            };
            ctx.DrawText(titleOptions, title, white);

            // Description (bottom area).
            if (description.Length > 0)
            {
                var descOptions = new RichTextOptions(descFont)
                {
                    Origin = new PointF(pad, Height - pad - 120),
                    WrappingLength = textWidth,
                    LineSpacing = 1.15f,
                };
                ctx.DrawText(descOptions, Truncate(description, 180), muted);
            }
        });

        return image;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private static FontFamily? ResolveFontFamily()
    {
        foreach (var name in new[] { "Open Sans", "Segoe UI", "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans", "Noto Sans" })
            if (SystemFonts.TryGet(name, out var family))
                return family;
        return SystemFonts.Families.FirstOrDefault();
    }

    private static Color PrimaryColor(string? name) => (name ?? "grey").ToLowerInvariant() switch
    {
        "red" => Color.ParseHex("ef5350"),
        "pink" => Color.ParseHex("e91e63"),
        "purple" => Color.ParseHex("ab47bc"),
        "indigo" => Color.ParseHex("3f51b5"),
        "blue" => Color.ParseHex("2196f3"),
        "cyan" => Color.ParseHex("00bcd4"),
        "teal" => Color.ParseHex("009688"),
        "green" => Color.ParseHex("4caf50"),
        "orange" => Color.ParseHex("ff9800"),
        "brown" => Color.ParseHex("795548"),
        "grey" or "gray" => Color.ParseHex("42464e"),
        "blue-grey" => Color.ParseHex("546e7a"),
        "black" => Color.ParseHex("1f2129"),
        _ => Color.ParseHex("42464e"),
    };

    private static Color AccentColor(string? name) => (name ?? "orange").ToLowerInvariant() switch
    {
        "orange" => Color.ParseHex("ff9800"),
        "red" => Color.ParseHex("ff5252"),
        "pink" => Color.ParseHex("ff4081"),
        "purple" => Color.ParseHex("e040fb"),
        "blue" => Color.ParseHex("448aff"),
        "cyan" => Color.ParseHex("18ffff"),
        "teal" => Color.ParseHex("64ffda"),
        "green" => Color.ParseHex("69f0ae"),
        "yellow" => Color.ParseHex("ffd740"),
        _ => Color.ParseHex("ff9800"),
    };
}
