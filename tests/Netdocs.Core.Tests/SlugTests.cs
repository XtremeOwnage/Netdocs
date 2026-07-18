using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("under_score/slash", "under-score-slash")]
    public void Make_Default_LowercasesAndHyphenates(string input, string expected)
        => Assert.Equal(expected, Slug.Make(input));

    [Fact]
    public void Make_CustomSeparator_IsUsed()
        => Assert.Equal("hello_world", Slug.Make("Hello World", "_"));

    [Fact]
    public void Make_UpperCase_Config()
        => Assert.Equal("HELLO-WORLD", Slug.Make("Hello World", new SlugifyConfig { Case = "upper" }));

    [Fact]
    public void Make_PreserveCase_Config()
        => Assert.Equal("Hello-World", Slug.Make("Hello World", new SlugifyConfig { Case = "none" }));

    [Fact]
    public void Make_CustomSeparator_Config()
        => Assert.Equal("hello.world", Slug.Make("Hello World", new SlugifyConfig { Separator = "." }));

    [Fact]
    public void Make_AsciiOnly_DropsNonAscii()
        => Assert.Equal("nihao", Slug.Make("ni你好hao", new SlugifyConfig { Ascii = true }));
}
