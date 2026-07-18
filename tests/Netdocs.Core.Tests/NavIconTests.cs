using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Templating;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers curated Material icon resolution and nav-icon wiring (config + front matter).</summary>
public class NavIconTests
{
    [Theory]
    [InlineData("code-braces")]
    [InlineData("material/code-braces")]
    [InlineData(":material-code-braces:")]
    [InlineData("octicons/code-braces")]
    public void MaterialIcons_ResolvesKnownName_IgnoringPrefixes(string name)
    {
        Assert.NotNull(MaterialIcons.Path(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("definitely-not-an-icon")]
    public void MaterialIcons_UnknownOrBlank_ReturnsNull(string name)
    {
        Assert.Null(MaterialIcons.Path(name));
    }

    [Fact]
    public void NavConfigIcon_IsCarriedToNode_ForLeafAndSection()
    {
        var page = new Page { SourcePath = "a", RelativePath = "reference/code.md", Url = "reference/code/" };
        var config = new SiteConfig
        {
            Nav =
            [
                new NavItem { Title = "Code", Path = "reference/code.md", Icon = "code-braces" },
                new NavItem { Title = "Setup", Icon = "rocket-launch", Children =
                    [ new NavItem { Title = "Other", Path = "reference/code.md" } ] },
            ],
        };

        var nodes = NavigationBuilder.Build(config, [page]);

        Assert.Equal("code-braces", nodes[0].Icon);
        Assert.Equal("rocket-launch", nodes[1].Icon);
    }

    [Fact]
    public void FrontMatterIcon_IsUsedWhenNavItemHasNone()
    {
        var page = new Page
        {
            SourcePath = "a",
            RelativePath = "reference/code.md",
            Url = "reference/code/",
            FrontMatter = new Dictionary<string, object?> { ["icon"] = "table" },
        };
        var config = new SiteConfig { Nav = [new NavItem { Title = "Code", Path = "reference/code.md" }] };

        var nodes = NavigationBuilder.Build(config, [page]);

        Assert.Equal("table", nodes[0].Icon);
    }

    [Fact]
    public void NavItemIcon_TakesPrecedenceOverFrontMatter()
    {
        var page = new Page
        {
            SourcePath = "a",
            RelativePath = "reference/code.md",
            Url = "reference/code/",
            FrontMatter = new Dictionary<string, object?> { ["icon"] = "table" },
        };
        var config = new SiteConfig { Nav = [new NavItem { Title = "Code", Path = "reference/code.md", Icon = "code-braces" }] };

        var nodes = NavigationBuilder.Build(config, [page]);

        Assert.Equal("code-braces", nodes[0].Icon);
    }
}
