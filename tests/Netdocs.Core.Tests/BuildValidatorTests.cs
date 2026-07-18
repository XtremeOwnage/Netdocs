using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Validation;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the optional build-time link/anchor/orphan/unused-image validation.</summary>
public sealed class BuildValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-val-" + Guid.NewGuid().ToString("N"));

    public BuildValidatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string SiteDir => Path.Combine(_root, "site");
    private string DocsDir => Path.Combine(_root, "docs");

    private (SiteContext Site, ListLogger Log) NewSite(ValidationConfig validation)
    {
        var site = new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = _root, SiteDir = "site", DocsDir = "docs", Validation = validation },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        Directory.CreateDirectory(SiteDir);
        return (site, new ListLogger());
    }

    private (Page Page, string OutputPath) Emit(string relDir, string html)
    {
        var dir = Path.Combine(SiteDir, relDir);
        Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, "index.html");
        File.WriteAllText(output, html);
        var page = new Page
        {
            SourcePath = Path.Combine(DocsDir, relDir + ".md"),
            RelativePath = relDir + ".md",
            Url = relDir.Length == 0 ? "" : relDir.Replace('\\', '/') + "/",
            OutputPath = output,
        };
        return (page, output);
    }

    [Fact]
    public void BrokenInternalLink_Warns()
    {
        var (site, log) = NewSite(new ValidationConfig { Links = true });
        var (page, _) = Emit("guide", "<a href=\"../missing/\">gone</a>");
        site.Pages.Add(page);

        BuildValidator.Validate(site, [new RenderedPage(page, File.ReadAllText(page.OutputPath))], log);

        Assert.Contains(log.Warnings, w => w.Contains("Broken link") && w.Contains("../missing/"));
    }

    [Fact]
    public void ValidInternalLink_NoWarning()
    {
        var (site, log) = NewSite(new ValidationConfig { Links = true });
        var (target, _) = Emit("reference", "<h1 id=\"top\">Reference</h1>");
        var (page, _) = Emit("guide", "<a href=\"../reference/\">ref</a>");
        site.Pages.Add(target);
        site.Pages.Add(page);

        BuildValidator.Validate(site,
            [new RenderedPage(page, File.ReadAllText(page.OutputPath)),
             new RenderedPage(target, File.ReadAllText(target.OutputPath))], log);

        Assert.DoesNotContain(log.Warnings, w => w.Contains("Broken link"));
    }

    [Fact]
    public void ExternalAndSpecialLinks_Skipped()
    {
        var (site, log) = NewSite(new ValidationConfig { Links = true });
        var (page, _) = Emit("guide",
            "<a href=\"https://example.com/x\">e</a><a href=\"mailto:a@b.co\">m</a><a href=\"#local\">l</a>");
        site.Pages.Add(page);

        BuildValidator.Validate(site, [new RenderedPage(page, File.ReadAllText(page.OutputPath))], log);

        // Anchor check is off, so a same-page #local link doesn't warn; external/mailto are skipped.
        Assert.DoesNotContain(log.Warnings, w => w.Contains("Broken link"));
    }

    [Fact]
    public void BrokenAnchor_WarnsWhenAnchorsEnabled()
    {
        var (site, log) = NewSite(new ValidationConfig { Links = true, Anchors = true });
        var (target, _) = Emit("reference", "<h1 id=\"intro\">Reference</h1>");
        var (page, _) = Emit("guide", "<a href=\"../reference/#nope\">ref</a>");
        site.Pages.Add(target);
        site.Pages.Add(page);

        BuildValidator.Validate(site,
            [new RenderedPage(page, File.ReadAllText(page.OutputPath)),
             new RenderedPage(target, File.ReadAllText(target.OutputPath))], log);

        Assert.Contains(log.Warnings, w => w.Contains("Broken anchor") && w.Contains("nope"));
    }

    [Fact]
    public void ValidAnchor_NoWarning()
    {
        var (site, log) = NewSite(new ValidationConfig { Links = true, Anchors = true });
        var (target, _) = Emit("reference", "<h1 id=\"intro\">Reference</h1>");
        var (page, _) = Emit("guide", "<a href=\"../reference/#intro\">ref</a>");
        site.Pages.Add(target);
        site.Pages.Add(page);

        BuildValidator.Validate(site,
            [new RenderedPage(page, File.ReadAllText(page.OutputPath)),
             new RenderedPage(target, File.ReadAllText(target.OutputPath))], log);

        Assert.DoesNotContain(log.Warnings, w => w.Contains("Broken anchor"));
    }

    [Fact]
    public void UnusedImage_Warns()
    {
        var (site, log) = NewSite(new ValidationConfig { UnusedImages = true });
        Directory.CreateDirectory(Path.Combine(DocsDir, "img"));
        File.WriteAllText(Path.Combine(DocsDir, "img", "unused.png"), "x");
        var (page, _) = Emit("guide", "<p>no images here</p>");
        site.Pages.Add(page);

        BuildValidator.Validate(site, [new RenderedPage(page, File.ReadAllText(page.OutputPath))], log);

        Assert.Contains(log.Warnings, w => w.Contains("Unused image") && w.Contains("img/unused.png"));
    }

    [Fact]
    public void ReferencedImage_NoUnusedWarning()
    {
        var (site, log) = NewSite(new ValidationConfig { UnusedImages = true });
        Directory.CreateDirectory(Path.Combine(DocsDir, "img"));
        File.WriteAllText(Path.Combine(DocsDir, "img", "used.png"), "x");
        // Mirror the asset into the output so the referenced path exists.
        Directory.CreateDirectory(Path.Combine(SiteDir, "img"));
        File.WriteAllText(Path.Combine(SiteDir, "img", "used.png"), "x");
        var (page, _) = Emit("guide", "<img src=\"/img/used.png\">");
        site.Pages.Add(page);

        BuildValidator.Validate(site, [new RenderedPage(page, File.ReadAllText(page.OutputPath))], log);

        Assert.DoesNotContain(log.Warnings, w => w.Contains("Unused image"));
    }

    [Fact]
    public void OrphanPage_WarnsWhenNotInNav()
    {
        var (site, log) = NewSite(new ValidationConfig { OrphanPages = true });
        var (inNav, _) = Emit("guide", "<p>x</p>");
        var (orphan, _) = Emit("secret", "<p>y</p>");
        site.Pages.Add(inNav);
        site.Pages.Add(orphan);
        site.Navigation = [new NavNode { Title = "Guide", Page = inNav }];

        BuildValidator.Validate(site, [], log);

        Assert.Contains(log.Warnings, w => w.Contains("Orphan page") && w.Contains("secret.md"));
        Assert.DoesNotContain(log.Warnings, w => w.Contains("guide.md"));
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
