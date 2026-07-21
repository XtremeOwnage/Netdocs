using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the pymdownx.b64 image-embedding preprocessor.</summary>
public class Base64EmbedPluginTests : IDisposable
{
    // 1x1 transparent PNG.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-b64-" + Guid.NewGuid().ToString("N"));

    public Base64EmbedPluginTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "pic.png"), PngBytes);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static SiteContext Site() => new()
    {
        Config = new SiteConfig(),
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private Page PageWith(Dictionary<string, object?>? frontMatter = null) => new()
    {
        SourcePath = Path.Combine(_dir, "index.md"),
        RelativePath = "index.md",
        Url = "",
        FrontMatter = frontMatter ?? new Dictionary<string, object?>(),
    };

    [Fact]
    public async Task EmbedsLocalImage_WhenFrontMatterEnabled()
    {
        var page = PageWith(new() { ["embed_images"] = true });
        var result = await new Base64EmbedPlugin()
            .ProcessAsync(page, "![alt](pic.png)", Site(), default);

        Assert.Contains("![alt](data:image/png;base64,", result);
        Assert.DoesNotContain("(pic.png)", result);
    }

    [Fact]
    public async Task LeavesMarkdownUntouched_WhenDisabledByDefault()
    {
        var page = PageWith();
        const string md = "![alt](pic.png)";
        var result = await new Base64EmbedPlugin().ProcessAsync(page, md, Site(), default);

        Assert.Equal(md, result);
    }

    [Fact]
    public async Task DoesNotEmbedRemoteImages()
    {
        var page = PageWith(new() { ["embed_images"] = true });
        const string md = "![x](https://example.com/a.png)";
        var result = await new Base64EmbedPlugin().ProcessAsync(page, md, Site(), default);

        Assert.Equal(md, result);
    }

    [Fact]
    public async Task EmbedsInlineHtmlImage()
    {
        var page = PageWith(new() { ["embed_images"] = true });
        var result = await new Base64EmbedPlugin()
            .ProcessAsync(page, "<img src=\"pic.png\" alt=\"x\">", Site(), default);

        Assert.Contains("src=\"data:image/png;base64,", result);
    }

    [Fact]
    public async Task PreservesTitleOnMarkdownImage()
    {
        var page = PageWith(new() { ["embed_images"] = true });
        var result = await new Base64EmbedPlugin()
            .ProcessAsync(page, "![alt](pic.png \"A title\")", Site(), default);

        Assert.Contains("data:image/png;base64,", result);
        Assert.Contains("\"A title\")", result);
    }

    [Fact]
    public async Task SiteDefaultOption_EnablesEmbedding()
    {
        var plugin = new Base64EmbedPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?> { ["default"] = true }));

        var page = PageWith();
        var result = await plugin.ProcessAsync(page, "![alt](pic.png)", Site(), default);

        Assert.Contains("data:image/png;base64,", result);
    }

    [Fact]
    public async Task FrontMatterFalse_OverridesSiteDefault()
    {
        var plugin = new Base64EmbedPlugin();
        plugin.Configure(new FakeContext(new Dictionary<string, object?> { ["default"] = true }));

        var page = PageWith(new() { ["embed_images"] = false });
        const string md = "![alt](pic.png)";
        var result = await plugin.ProcessAsync(page, md, Site(), default);

        Assert.Equal(md, result);
    }

    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options) : IPluginContext
    {
        public SiteConfig Config { get; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }
}
