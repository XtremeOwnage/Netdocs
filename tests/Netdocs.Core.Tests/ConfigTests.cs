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
}
