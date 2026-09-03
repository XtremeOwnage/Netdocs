using Netdocs.Abstractions;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Covers Material's <c>hide:</c> front matter, which lets a page opt out of theme chrome
/// (<c>toc</c>, <c>nav</c>, <c>path</c>). Wide auto-generated tables use it to reclaim the
/// horizontal space the table-of-contents column would otherwise take.
/// </summary>
public class HiddenElementsTests
{
    private static Page PageWith(object? hide)
    {
        var meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (hide is not null) meta["hide"] = hide;

        return new Page
        {
            SourcePath = "x.md",
            RelativePath = "x.md",
            FrontMatter = meta,
        };
    }

    [Fact]
    public void NoFrontMatterHidesNothing()
    {
        Assert.Empty(PageRenderer.HiddenElements(PageWith(null)));
    }

    [Fact]
    public void ListValuesAreCollected()
    {
        var hidden = PageRenderer.HiddenElements(PageWith(new List<object?> { "toc", "nav" }));

        Assert.Contains("toc", hidden);
        Assert.Contains("nav", hidden);
        Assert.DoesNotContain("path", hidden);
    }

    [Fact]
    public void SingleScalarValueIsAccepted()
    {
        Assert.Contains("toc", PageRenderer.HiddenElements(PageWith("toc")));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAndTrimmed()
    {
        var hidden = PageRenderer.HiddenElements(PageWith(new List<object?> { "  TOC  " }));

        Assert.Contains("toc", hidden);
    }
}
