using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Verifies the incremental output writer only touches files whose contents changed.</summary>
public class OutputWriterTests
{
    private static SiteContext NewSite() =>
        new() { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };

    [Fact]
    public async Task WritesWhenMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "netdocs-ow-" + Guid.NewGuid().ToString("N"), "index.html");
        try
        {
            Assert.True(await OutputWriter.WriteTextIfChangedAsync(path, "<h1>hi</h1>"));
            Assert.Equal("<h1>hi</h1>", await File.ReadAllTextAsync(path));
        }
        finally
        {
            var dir = Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SkipsWhenUnchanged_ButRewritesOnChange()
    {
        var path = Path.Combine(Path.GetTempPath(), "netdocs-ow-" + Guid.NewGuid().ToString("N"), "page.html");
        try
        {
            Assert.True(await OutputWriter.WriteTextIfChangedAsync(path, "same"));
            var firstWrite = File.GetLastWriteTimeUtc(path);

            // Identical content: no write, timestamp preserved.
            await Task.Delay(20);
            Assert.False(await OutputWriter.WriteTextIfChangedAsync(path, "same"));
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path));

            // Different content: rewrite.
            Assert.True(await OutputWriter.WriteTextIfChangedAsync(path, "different"));
            Assert.Equal("different", await File.ReadAllTextAsync(path));
        }
        finally
        {
            var dir = Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TrackedWrite_RecordsOutput_AndPrunePreservesIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-ow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var site = NewSite();
            var kept = Path.Combine(dir, "page.html");
            await OutputWriter.WriteTextIfChangedAsync(site, kept, "<h1>kept</h1>");

            Assert.Contains(Path.GetFullPath(kept), site.WrittenOutputs.Keys);

            // A file that was never tracked should be pruned; the tracked one survives.
            var orphan = Path.Combine(dir, "nested", "orphan.html");
            Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
            await File.WriteAllTextAsync(orphan, "stale");

            var pruned = OutputWriter.PruneStale(site, dir);

            Assert.Equal(1, pruned);
            Assert.True(File.Exists(kept));
            Assert.False(File.Exists(orphan));
            Assert.False(Directory.Exists(Path.GetDirectoryName(orphan)!)); // emptied dir removed
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CopyIfChanged_SkipsIdenticalBytes_AndTracks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-ow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var site = NewSite();
            var source = Path.Combine(dir, "src.bin");
            var dest = Path.Combine(dir, "out", "dest.bin");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);

            Assert.True(await OutputWriter.CopyIfChangedAsync(site, source, dest));
            var firstWrite = File.GetLastWriteTimeUtc(dest);
            Assert.Contains(Path.GetFullPath(dest), site.WrittenOutputs.Keys);

            await Task.Delay(20);
            Assert.False(await OutputWriter.CopyIfChangedAsync(site, source, dest));
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(dest));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
