using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Plugins;

namespace Netdocs.Core.Export;

/// <summary>
/// <c>netdocs export</c>: renders one markdown file as a standalone page and writes it as
/// PDF, PNG and/or WebP using a headless Chromium-family browser.
/// </summary>
public static class SingleFileExporter
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);

    /// <returns>The files written, keyed by format.</returns>
    public static async Task<IReadOnlyDictionary<ExportFormat, string>> ExportAsync(
        SiteConfig config,
        BuildOptions buildOptions,
        PluginRegistry registry,
        ExportOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        var log = loggerFactory.CreateLogger("Export");
        if (!File.Exists(options.SourcePath))
            throw new FileNotFoundException($"Markdown file not found: {options.SourcePath}", options.SourcePath);

        var browserPath = HeadlessBrowser.Find(options.BrowserPath)
            ?? throw new InvalidOperationException(
                "No Chrome, Chromium or Edge browser was found to render the export. Install one, or pass " +
                $"--browser <path> (or set {HeadlessBrowser.BrowserEnvironmentVariable}) to point at its executable.");
        log.LogDebug("Rendering with {Browser}", browserPath);

        var page = await StandalonePageBuilder.BuildAsync(config, buildOptions, registry, loggerFactory, options.SourcePath, options.Scheme, ct);

        // The page is loaded from a temp file; its <base> points back at the docs folder so
        // relative images still resolve, and theme assets load straight from the theme folder.
        var tempDir = Path.Combine(Path.GetTempPath(), "netdocs-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var htmlPath = Path.Combine(tempDir, "page.html");
        await File.WriteAllTextAsync(htmlPath, page.Html, ct);

        var outputs = options.ResolveOutputs();
        try
        {
            await using var browser = await HeadlessBrowser.LaunchAsync(browserPath, LaunchTimeout, ct);
            await browser.LoadAsync(new Uri(htmlPath), options.Width, options.Scale, LoadTimeout, ct);

            foreach (var (format, path) in outputs)
            {
                byte[] data;
                if (format == ExportFormat.Pdf)
                {
                    var (width, height) = ExportOptions.PaperSizes[options.Paper];
                    data = await browser.PrintToPdfAsync(width, height, ct);
                }
                else
                {
                    var (image, downscaled) = await browser.ScreenshotAsync(format, options.Width, options.Scale, ct);
                    if (downscaled)
                        log.LogWarning("The page is too tall for a single {Format} image ({Max}px limit); it was scaled down to fit.",
                            format.ToString().ToUpperInvariant(), HeadlessBrowser.MaxImageDimension);
                    data = image;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, data, ct);
                log.LogInformation("Wrote {Path}", path);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        return outputs;
    }
}
