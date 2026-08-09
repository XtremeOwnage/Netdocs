using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core;
using Netdocs.Plugins;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Covers the configurable social-card options: parsing and defaults, the card path published for
/// <c>og:image</c>, per-page front-matter overrides, and (where a font is available) the drawn card.
/// </summary>
public class SocialPluginTests
{
    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options, SiteConfig config) : IPluginContext
    {
        public SiteConfig Config { get; } = config;
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }

    private static Dictionary<string, object?> Options(params (string Key, object? Value)[] pairs)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs) map[key] = value;
        return map;
    }

    private static Dictionary<string, object?> Layout(params (string Key, object? Value)[] pairs) =>
        Options(("cards_layout_options", Options(pairs)));

    // ---------------------------------------------------------------- option parsing

    [Fact]
    public void Defaults_MatchTheDocumentedCardShape()
    {
        var options = SocialCardOptions.Parse(Options(), palette: null);

        Assert.True(options.Cards);
        Assert.True(options.Cache);
        Assert.True(options.EnabledOnServe);
        Assert.Equal("assets/social", options.CardsDir);
        Assert.Equal(SocialCardFormat.Png, options.Format);
        Assert.Equal(1200, options.Width);
        Assert.Equal(630, options.Height);
        Assert.Equal(12, options.AccentWidth);
        Assert.Equal(".png", options.Extension);
    }

    [Fact]
    public void PaletteDrivesTheDefaultColors()
    {
        var palette = new PaletteConfig { Primary = "blue", Accent = "pink" };
        var options = SocialCardOptions.Parse(Options(), palette);

        Assert.Equal(Color.ParseHex("2196f3"), options.BackgroundColor);
        Assert.Equal(Color.ParseHex("ff4081"), options.AccentColor);
    }

    [Fact]
    public void LayoutOptionsOverridePaletteColors()
    {
        var options = SocialCardOptions.Parse(
            Layout(("background_color", "#101820"), ("color", "#fafafa"), ("accent_color", "#00ff00")),
            new PaletteConfig { Primary = "blue" });

        Assert.Equal(Color.ParseHex("101820"), options.BackgroundColor);
        Assert.Equal(Color.ParseHex("fafafa"), options.TitleColor);
        Assert.Equal(Color.ParseHex("00ff00"), options.AccentColor);
    }

    [Fact]
    public void MaterialPaletteNamesAreAcceptedAsExplicitColors()
    {
        var options = SocialCardOptions.Parse(Layout(("background_color", "blue-grey")), palette: null);
        Assert.Equal(Color.ParseHex("546e7a"), options.BackgroundColor);
    }

    [Fact]
    public void UnparseableColorKeepsTheDefault()
    {
        var options = SocialCardOptions.Parse(Layout(("background_color", "not-a-color")), palette: null);
        Assert.Equal(Color.ParseHex("42464e"), options.BackgroundColor);
    }

    [Theory]
    [InlineData("png", SocialCardFormat.Png, ".png")]
    [InlineData("jpeg", SocialCardFormat.Jpeg, ".jpg")]
    [InlineData("jpg", SocialCardFormat.Jpeg, ".jpg")]
    [InlineData("WEBP", SocialCardFormat.Webp, ".webp")]
    [InlineData("nonsense", SocialCardFormat.Png, ".png")]
    public void FormatSelectsTheEncoderAndExtension(string configured, SocialCardFormat expected, string extension)
    {
        var options = SocialCardOptions.Parse(Options(("format", configured)), palette: null);

        Assert.Equal(expected, options.Format);
        Assert.Equal(extension, options.Extension);
    }

    [Fact]
    public void NumericOptionsAreParsedFromJsonLongsAndStrings()
    {
        var options = SocialCardOptions.Parse(
            Options(("quality", 55L), ("cards_layout_options", Options(("width", "800"), ("height", 400L)))),
            palette: null);

        Assert.Equal(55, options.Quality);
        Assert.Equal(800, options.Width);
        Assert.Equal(400, options.Height);
    }

    [Fact]
    public void OutOfRangeValuesAreClampedRatherThanBreakingTheRender()
    {
        var options = SocialCardOptions.Parse(
            Options(("quality", 5000L), ("cards_layout_options", Options(("width", 5L), ("accent_width", -10L)))),
            palette: null);

        Assert.Equal(100, options.Quality);
        Assert.Equal(200, options.Width);
        Assert.Equal(0, options.AccentWidth);
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        var options = SocialCardOptions.Parse(Options(("nope", "value"), ("cards_dir", "og")), palette: null);
        Assert.Equal("og", options.CardsDir);
    }

    // ---------------------------------------------------------------- card paths

    [Fact]
    public void CardPathFollowsCardsDirAndFormat()
    {
        var options = SocialCardOptions.Parse(Options(("cards_dir", "og"), ("format", "webp")), palette: null);
        var page = new Page { SourcePath = "", RelativePath = "guide/setup.md", Url = "guide/setup/" };

        Assert.Equal("og/guide_setup.webp", SocialImagePath.For(page, options.PathSettings));
    }

    [Fact]
    public void HomePageCardIsNamedIndex()
    {
        var page = new Page { SourcePath = "", RelativePath = "index.md", Url = "" };
        Assert.Equal("assets/social/index.png", SocialImagePath.For(page));
    }

    [Fact]
    public void ResolveReturnsNull_WhenNothingIsGeneratingCards()
    {
        var site = Site(new SiteConfig());
        var page = new Page { SourcePath = "", RelativePath = "index.md", Url = "" };

        Assert.Null(SocialImagePath.Resolve(site, page));
    }

    [Fact]
    public async Task BuildStartPublishesTheCardLocation()
    {
        var site = Site(new SiteConfig());
        var plugin = new SocialPlugin();
        plugin.Configure(new FakeContext(Options(("cards_dir", "og"), ("format", "jpeg")), site.Config));

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        var page = new Page { SourcePath = "", RelativePath = "a.md", Url = "a/" };
        Assert.Equal("og/a.jpg", SocialImagePath.Resolve(site, page));
    }

    [Fact]
    public async Task DisablingCardsSuppressesTheCardLocation()
    {
        var site = Site(new SiteConfig());
        var plugin = new SocialPlugin();
        plugin.Configure(new FakeContext(Options(("cards", false)), site.Config));

        await plugin.OnBuildStartAsync(site, CancellationToken.None);

        var page = new Page { SourcePath = "", RelativePath = "a.md", Url = "a/" };
        Assert.Null(SocialImagePath.Resolve(site, page));
    }

    // ---------------------------------------------------------------- per-page overrides

    [Fact]
    public void FrontMatterOverridesTheCardText()
    {
        var frontMatter = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["social"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cards_layout_options"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = "Custom title",
                    ["description"] = "Custom description",
                },
            },
        };

        var (title, description) = SocialCardOptions.PageOverrides(frontMatter);

        Assert.Equal("Custom title", title);
        Assert.Equal("Custom description", description);
    }

    [Fact]
    public void FrontMatterAcceptsTheShorthandWithoutTheLayoutBlock()
    {
        var frontMatter = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["social"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["title"] = "Short" },
        };

        Assert.Equal("Short", SocialCardOptions.PageOverrides(frontMatter).Title);
    }

    [Fact]
    public void NoSocialFrontMatterMeansNoOverride()
    {
        var frontMatter = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["title"] = "Page" };
        Assert.Equal((null, null), SocialCardOptions.PageOverrides(frontMatter));
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>
    /// Card drawing needs a real font. A machine with none installed (a bare container) cannot
    /// exercise these paths at all — the plugin deliberately skips generation there — so the
    /// rendering tests bail out rather than assert on output that was never meant to appear.
    /// </summary>
    private static bool FontsAvailable => SystemFonts.Families.Any();

    [Fact]
    public async Task GeneratesACardPerPage_AtTheConfiguredSizeAndFormat()
    {
        if (!FontsAvailable) return;

        var dir = TempDir();
        try
        {
            var site = Site(new SiteConfig { ProjectRoot = dir, SiteName = "Docs" });
            site.Pages.Add(new Page { SourcePath = "", RelativePath = "index.md", Url = "", Title = "Home" });
            site.Pages.Add(new Page { SourcePath = "", RelativePath = "a.md", Url = "a/", Title = "A" });

            var plugin = new SocialPlugin();
            plugin.Configure(new FakeContext(
                Options(("cards_dir", "og"), ("format", "jpeg"),
                    ("cards_layout_options", Options(("width", 800L), ("height", 418L)))),
                site.Config));
            await plugin.OnBuildStartAsync(site, CancellationToken.None);
            await plugin.OnBuildCompleteAsync(site, CancellationToken.None);

            var card = Path.Combine(site.Config.AbsoluteSiteDir, "og", "index.jpg");
            Assert.True(File.Exists(card));
            Assert.True(File.Exists(Path.Combine(site.Config.AbsoluteSiteDir, "og", "a.jpg")));

            using var image = Image.Load(card);
            Assert.Equal(800, image.Width);
            Assert.Equal(418, image.Height);

            // Every card must survive stale-file pruning.
            Assert.True(site.WrittenOutputs.ContainsKey(Path.GetFullPath(card)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CacheReusesAnExistingCard()
    {
        if (!FontsAvailable) return;

        var dir = TempDir();
        try
        {
            var site = Site(new SiteConfig { ProjectRoot = dir, SiteName = "Docs" });
            site.Pages.Add(new Page { SourcePath = "", RelativePath = "index.md", Url = "", Title = "Home" });

            var plugin = new SocialPlugin();
            plugin.Configure(new FakeContext(Options(), site.Config));
            await plugin.OnBuildStartAsync(site, CancellationToken.None);
            await plugin.OnBuildCompleteAsync(site, CancellationToken.None);

            var card = Path.Combine(site.Config.AbsoluteSiteDir, "assets", "social", "index.png");
            var written = File.GetLastWriteTimeUtc(card);

            await plugin.OnBuildStartAsync(site, CancellationToken.None);
            await plugin.OnBuildCompleteAsync(site, CancellationToken.None);

            Assert.Equal(written, File.GetLastWriteTimeUtc(card));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DisabledCardsWriteNothing()
    {
        if (!FontsAvailable) return;

        var dir = TempDir();
        try
        {
            var site = Site(new SiteConfig { ProjectRoot = dir, SiteName = "Docs" });
            site.Pages.Add(new Page { SourcePath = "", RelativePath = "index.md", Url = "", Title = "Home" });

            var plugin = new SocialPlugin();
            plugin.Configure(new FakeContext(Options(("cards", false)), site.Config));
            await plugin.OnBuildStartAsync(site, CancellationToken.None);
            await plugin.OnBuildCompleteAsync(site, CancellationToken.None);

            Assert.False(Directory.Exists(Path.Combine(site.Config.AbsoluteSiteDir, "assets", "social")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static SiteContext Site(SiteConfig config) => new()
    {
        Config = config,
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-social-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
