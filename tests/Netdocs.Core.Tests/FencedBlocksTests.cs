using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Direct cover for the fenced-block scanner shared by the fence-replacing plugins
/// (<c>calculator</c>, <c>timeline</c>). Those plugins exercise it end to end, but only through
/// whatever YAML they happen to accept; these tests pin the scanning contract itself — what
/// counts as a block, what the render delegate is handed, and what is copied through untouched.
/// </summary>
public class FencedBlocksTests
{
    /// <summary>Renders a marker so the block's boundaries are visible in assertions.</summary>
    private static string Rewrite(string markdown, string infoWord = "calc") =>
        FencedBlocks.Rewrite(markdown, infoWord, (body, index) => $"[{index}:{body}]");

    // --- what counts as a block -------------------------------------------------------

    [Theory]
    [InlineData("```calc\nX\n```")]
    [InlineData("~~~calc\nX\n~~~")]
    [InlineData("`````calc\nX\n`````")]
    [InlineData("   ```calc\nX\n   ```")]
    [InlineData("```calc title=\"t\" {.cls}\nX\n```")]
    [InlineData("```CALC\nX\n```")]
    public void RecognisesFenceShapes(string markdown)
    {
        Assert.Contains("[0:X]", Rewrite(markdown));
    }

    [Theory]
    [InlineData("```calculator\nX\n```")]   // info word must match exactly, not by prefix
    [InlineData("```python\nX\n```")]
    [InlineData("plain text mentioning calc")]
    [InlineData("")]
    public void LeavesEverythingElseAlone(string markdown)
    {
        Assert.Equal(markdown, Rewrite(markdown));
    }

    [Fact]
    public void UnmatchedInput_IsReturnedUnchanged()
    {
        const string markdown = "# Title\n\nNo fences here.";
        Assert.Same(markdown, Rewrite(markdown));
    }

    // --- fence pairing ----------------------------------------------------------------

    [Fact]
    public void ClosingFenceMayBeLongerThanTheOpener()
    {
        Assert.Contains("[0:X]", Rewrite("```calc\nX\n`````"));
    }

    [Fact]
    public void ShorterRunDoesNotCloseTheFence()
    {
        // CommonMark: the closing fence must be at least as long as the opener, so the ``` here
        // is block content and the block runs to the end of the document.
        var result = Rewrite("````calc\nX\n```\nY\n````");
        Assert.Contains("[0:X\n```\nY]", result);
    }

    [Fact]
    public void TildeFenceIsNotClosedByBackticks()
    {
        var result = Rewrite("~~~calc\nX\n```\nY\n~~~");
        Assert.Contains("[0:X\n```\nY]", result);
    }

    [Fact]
    public void UnclosedFence_RunsToEndOfDocument()
    {
        Assert.Contains("[0:X\nY]", Rewrite("```calc\nX\nY"));
    }

    [Fact]
    public void EmptyBody_IsRenderedAsEmpty()
    {
        Assert.Contains("[0:]", Rewrite("```calc\n```"));
    }

    // --- what the delegate receives ---------------------------------------------------

    [Fact]
    public void RenderReceivesTheBodyWithoutEitherFenceLine()
    {
        string? seen = null;
        FencedBlocks.Rewrite("```calc\nline one\nline two\n```", "calc", (body, _) => { seen = body; return ""; });

        Assert.Equal("line one\nline two", seen);
    }

    [Fact]
    public void BlockIndexCountsOnlyMatchedBlocks()
    {
        var indexes = new List<int>();
        FencedBlocks.Rewrite(
            "```calc\nA\n```\n```python\nnope\n```\n```calc\nB\n```\n```calc\nC\n```",
            "calc", (_, index) => { indexes.Add(index); return ""; });

        Assert.Equal([0, 1, 2], indexes);
    }

    // --- surrounding content ----------------------------------------------------------

    [Fact]
    public void ContentAroundBlocksIsPreserved()
    {
        var result = Rewrite("before\n\n```calc\nX\n```\n\nafter");

        Assert.Contains("before", result);
        Assert.Contains("[0:X]", result);
        Assert.Contains("after", result);
    }

    [Fact]
    public void NonMatchingFenceIsCopiedThroughWithItsClosingLine()
    {
        var result = Rewrite("```python\nprint(1)\n```\n\n```calc\nX\n```");

        Assert.Contains("```python\nprint(1)\n```", result);
        Assert.Contains("[0:X]", result);
    }

    [Fact]
    public void MatchingFenceNestedInALargerFence_IsLeftAsSource()
    {
        var markdown = "````markdown\n```calc\nX\n```\n````";
        var result = Rewrite(markdown);

        Assert.DoesNotContain("[0:", result);
        Assert.Contains("```calc", result);
    }

    [Fact]
    public void CarriageReturnsDoNotBreakFenceDetection()
    {
        Assert.Contains("[0:", Rewrite("```calc\r\nX\r\n```\r\n"));
    }

    // --- element ids ------------------------------------------------------------------

    [Fact]
    public void ElementIdIsStableForTheSameInputs()
    {
        Assert.Equal(
            FencedBlocks.ElementId("ndc", "guide/page.md", 0, "amount"),
            FencedBlocks.ElementId("ndc", "guide/page.md", 0, "amount"));
    }

    [Theory]
    [InlineData("other/page.md", 0, "amount")]
    [InlineData("guide/page.md", 1, "amount")]
    [InlineData("guide/page.md", 0, "total")]
    public void ElementIdVariesWithEveryComponent(string pageKey, int blockIndex, string name)
    {
        Assert.NotEqual(
            FencedBlocks.ElementId("ndc", "guide/page.md", 0, "amount"),
            FencedBlocks.ElementId("ndc", pageKey, blockIndex, name));
    }

    [Fact]
    public void ElementIdIsAValidHtmlIdentifier()
    {
        var id = FencedBlocks.ElementId("ndc", "guide/page.md", 0, "amount");

        Assert.StartsWith("ndc-", id);
        Assert.Matches("^ndc-[0-9a-f]{8}$", id);
    }
}
