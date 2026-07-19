using Netdocs.Core.Markdown;
using Xunit;

namespace Netdocs.Core.Tests;

public class AbbreviationReducerTests
{
    [Fact]
    public void KeepsFirstOccurrence_UnwrapsLater()
    {
        var html = "<p>The <abbr title=\"HyperText Markup Language\">HTML</abbr> spec. " +
                   "More <abbr title=\"HyperText Markup Language\">HTML</abbr> here.</p>";

        var result = AbbreviationReducer.KeepFirstInstances(html);

        // First stays wrapped, second is unwrapped to plain text.
        Assert.Equal(1, CountOccurrences(result, "<abbr"));
        Assert.Contains("More HTML here.", result);
    }

    [Fact]
    public void DistinctTermsEachKeepFirst()
    {
        var html = "<abbr title=\"a\">HTML</abbr> <abbr title=\"b\">CSS</abbr> " +
                   "<abbr title=\"a\">HTML</abbr> <abbr title=\"b\">CSS</abbr>";

        var result = AbbreviationReducer.KeepFirstInstances(html);

        Assert.Equal(2, CountOccurrences(result, "<abbr"));
    }

    [Fact]
    public void NoAbbr_ReturnsInputUnchanged()
    {
        const string html = "<p>Nothing to reduce here.</p>";
        Assert.Equal(html, AbbreviationReducer.KeepFirstInstances(html));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx += needle.Length; }
        return count;
    }
}
