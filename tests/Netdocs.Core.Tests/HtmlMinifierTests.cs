using Netdocs.Core.Optimization;
using Xunit;

namespace Netdocs.Core.Tests;

public class HtmlMinifierTests
{
    [Fact]
    public void Minify_CollapsesWhitespaceBetweenTags()
    {
        var input = "<div>\n    <p>Hello</p>\n    <p>World</p>\n</div>";
        var output = HtmlMinifier.Minify(input);
        Assert.Equal("<div><p>Hello</p><p>World</p></div>", output);
    }

    [Fact]
    public void Minify_PreservesPreContent()
    {
        var input = "<pre>\n  line1\n    line2\n</pre>";
        var output = HtmlMinifier.Minify(input);
        Assert.Contains("  line1\n    line2", output);
    }

    [Fact]
    public void Minify_PreservesScriptAndStyle()
    {
        var input = "<style>\n  .a {\n    color: red;\n  }\n</style>";
        var output = HtmlMinifier.Minify(input);
        Assert.Contains("color: red;", output);
        Assert.Contains("\n", output);
    }

    [Fact]
    public void Minify_StripsComments_KeepsConditionals()
    {
        var input = "<div><!-- drop me --><!--[if IE]>keep<![endif]--></div>";
        var output = HtmlMinifier.Minify(input);
        Assert.DoesNotContain("drop me", output);
        Assert.Contains("[if IE]", output);
    }

    [Fact]
    public void Minify_KeepsSingleSpaceInInlineText()
    {
        var input = "<p>Hello    world\n    again</p>";
        var output = HtmlMinifier.Minify(input);
        Assert.Equal("<p>Hello world again</p>", output);
    }

    [Fact]
    public void Minify_EmptyString_ReturnsEmpty()
        => Assert.Equal("", HtmlMinifier.Minify(""));
}
