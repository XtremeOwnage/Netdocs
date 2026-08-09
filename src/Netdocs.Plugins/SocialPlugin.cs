using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Netdocs.Plugins;

/// <summary>
/// Generates Material-style social (Open Graph) cards for each page. Layout, colors, size, fonts,
/// format and output directory are configurable through <see cref="SocialCardOptions"/>; with no
/// options at all the card is drawn from the theme palette.
/// </summary>
public sealed class SocialPlugin : IPlugin, IBuildHook
{
    private ILogger _log = null!;
    private SocialCardOptions _options = new();
    private string _projectRoot = "";
    private CardFonts? _fonts;

    public string Name => "social";

    public void Configure(IPluginContext ctx)
    {
        _log = ctx.Logger;
        _projectRoot = ctx.Config.ProjectRoot;
        var palette = ctx.Config.Theme.Palette.Count > 0 ? ctx.Config.Theme.Palette[0] : null;
        _options = SocialCardOptions.Parse(ctx.PluginOptions, palette);
    }

    /// <summary>
    /// Publishes the card location before pages render, so <c>og:image</c> points at the file this
    /// plugin will write — and, when cards are off or no font is available, so no page advertises
    /// an image that will never exist. The font is resolved here, not at draw time, precisely
    /// because pages are rendered first: deciding later would be too late to affect their markup.
    /// </summary>
    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        _fonts = null;
        if (!Generating(site)) return Task.CompletedTask;

        _fonts = LoadFonts();
        if (_fonts is null)
        {
            _log.LogWarning("social: no usable font found; skipping card generation");
            return Task.CompletedTask;
        }

        site.State[SocialImagePath.StateKey] = _options.PathSettings;
        return Task.CompletedTask;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        if (!Generating(site)) return;

        // OnBuildStart resolves the font; a null here means it already reported why.
        var fonts = _fonts;
        if (fonts is null) return;

        using var background = LoadImage(_options.BackgroundImage, "background_image");
        using var logo = LoadImage(_options.Logo, "logo");

        var count = 0;
        Parallel.ForEach(site.Pages, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, page =>
        {
            var relative = SocialImagePath.For(page, _options.PathSettings);
            var dest = Path.Combine(site.Config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar));
            site.TrackOutput(dest);
            if (_options.Cache && File.Exists(dest)) return;

            // Defensive: ensure the parent directory exists even if the output dir was pruned or
            // the configured cards_dir is nested. Cheap + idempotent.
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            var (title, description) = CardText(site, page);
            using var image = RenderCard(title, site.Config.SiteName, description, fonts.Value, background, logo);
            Save(image, dest);
            Interlocked.Increment(ref count);
        });

        _log.LogInformation("social: generated {Count} card(s) in {Dir}", count, _options.CardsDir);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Cards are cached by file existence, so a serve session only pays the cost once (on the
    /// first build). Generate on serve by default; large sites can opt out.
    /// </summary>
    private bool Generating(SiteContext site) =>
        _options.Cards && !(site.Options.IsServe && !_options.EnabledOnServe);

    /// <summary>Front matter wins, then the configured static override, then the site defaults.</summary>
    private (string Title, string Description) CardText(SiteContext site, Page page)
    {
        var (pageTitle, pageDescription) = SocialCardOptions.PageOverrides(page.FrontMatter);

        var title = pageTitle
            ?? _options.Title
            ?? (string.IsNullOrWhiteSpace(page.DisplayTitle) ? site.Config.SiteName : page.DisplayTitle);

        var description = pageDescription
            ?? (page.FrontMatter.GetValueOrDefault("description") is string d && d.Length > 0 ? d : null)
            ?? _options.Description
            ?? site.Config.SiteDescription
            ?? "";

        return (title, description);
    }

    private Image<Rgba32> RenderCard(string title, string siteName, string description,
        CardFonts fonts, Image? background, Image? logo)
    {
        var image = new Image<Rgba32>(_options.Width, _options.Height);
        var pad = _options.Padding;
        var textWidth = _options.Width - pad * 2;

        image.Mutate(ctx =>
        {
            ctx.Fill(_options.BackgroundColor);

            if (background is not null)
            {
                using var scaled = background.Clone(c => c.Resize(new ResizeOptions
                {
                    Size = new Size(_options.Width, _options.Height),
                    Mode = ResizeMode.Crop,
                }));
                ctx.DrawImage(scaled, 1f);
            }

            if (_options.AccentWidth > 0)
                ctx.Fill(_options.AccentColor,
                    new SixLabors.ImageSharp.Drawing.RectangularPolygon(0, 0, _options.AccentWidth, _options.Height));

            if (logo is not null)
            {
                using var scaled = logo.Clone(c => c.Resize(new ResizeOptions
                {
                    Size = new Size(_options.LogoSize, _options.LogoSize),
                    Mode = ResizeMode.Max,
                }));
                ctx.DrawImage(scaled, new Point(_options.Width - pad - scaled.Width, pad), 1f);
            }

            // Site name (top).
            ctx.DrawText(siteName.ToUpperInvariant(), fonts.SiteName, _options.DescriptionColor, new PointF(pad, pad));

            // Title (wrapped), below the site name.
            var titleOptions = new RichTextOptions(fonts.Title)
            {
                Origin = new PointF(pad, _options.Height * 0.30f),
                WrappingLength = textWidth,
                LineSpacing = 1.1f,
            };
            ctx.DrawText(titleOptions, title, _options.TitleColor);

            // Description (bottom area).
            if (description.Length > 0)
            {
                var descOptions = new RichTextOptions(fonts.Description)
                {
                    Origin = new PointF(pad, _options.Height - pad - _options.DescriptionFontSize * 4),
                    WrappingLength = textWidth,
                    LineSpacing = 1.15f,
                };
                ctx.DrawText(descOptions, Truncate(description, _options.DescriptionLength), _options.DescriptionColor);
            }
        });

        return image;
    }

    private void Save(Image image, string path)
    {
        switch (_options.Format)
        {
            case SocialCardFormat.Jpeg:
                image.SaveAsJpeg(path, new JpegEncoder { Quality = _options.Quality });
                break;
            case SocialCardFormat.Webp:
                image.SaveAsWebp(path, new WebpEncoder { Quality = _options.Quality });
                break;
            default:
                image.SaveAsPng(path);
                break;
        }
    }

    /// <summary>Loads a configured decoration (background/logo), or null when unset or unreadable.
    /// A bad path degrades the card rather than failing the build.</summary>
    private Image? LoadImage(string? configured, string optionName)
    {
        if (configured is null) return null;

        var path = Path.IsPathRooted(configured) ? configured : Path.Combine(_projectRoot, configured);
        try
        {
            return Image.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            _log.LogWarning("social: could not load {Option} '{Path}' ({Message}); ignoring", optionName, configured, ex.Message);
            return null;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private readonly record struct CardFonts(Font Title, Font Description, Font SiteName);

    private CardFonts? LoadFonts()
    {
        var family = ResolveFontFamily();
        if (family is null) return null;

        return new CardFonts(
            family.Value.CreateFont(_options.TitleFontSize, FontStyle.Bold),
            family.Value.CreateFont(_options.DescriptionFontSize, FontStyle.Regular),
            family.Value.CreateFont(_options.SiteNameFontSize, FontStyle.Regular));
    }

    private FontFamily? ResolveFontFamily()
    {
        // An explicit font file needs nothing installed on the machine — the reliable option for
        // containers and CI, where system fonts are often absent entirely.
        if (_options.FontPath is { } fontPath)
        {
            var path = Path.IsPathRooted(fontPath) ? fontPath : Path.Combine(_projectRoot, fontPath);
            try
            {
                return new FontCollection().Add(path);
            }
            catch (Exception ex) when (ex is IOException or FontException)
            {
                _log.LogWarning("social: could not load font_path '{Path}' ({Message}); falling back", fontPath, ex.Message);
            }
        }

        if (_options.FontFamily is { } configured)
        {
            if (SystemFonts.TryGet(configured, out var requested)) return requested;
            _log.LogWarning("social: font_family '{Font}' is not installed; falling back", configured);
        }

        foreach (var name in new[] { "Open Sans", "Roboto", "Segoe UI", "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans", "Noto Sans" })
            if (SystemFonts.TryGet(name, out var family))
                return family;

        // SystemFonts.Families.FirstOrDefault() returns a *default* FontFamily struct
        // (not null) when no fonts are installed -- e.g. inside a minimal container.
        // Calling CreateFont on that default throws "Cannot use the default value type
        // instance to create a font" and fails the whole build. Only return a family
        // when one genuinely exists, so callers can skip card generation instead.
        var first = SystemFonts.Families.FirstOrDefault();
        if (first != default)
        {
            _log.LogInformation("social: using fallback system font '{Font}'", first.Name);
            return first;
        }

        return null;
    }
}
