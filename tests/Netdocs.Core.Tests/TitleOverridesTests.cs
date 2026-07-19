using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the page_title / nav_title / tag_title front-matter overrides and their
/// fallback to the standard title.</summary>
public class TitleOverridesTests
{
    [Fact]
    public void ComputedTitles_FallBackToStandardTitle_WhenOverridesUnset()
    {
        var page = new Page { SourcePath = "a", RelativePath = "x.md", Title = "Standard" };

        Assert.Equal("Standard", page.DisplayTitle);
        Assert.Equal("Standard", page.NavigationTitle);
        Assert.Equal("Standard", page.TagListingTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ComputedTitles_FallBackToStandardTitle_WhenOverridesBlank(string blank)
    {
        var page = new Page
        {
            SourcePath = "a",
            RelativePath = "x.md",
            Title = "Standard",
            PageTitle = blank,
            NavTitle = blank,
            TagTitle = blank,
        };

        Assert.Equal("Standard", page.DisplayTitle);
        Assert.Equal("Standard", page.NavigationTitle);
        Assert.Equal("Standard", page.TagListingTitle);
    }

    [Fact]
    public void ComputedTitles_UseOverrides_WhenSet()
    {
        var page = new Page
        {
            SourcePath = "a",
            RelativePath = "x.md",
            Title = "Standard",
            PageTitle = "Page Display",
            NavTitle = "Nav Label",
            TagTitle = "Tag Label",
        };

        Assert.Equal("Page Display", page.DisplayTitle);
        Assert.Equal("Nav Label", page.NavigationTitle);
        Assert.Equal("Tag Label", page.TagListingTitle);
    }

    [Fact]
    public void Navigation_UsesNavTitle_ForLeafPage()
    {
        var page = new Page
        {
            SourcePath = "a",
            RelativePath = "reference/code.md",
            Url = "reference/code/",
            Title = "The Very Long Reference Title",
            NavTitle = "Code",
        };
        var config = new SiteConfig { Nav = [new NavItem { Path = "reference/code.md" }] };

        var nodes = NavigationBuilder.Build(config, [page]);

        Assert.Equal("Code", nodes[0].Title);
    }

    [Fact]
    public void Navigation_ConfigNavTitle_WinsOverFrontMatterNavTitle()
    {
        var page = new Page
        {
            SourcePath = "a",
            RelativePath = "reference/code.md",
            Url = "reference/code/",
            Title = "Standard",
            NavTitle = "FrontMatterNav",
        };
        var config = new SiteConfig { Nav = [new NavItem { Title = "ConfigNav", Path = "reference/code.md" }] };

        var nodes = NavigationBuilder.Build(config, [page]);

        Assert.Equal("ConfigNav", nodes[0].Title);
    }
}
