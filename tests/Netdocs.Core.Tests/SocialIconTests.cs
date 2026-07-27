using Netdocs.Core.Templating;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the social_icon template helper: built-in brands and config-driven overrides.</summary>
public class SocialIconTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-icon-" + Guid.NewGuid().ToString("N"));

    public SocialIconTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "t.html"), "{{ social_icon icon }}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Render(string icon, IDictionary<string, object?>? extra = null)
    {
        var engine = new TemplateEngine([_dir]);
        var model = new Dictionary<string, object?> { ["icon"] = icon };
        if (extra is not null) model["extra"] = extra;
        return engine.Render("t.html", model);
    }

    [Theory]
    [InlineData("fontawesome/brands/github")]
    [InlineData("fontawesome/brands/x-twitter")]
    [InlineData("fontawesome/brands/mastodon")]
    [InlineData("fontawesome/brands/linkedin")]
    [InlineData("fontawesome/brands/youtube")]
    [InlineData("material/email")]
    public void RendersKnownBrandsAsSvg(string icon)
    {
        var html = Render(icon);
        Assert.Contains("<svg", html);
        Assert.Contains("<path d=\"", html);
    }

    [Fact]
    public void UnknownIconFallsBackToGlobe()
    {
        var html = Render("something/unknown");
        Assert.Contains("<svg", html);
    }

    [Fact]
    public void CustomIconOverrideIsUsed()
    {
        var extra = new Dictionary<string, object?>
        {
            ["social_icons"] = new Dictionary<string, object?> { ["bluesky"] = "M1 2 3 4" },
        };
        var html = Render("fontawesome/brands/bluesky", extra);
        Assert.Contains("d=\"M1 2 3 4\"", html);
    }

    [Theory]
    [InlineData("fontawesome/brands/github", "GitHub")]
    [InlineData("fontawesome/brands/discord", "Discord")]
    [InlineData("fontawesome/brands/reddit", "Reddit")]
    [InlineData("fontawesome/solid/rss", "RSS Feed")]
    [InlineData("fontawesome/solid/globe", "Website")]
    [InlineData("fontawesome/brands/x-twitter", "X (Twitter)")]
    [InlineData("fontawesome/brands/linkedin", "LinkedIn")]
    [InlineData("fontawesome/brands/youtube", "YouTube")]
    [InlineData("material/email", "Email")]
    public void SocialLabelMapsKnownIconsToFriendlyNames(string icon, string expected)
        => Assert.Equal(expected, TemplateFunctions.SocialLabel(icon));

    [Theory]
    [InlineData("some/cool-thing", "Cool Thing")]
    [InlineData("simple/my_service", "My Service")]
    [InlineData("bare-name", "Bare Name")]
    public void SocialLabelTitleCasesUnknownIcons(string icon, string expected)
        => Assert.Equal(expected, TemplateFunctions.SocialLabel(icon));

    [Fact]
    public void SocialLabelReturnsEmptyForBlank()
        => Assert.Equal("", TemplateFunctions.SocialLabel("  "));
}
