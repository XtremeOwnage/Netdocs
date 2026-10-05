using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Export;
using Netdocs.Core.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Covers <c>netdocs export</c>: argument parsing, output naming, the standalone page (content
/// only, no site chrome, forced palette, resolvable links), and, when a Chromium-family browser
/// is installed, the PDF/PNG/WebP files it produces.
/// </summary>
public class ExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-export-test-" + Guid.NewGuid().ToString("N"));

    public ExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Parse_AcceptsSingleDashOptionsAndFormatList()
    {
        var options = ExportOptions.Parse(["myfile.md", "-format", "pdf,png,WebP", "-theme", "dark"], out var error);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(Path.GetFullPath("myfile.md"), options.SourcePath);
        Assert.Equal([ExportFormat.Pdf, ExportFormat.Png, ExportFormat.Webp], options.Formats);
        Assert.Equal("slate", options.Scheme);
    }

    [Fact]
    public void Parse_DefaultsToPdfAndSitePalette()
    {
        var options = ExportOptions.Parse(["--width=1200", "page.md", "--paper", "A4"], out _);

        Assert.NotNull(options);
        Assert.Equal([ExportFormat.Pdf], options.Formats);
        Assert.Null(options.Scheme);
        Assert.Equal(1200, options.Width);
        Assert.Equal("a4", options.Paper);
    }

    [Theory]
    [InlineData("--format", "gif")]
    [InlineData("--theme", "purple")]
    [InlineData("--paper", "napkin")]
    [InlineData("--scale", "0")]
    [InlineData("--bogus", "x")]
    public void Parse_RejectsInvalidValues(string flag, string value)
    {
        var options = ExportOptions.Parse(["page.md", flag, value], out var error);

        Assert.Null(options);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_RequiresExactlyOneFile()
    {
        Assert.Null(ExportOptions.Parse(["--theme", "dark"], out _));
        Assert.Null(ExportOptions.Parse(["a.md", "b.md"], out _));
    }

    [Fact]
    public void ResolveOutputs_WritesNextToSourceByDefault()
    {
        var source = Path.Combine(_dir, "notes.md");
        var options = new ExportOptions { SourcePath = source, Formats = [ExportFormat.Pdf, ExportFormat.Webp] };

        var outputs = options.ResolveOutputs();

        Assert.Equal(Path.Combine(_dir, "notes.pdf"), outputs[ExportFormat.Pdf]);
        Assert.Equal(Path.Combine(_dir, "notes.webp"), outputs[ExportFormat.Webp]);
    }

    [Fact]
    public void ResolveOutputs_HonorsDirectoryAndFileOutputs()
    {
        var source = Path.Combine(_dir, "notes.md");

        var intoDir = new ExportOptions { SourcePath = source, OutputPath = _dir + Path.DirectorySeparatorChar + "out" + Path.DirectorySeparatorChar, Formats = [ExportFormat.Png] };
        Assert.Equal(Path.Combine(_dir, "out", "notes.png"), intoDir.ResolveOutputs()[ExportFormat.Png]);

        var named = new ExportOptions { SourcePath = source, OutputPath = Path.Combine(_dir, "report.pdf"), Formats = [ExportFormat.Pdf, ExportFormat.Png] };
        var outputs = named.ResolveOutputs();
        Assert.Equal(Path.Combine(_dir, "report.pdf"), outputs[ExportFormat.Pdf]);
        Assert.Equal(Path.Combine(_dir, "report.png"), outputs[ExportFormat.Png]);
    }

    [Fact]
    public async Task StandalonePage_RendersContentWithoutSiteChrome()
    {
        var source = WriteSample();

        var page = await Build(DefaultConfig(), source, scheme: "slate");

        Assert.Equal("Export Sample", page.Title);
        Assert.Contains("<h1 id=\"export-sample\"", page.Html);
        Assert.Contains("class=\"admonition note\"", page.Html);
        Assert.Contains("data-md-color-scheme=\"slate\"", page.Html);

        // No header, navigation sidebars, search, breadcrumbs, or footer.
        Assert.DoesNotContain("md-header", page.Html);
        Assert.DoesNotContain("md-sidebar", page.Html);
        Assert.DoesNotContain("md-search", page.Html);
        Assert.DoesNotContain("md-path", page.Html);
        Assert.DoesNotContain("md-footer", page.Html);
    }

    [Fact]
    public async Task StandalonePage_ResolvesThemeAssetsAndImagesFromDisk()
    {
        var source = WriteSample();

        var page = await Build(DefaultConfig(), source, scheme: null);

        // Relative content links resolve from the markdown file's folder via <base>, and the
        // theme stylesheet is referenced by an absolute file URL that actually exists.
        Assert.Contains($"<base href=\"{new Uri(_dir + Path.DirectorySeparatorChar).AbsoluteUri}\">", page.Html);
        Assert.Contains("src=\"img/pixel.png\"", page.Html);
        var css = System.Text.RegularExpressions.Regex.Match(page.Html, "href=\"(file:[^\"]+main\\.[^\"]+\\.css)\"");
        Assert.True(css.Success, "theme stylesheet link missing");
        Assert.True(File.Exists(new Uri(css.Groups[1].Value).LocalPath));
    }

    [Fact]
    public void ResolvePalette_KeepsTheMatchingPaletteColors()
    {
        var config = new SiteConfig
        {
            Theme = new ThemeConfig
            {
                Palette =
                [
                    new PaletteConfig { Scheme = "default", Primary = "teal", Accent = "amber" },
                    new PaletteConfig { Scheme = "slate", Primary = "deep purple", Accent = "lime" },
                ],
            },
        };

        Assert.Equal(("slate", "deep-purple", "lime"), StandalonePageBuilder.ResolvePalette(config, "slate"));
        Assert.Equal(("default", "teal", "amber"), StandalonePageBuilder.ResolvePalette(config, null));
        Assert.Equal(("default", "indigo", "indigo"), StandalonePageBuilder.ResolvePalette(new SiteConfig(), null));
    }

    [Fact]
    public async Task Export_WritesPdfPngAndWebp_WhenBrowserAvailable()
    {
        // Rendering needs a local Chrome/Chromium/Edge; machines without one only run the tests above.
        if (HeadlessBrowser.Find() is null) return;

        var source = WriteSample();
        var options = new ExportOptions
        {
            SourcePath = source,
            Formats = [ExportFormat.Pdf, ExportFormat.Png, ExportFormat.Webp],
            Scheme = "slate",
            OutputPath = Path.Combine(_dir, "out") + Path.DirectorySeparatorChar,
        };

        var outputs = await SingleFileExporter.ExportAsync(DefaultConfig(), new BuildOptions(), new PluginRegistry(), options, NullLoggerFactory.Instance);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(outputs[ExportFormat.Pdf])[..4]));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], File.ReadAllBytes(outputs[ExportFormat.Png])[..4]);
        var webp = File.ReadAllBytes(outputs[ExportFormat.Webp]);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(webp[..4]));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(webp[8..12]));
    }

    private SiteConfig DefaultConfig() => new() { ProjectRoot = _dir, DocsDir = "." };

    private static Task<StandalonePage> Build(SiteConfig config, string source, string? scheme) =>
        StandalonePageBuilder.BuildAsync(config, new BuildOptions(), new PluginRegistry(), NullLoggerFactory.Instance, source, scheme);

    private string WriteSample()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "img"));
        // 1x1 transparent PNG.
        File.WriteAllBytes(Path.Combine(_dir, "img", "pixel.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));
        var path = Path.Combine(_dir, "sample.md");
        File.WriteAllText(path, """
            ---
            title: Export Sample
            ---

            # Export sample

            !!! note
                An admonition.

            ![pixel](img/pixel.png)
            """);
        return path;
    }
}
