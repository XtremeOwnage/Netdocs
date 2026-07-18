using Netdocs.Core.Configuration;
using Xunit;

namespace Netdocs.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void YamlTree_ResolvesEnvTagWithDefault()
    {
        var tree = YamlTree.Parse("value: !ENV [DOES_NOT_EXIST_VAR, fallback]").AsMap();
        Assert.Equal("fallback", tree.Get("value").AsString());
    }

    [Fact]
    public void YamlTree_ParsesScalarTypes()
    {
        var map = YamlTree.Parse("a: 1\nb: true\nc: hello\nd: 1.5").AsMap();
        Assert.Equal(1L, map.Get("a"));
        Assert.Equal(true, map.Get("b"));
        Assert.Equal("hello", map.Get("c"));
        Assert.Equal(1.5d, map.Get("d"));
    }

    [Fact]
    public void ConfigLoader_ReadsPluginsAndExtensionsInOrder()
    {
        var yaml = """
            site_name: Test Site
            theme:
              name: material
              features:
                - navigation.tabs
            plugins:
              - search
              - blog:
                  blog_dir: blog/
            markdown_extensions:
              - admonition
              - pymdownx.snippets:
                  base_path:
                    - .
            """;
        var path = Path.Combine(Path.GetTempPath(), $"mkdocs_{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, yaml);
        try
        {
            var config = ConfigLoader.Load(path);
            Assert.Equal("Test Site", config.SiteName);
            Assert.Contains("navigation.tabs", config.Theme.Features);
            Assert.Equal(["search", "blog"], config.Plugins.Select(p => p.Name));
            Assert.True(config.MarkdownExtensions.ContainsKey("pymdownx.snippets"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConfigLoader_ParsesPaletteToggle()
    {
        var yaml = """
            site_name: Test Site
            theme:
              name: material
              palette:
                - scheme: slate
                  primary: indigo
                  toggle:
                    icon: material/brightness-4
                    name: Switch to light mode
                - scheme: default
                  toggle:
                    icon: material/brightness-7
                    name: Switch to dark mode
            """;
        var path = Path.Combine(Path.GetTempPath(), $"mkdocs_{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, yaml);
        try
        {
            var config = ConfigLoader.Load(path);
            Assert.Equal(2, config.Theme.Palette.Count);
            Assert.Equal("slate", config.Theme.Palette[0].Scheme);
            Assert.Equal("material/brightness-4", config.Theme.Palette[0].ToggleIcon);
            Assert.Equal("Switch to light mode", config.Theme.Palette[0].ToggleName);
            Assert.Equal("material/brightness-7", config.Theme.Palette[1].ToggleIcon);
        }
        finally { File.Delete(path); }
    }
}

public class NavigationTests
{
    private static Netdocs.Abstractions.Page P(string rel, string url) =>
        new() { SourcePath = rel, RelativePath = rel, Url = url, Title = rel };

    [Fact]
    public void SectionIndex_PromotesIndexChildToSectionLanding()
    {
        var pages = new List<Netdocs.Abstractions.Page>
        {
            P("guide/index.md", "guide/"),
            P("guide/setup.md", "guide/setup/"),
        };
        var config = new Netdocs.Abstractions.SiteConfig
        {
            Nav = new List<Netdocs.Abstractions.NavItem>
            {
                new()
                {
                    Title = "Guide",
                    Children = new List<Netdocs.Abstractions.NavItem>
                    {
                        new() { Path = "guide/index.md" },
                        new() { Path = "guide/setup.md" },
                    },
                },
            },
        };

        var nav = Netdocs.Core.Content.NavigationBuilder.Build(config, pages);

        var section = Assert.Single(nav);
        Assert.True(section.IsSection);
        Assert.NotNull(section.SectionIndex);
        Assert.Equal("guide/", section.SectionIndex!.Url);
        Assert.Single(section.Children); // index child was promoted out of the list
        Assert.Equal("guide/setup/", section.Children[0].Url);
    }

    [Fact]
    public void Flatten_IncludesSectionIndexBeforeChildren()
    {
        var pages = new List<Netdocs.Abstractions.Page>
        {
            P("guide/index.md", "guide/"),
            P("guide/setup.md", "guide/setup/"),
        };
        var config = new Netdocs.Abstractions.SiteConfig
        {
            Nav = new List<Netdocs.Abstractions.NavItem>
            {
                new()
                {
                    Title = "Guide",
                    Children = new List<Netdocs.Abstractions.NavItem>
                    {
                        new() { Path = "guide/index.md" },
                        new() { Path = "guide/setup.md" },
                    },
                },
            },
        };

        var nav = Netdocs.Core.Content.NavigationBuilder.Build(config, pages);
        var flat = Netdocs.Core.Content.NavigationBuilder.Flatten(nav);

        Assert.Equal(new[] { "guide/", "guide/setup/" }, flat.ConvertAll(p => p.Url));
    }

    [Fact]
    public void AutoNav_BuildsHierarchyFromDirectories()
    {
        // No authored nav -> auto-nav should nest by folder, not dump everything flat.
        var pages = new List<Netdocs.Abstractions.Page>
        {
            P("index.md", ""),
            P("aws/index.md", "aws/"),
            P("aws/iam/roles.md", "aws/iam/roles/"),
            P("aws/iam/index.md", "aws/iam/"),
            P("account-management/provisioning.md", "account-management/provisioning/"),
        };
        var config = new Netdocs.Abstractions.SiteConfig();

        var nav = Netdocs.Core.Content.NavigationBuilder.Build(config, pages);

        // Home (root index) first, then sections ordered alphabetically by folder name.
        Assert.Equal("index.md", nav[0].Title); // P() uses rel path as title; root index is a link
        Assert.False(nav[0].IsSection);

        var accountMgmt = Assert.Single(nav, n => n.Title == "Account Management");
        Assert.True(accountMgmt.IsSection);

        var aws = Assert.Single(nav, n => n.Title == "Aws");
        Assert.True(aws.IsSection);
        Assert.NotNull(aws.SectionIndex);
        Assert.Equal("aws/", aws.SectionIndex!.Url);

        // aws has a nested "Iam" section with its own index promoted to the landing.
        var iam = Assert.Single(aws.Children, n => n.Title == "Iam");
        Assert.True(iam.IsSection);
        Assert.Equal("aws/iam/", iam.SectionIndex!.Url);
        Assert.Single(iam.Children); // roles.md, index promoted out
        Assert.Equal("aws/iam/roles/", iam.Children[0].Url);
    }
}
