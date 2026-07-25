using Netdocs.Core.Templating;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Covers the <c>extra.footer_links</c> rendering logic used by the theme footer:
/// internal paths are resolved against <c>base_url</c> (so they work from any page depth)
/// while absolute URLs are left untouched and open in a new tab.
/// </summary>
public class FooterLinksTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-footer-" + Guid.NewGuid().ToString("N"));

    public FooterLinksTests()
    {
        Directory.CreateDirectory(_dir);
        // Mirrors the resolution logic in partials/footer.html.
        File.WriteAllText(Path.Combine(_dir, "t.html"),
            "{{~ for l in extra.footer_links ~}}" +
            "{{~ href = l.link ~}}" +
            "{{~ external = (href | string.starts_with \"http\") || (href | string.starts_with \"//\") ~}}" +
            "{{~ if !external ~}}{{~ href = base_url + (strip_slash href) ~}}{{~ end ~}}" +
            "<a href=\"{{ href }}\"{{ if external }} target=\"_blank\"{{ end }}>{{ l.name }}</a>" +
            "{{~ end ~}}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Render(string baseUrl, params (string name, string link)[] links)
    {
        var engine = new TemplateEngine([_dir]);
        var model = new Dictionary<string, object?>
        {
            ["base_url"] = baseUrl,
            ["extra"] = new Dictionary<string, object?>
            {
                ["footer_links"] = links
                    .Select(l => (object?)new Dictionary<string, object?> { ["name"] = l.name, ["link"] = l.link })
                    .ToList(),
            },
        };
        return engine.Render("t.html", model);
    }

    [Fact]
    public void InternalLinkIsPrefixedWithBaseUrl()
    {
        var html = Render("../../", ("Disclaimers", "/pages/disclaimers/"));
        Assert.Contains("<a href=\"../../pages/disclaimers/\">Disclaimers</a>", html);
        Assert.DoesNotContain("target=\"_blank\"", html);
    }

    [Fact]
    public void InternalLinkAtRootHasNoPrefix()
    {
        var html = Render("", ("Disclaimers", "/pages/disclaimers/"));
        Assert.Contains("<a href=\"pages/disclaimers/\">Disclaimers</a>", html);
    }

    [Theory]
    [InlineData("https://github.com/you/repo")]
    [InlineData("//cdn.example.com/x")]
    public void AbsoluteLinkIsLeftAsIsAndOpensInNewTab(string link)
    {
        var html = Render("../../", ("Source", link));
        Assert.Contains($"<a href=\"{link}\" target=\"_blank\">Source</a>", html);
    }

    [Fact]
    public void RendersMultipleLinksInOrder()
    {
        var html = Render("../", ("Disclaimers", "/pages/disclaimers/"), ("Privacy", "/pages/privacy/"));
        var disc = html.IndexOf("Disclaimers", StringComparison.Ordinal);
        var priv = html.IndexOf("Privacy", StringComparison.Ordinal);
        Assert.True(disc >= 0 && priv > disc);
    }
}
