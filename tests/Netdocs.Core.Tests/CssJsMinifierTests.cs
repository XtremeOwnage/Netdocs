using Netdocs.Core.Optimization;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the conservative CSS/JS minifier used by the optimize toggles.</summary>
public class CssJsMinifierTests
{
    [Fact]
    public void MinifyCss_StripsCommentsAndCollapsesWhitespace()
    {
        var css = """
            /* header */
            body {
                color: red;
                margin: 0;
            }
            """;
        var min = CssJsMinifier.MinifyCss(css);

        Assert.DoesNotContain("/*", min);
        Assert.DoesNotContain("\n", min);
        Assert.Contains("body{color:red;margin:0}", min);
    }

    [Fact]
    public void MinifyCss_PreservesStringContents()
    {
        var css = "a::before { content: \"  spaced  value  \"; }";
        var min = CssJsMinifier.MinifyCss(css);
        Assert.Contains("\"  spaced  value  \"", min);
    }

    [Fact]
    public void MinifyJs_StripsCommentsAndCollapsesWhitespace()
    {
        var js = """
            // greet
            function hi() {
                /* block */
                return    1;
            }
            """;
        var min = CssJsMinifier.MinifyJs(js);

        Assert.DoesNotContain("//", min);
        Assert.DoesNotContain("/*", min);
        Assert.DoesNotContain("return    1", min);
        Assert.Contains("return 1;", min);
    }

    [Fact]
    public void MinifyJs_DoesNotStripUrlInsideString()
    {
        var js = "const u = \"https://example.com/path\"; // trailing";
        var min = CssJsMinifier.MinifyJs(js);
        Assert.Contains("\"https://example.com/path\"", min);
        Assert.DoesNotContain("// trailing", min);
    }

    [Fact]
    public void Minify_HandlesEmptyInput()
    {
        Assert.Equal("", CssJsMinifier.MinifyCss(""));
        Assert.Equal("", CssJsMinifier.MinifyJs(""));
    }
}
