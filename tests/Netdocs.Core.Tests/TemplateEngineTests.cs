using Netdocs.Core.Templating;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers template rendering limits that only a large site would ever reach.</summary>
public sealed class TemplateEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-tmpl-" + Guid.NewGuid().ToString("N"));

    public TemplateEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private TemplateEngine EngineWith(string name, string template)
    {
        File.WriteAllText(Path.Combine(_dir, name), template);
        return new TemplateEngine([_dir]);
    }

    /// <summary>
    /// Scriban caps a loop at 1000 iterations by default — a sandbox guard aimed at untrusted
    /// templates. Here the template is the theme's own and the loop is over the site's navigation,
    /// so the cap only ever fires on a genuinely large site, and it fails the entire build rather
    /// than degrading. A site with more than 1000 nav entries must still render.
    /// </summary>
    [Fact]
    public void RendersLoopsBeyondScribansDefaultIterationLimit()
    {
        var engine = EngineWith("loop.html", "{{~ for i in items ~}}x{{~ end ~}}");
        var model = new Dictionary<string, object?> { ["items"] = Enumerable.Range(0, 1500).ToList() };

        var html = engine.Render("loop.html", model);

        Assert.Equal(1500, html.Count(c => c == 'x'));
    }

    [Fact]
    public void RendersNestedLoopsBeyondTheLimit()
    {
        // The nav renders recursively, so the cap has to be lifted for inner loops too.
        var engine = EngineWith("nested.html", "{{~ for a in outer ~}}{{~ for b in inner ~}}y{{~ end ~}}{{~ end ~}}");
        var model = new Dictionary<string, object?>
        {
            ["outer"] = Enumerable.Range(0, 2).ToList(),
            ["inner"] = Enumerable.Range(0, 1200).ToList(),
        };

        var html = engine.Render("nested.html", model);

        Assert.Equal(2400, html.Count(c => c == 'y'));
    }
}
