using System.Text.Json;
using Netdocs.Core.Configuration;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers converting an mkdocs.yml into the Netdocs appsettings.json schema.</summary>
public class MkDocsImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-import-" + Guid.NewGuid().ToString("N"));

    public MkDocsImporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string WriteYaml(string yaml)
    {
        var path = Path.Combine(_dir, "mkdocs.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private JsonElement Convert(string yaml)
    {
        var json = MkDocsImporter.ConvertToJson(WriteYaml(yaml));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void MapsCoreMetadata()
    {
        var root = Convert("""
            site_name: My Docs
            site_url: https://example.com/docs/
            repo_url: https://github.com/me/repo
            """);

        var netdocs = root.GetProperty("Netdocs");
        Assert.Equal("My Docs", netdocs.GetProperty("siteName").GetString());
        Assert.Equal("https://example.com/docs/", netdocs.GetProperty("siteUrl").GetString());
        Assert.Equal("https://github.com/me/repo", netdocs.GetProperty("repoUrl").GetString());
    }

    [Fact]
    public void MapsNestedNav()
    {
        var root = Convert("""
            site_name: X
            nav:
              - Home: index.md
              - Guide:
                  - Intro: guide/intro.md
            """);

        var nav = root.GetProperty("Netdocs").GetProperty("nav");
        Assert.Equal("index.md", nav[0].GetProperty("path").GetString());
        var guide = nav[1];
        Assert.Equal("Guide", guide.GetProperty("title").GetString());
        Assert.Equal("guide/intro.md", guide.GetProperty("children")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void MapsPluginsAndExtensionsWithOptions()
    {
        var root = Convert("""
            site_name: X
            plugins:
              - search
              - tags:
                  export: true
            markdown_extensions:
              - admonition
              - toc:
                  permalink: true
            """);

        var netdocs = root.GetProperty("Netdocs");
        var plugins = netdocs.GetProperty("plugins");
        Assert.Equal("search", plugins[0].GetProperty("name").GetString());
        Assert.Equal("tags", plugins[1].GetProperty("name").GetString());
        Assert.True(plugins[1].GetProperty("options").GetProperty("export").GetBoolean());

        var exts = netdocs.GetProperty("markdownExtensions");
        Assert.Equal("admonition", exts[0].GetProperty("name").GetString());
        Assert.True(exts[1].GetProperty("options").GetProperty("permalink").GetBoolean());
    }

    [Fact]
    public void ImportedConfigLoadsBackViaJsonConfigLoader()
    {
        var json = MkDocsImporter.ConvertToJson(WriteYaml("""
            site_name: Round Trip
            theme:
              name: material
              features: [navigation.tabs]
            nav:
              - Home: index.md
            plugins:
              - search
            """));
        var outPath = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(outPath, json);

        var config = JsonConfigLoader.Load(outPath);
        Assert.Equal("Round Trip", config.SiteName);
        Assert.Contains("navigation.tabs", config.Theme.Features);
        Assert.Contains(config.Plugins, p => p.Name == "search");
        Assert.Single(config.Nav);
    }
}
