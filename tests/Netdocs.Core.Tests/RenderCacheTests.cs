using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Verifies the incremental render cache: keys are stable for identical inputs, change when any
/// input changes, and a store→restore round-trip reproduces the rendered artifacts exactly.
/// </summary>
public class RenderCacheTests
{
    private static Page NewPage(string rel = "index.md") => new()
    {
        SourcePath = "",
        RelativePath = rel,
        Title = "Home",
        ProcessedMarkdown = "# Home\n\nHello world.",
    };

    [Fact]
    public void ComputeKey_IsStableForIdenticalInputs()
    {
        var a = RenderCache.ComputeKey(NewPage(), "salt", "linkmap");
        var b = RenderCache.ComputeKey(NewPage(), "salt", "linkmap");
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("# Home\n\nChanged.", "salt", "linkmap")]
    [InlineData("# Home\n\nHello world.", "salt2", "linkmap")]
    [InlineData("# Home\n\nHello world.", "salt", "linkmap2")]
    public void ComputeKey_ChangesWhenAnyInputChanges(string md, string salt, string linkMap)
    {
        var baseline = RenderCache.ComputeKey(NewPage(), "salt", "linkmap");
        var page = NewPage();
        page.ProcessedMarkdown = md;
        Assert.NotEqual(baseline, RenderCache.ComputeKey(page, salt, linkMap));
    }

    [Fact]
    public void StoreThenRestore_ReproducesArtifacts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            // First build: render and store.
            var cache = RenderCache.Load(dir);
            var key = RenderCache.ComputeKey(NewPage(), "salt", "linkmap");
            var rendered = NewPage();
            rendered.HtmlContent = "<h1>Home</h1><p>Hello world.</p>";
            rendered.PlainText = "Home Hello world.";
            rendered.Toc = new List<TocEntry> { new() { Level = 1, Id = "home", Title = "Home" } };
            cache.Store(rendered, key);
            cache.Save();

            // Second build: a fresh page with no artifacts should be restored from disk.
            var reloaded = RenderCache.Load(dir);
            var target = NewPage();
            Assert.True(reloaded.TryRestore(target, key));
            Assert.Equal(1, reloaded.Hits);
            Assert.Equal(rendered.HtmlContent, target.HtmlContent);
            Assert.Equal(rendered.PlainText, target.PlainText);
            Assert.Single(target.Toc);
            Assert.Equal("home", target.Toc[0].Id);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryRestore_MissesWhenKeyChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netdocs-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = RenderCache.Load(dir);
            cache.Store(NewPage(), RenderCache.ComputeKey(NewPage(), "salt", "linkmap"));
            cache.Save();

            var reloaded = RenderCache.Load(dir);
            var target = NewPage();
            var staleKey = RenderCache.ComputeKey(NewPage(), "different-salt", "linkmap");
            Assert.False(reloaded.TryRestore(target, staleKey));
            Assert.Equal(0, reloaded.Hits);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
