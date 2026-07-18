using Netdocs.Core.Optimization;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers webp encoding and the on-disk conversion cache.</summary>
public class WebpConverterTests : IDisposable
{
    // 2x2 solid PNG.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAE0lEQVR4nGPhEpH7zwAELAxQAAASHAFEWc1phgAAAABJRU5ErkJggg==";

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "netdocs-webp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_cache, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ConvertsPngToWebpAndCaches()
    {
        var png = Convert.FromBase64String(TinyPngBase64);
        var converter = new WebpConverter(_cache, 80);

        var webp = await converter.ConvertAsync(png, CancellationToken.None);

        Assert.NotNull(webp);
        // RIFF....WEBP magic bytes.
        Assert.Equal((byte)'R', webp![0]);
        Assert.Equal((byte)'I', webp[1]);
        Assert.Equal((byte)'F', webp[2]);
        Assert.Equal((byte)'F', webp[3]);
        Assert.True(Directory.Exists(_cache));
        Assert.NotEmpty(Directory.GetFiles(_cache, "*.webp"));
    }

    [Fact]
    public async Task SecondCallUsesCache()
    {
        var png = Convert.FromBase64String(TinyPngBase64);
        var converter = new WebpConverter(_cache, 80);

        var first = await converter.ConvertAsync(png, CancellationToken.None);
        var second = await converter.ConvertAsync(png, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task InvalidImageReturnsNull()
    {
        var converter = new WebpConverter(_cache, 80);
        var result = await converter.ConvertAsync("not an image"u8.ToArray(), CancellationToken.None);
        Assert.Null(result);
    }
}
