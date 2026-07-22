using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Optimization;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the offline self-hosting pass: asset extraction, download, and page rewriting.</summary>
public sealed class SelfHostAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-offline-" + Guid.NewGuid().ToString("N"));

    public SelfHostAssetsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private SiteContext NewSite()
    {
        Directory.CreateDirectory(Path.Combine(_root, "site"));
        return new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = _root, SiteDir = "site", DocsDir = "docs" },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
    }

    private string WritePage(string relDir, string html)
    {
        var dir = Path.Combine(_root, "site", relDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "index.html");
        File.WriteAllText(path, html);
        return path;
    }

    [Fact]
    public async Task SelfHostsScriptAndRewritesToRelativePath()
    {
        var site = NewSite();
        var page = WritePage("guide",
            "<html><body><script src=\"https://cdn.example.com/lib.js\"></script></body></html>");

        await SelfHostAssets.RunAsync(site, NullLogger.Instance, Fetch, default);

        var html = File.ReadAllText(page);
        Assert.DoesNotContain("https://cdn.example.com/lib.js", html);
        Assert.Contains("../assets/external/", html);
        Assert.Contains("lib.js", html);
        Assert.True(Directory.EnumerateFiles(Path.Combine(_root, "site", "assets", "external"), "*lib.js").Any());
    }

    [Fact]
    public async Task LeavesAnchorLinksAlone()
    {
        var site = NewSite();
        var page = WritePage("guide",
            "<a href=\"https://github.com/x/y\">repo</a><link rel=\"preconnect\" href=\"https://fonts.gstatic.com\">");

        await SelfHostAssets.RunAsync(site, NullLogger.Instance, Fetch, default);

        var html = File.ReadAllText(page);
        // <a> links and preconnect hints are not assets — must remain untouched.
        Assert.Contains("https://github.com/x/y", html);
        Assert.Contains("https://fonts.gstatic.com", html);
    }

    [Fact]
    public async Task RewritesMermaidDynamicImport()
    {
        var site = NewSite();
        var page = WritePage("ref",
            "<script type=\"module\">const m = await import(\"https://cdn.example.com/mermaid.mjs\");</script>");

        await SelfHostAssets.RunAsync(site, NullLogger.Instance, Fetch, default);

        var html = File.ReadAllText(page);
        Assert.DoesNotContain("https://cdn.example.com/mermaid.mjs", html);
        Assert.Contains("assets/external/", html);
    }

    [Fact]
    public async Task InlinesCssUrlReferences()
    {
        var site = NewSite();
        WritePage("guide",
            "<link rel=\"stylesheet\" href=\"https://fonts.example.com/font.css\">");

        await SelfHostAssets.RunAsync(site, NullLogger.Instance, Fetch, default);

        var cssFile = Directory.EnumerateFiles(Path.Combine(_root, "site", "assets", "external"), "*.css").Single();
        var css = File.ReadAllText(cssFile);
        // The @font-face gstatic URL must have been rewritten to a local filename.
        Assert.DoesNotContain("https://fonts.example.com/font.woff2", css);
        Assert.Contains(".woff2", css);
        Assert.True(Directory.EnumerateFiles(Path.Combine(_root, "site", "assets", "external"), "*.woff2").Any());
    }

    [Fact]
    public async Task SharesDownloadsAcrossPages()
    {
        var site = NewSite();
        var p1 = WritePage("a", "<script src=\"https://cdn.example.com/lib.js\"></script>");
        var p2 = WritePage("b", "<script src=\"https://cdn.example.com/lib.js\"></script>");

        await SelfHostAssets.RunAsync(site, NullLogger.Instance, Fetch, default);

        // One shared asset file, both pages rewritten.
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "site", "assets", "external"), "*lib.js"));
        Assert.DoesNotContain("https://cdn.example.com", File.ReadAllText(p1));
        Assert.DoesNotContain("https://cdn.example.com", File.ReadAllText(p2));
    }

    // Fake network: returns canned bytes/media types for the test URLs.
    private static Task<(byte[] Bytes, string? MediaType)?> Fetch(string url, CancellationToken ct)
    {
        (byte[], string?)? result = url switch
        {
            "https://cdn.example.com/lib.js" => (Encoding.UTF8.GetBytes("console.log(1)"), "application/javascript"),
            "https://cdn.example.com/mermaid.mjs" => (Encoding.UTF8.GetBytes("export default {}"), "text/javascript"),
            "https://fonts.example.com/font.css" =>
                (Encoding.UTF8.GetBytes("@font-face{src:url(https://fonts.example.com/font.woff2) format('woff2')}"), "text/css"),
            "https://fonts.example.com/font.woff2" => ([1, 2, 3, 4], "font/woff2"),
            _ => null,
        };
        return Task.FromResult(result);
    }
}
