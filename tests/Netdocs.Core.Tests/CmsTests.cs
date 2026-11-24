using System.Text.Json.Nodes;
using Markdig;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Cms;
using Netdocs.Core.Markdown;
using Xunit;

namespace Netdocs.Core.Tests;

public class CmsFrontMatterTests
{
    private const string Sample = """
        ---
        # The page title
        title: Hello
        tags:
          - one
          - two
        date: 2024-01-02
        ---

        # Body

        Text.
        """;

    [Fact]
    public void Unchanged_values_round_trip_byte_for_byte()
    {
        var editor = FrontMatterEditor.Parse(Sample);
        editor.ReplaceAll(editor.Values);
        Assert.Equal(Sample, editor.ToString());
    }

    [Fact]
    public void Changing_one_key_leaves_the_others_alone()
    {
        var editor = FrontMatterEditor.Parse(Sample);
        editor.Set("tags", new List<object?> { "one", "three" });
        var text = editor.ToString();
        Assert.Contains("# The page title\ntitle: Hello\n", text);
        Assert.Contains("tags:\n  - one\n  - three\n", text);
        Assert.Contains("date: 2024-01-02\n", text);
        Assert.EndsWith("# Body\n\nText.", text);
    }

    [Fact]
    public void Adds_and_removes_keys()
    {
        var editor = FrontMatterEditor.Parse(Sample);
        editor.Remove("date");
        editor.Set("description", "A: colon");
        var text = editor.ToString();
        Assert.DoesNotContain("date:", text);
        Assert.Contains("description: \"A: colon\"\n", text);
    }

    [Fact]
    public void Creates_front_matter_for_a_page_without_one()
    {
        var editor = FrontMatterEditor.Parse("# Title\n");
        Assert.False(editor.HadFrontMatter);
        editor.Set("title", "Title");
        Assert.Equal("---\ntitle: Title\n---\n\n# Title\n", editor.ToString());
    }

    [Fact]
    public void Quotes_strings_that_would_change_type()
    {
        Assert.Equal("\"true\"", FrontMatterEditor.EmitScalar("true"));
        Assert.Equal("\"42\"", FrontMatterEditor.EmitScalar("42"));
        Assert.Equal("42", FrontMatterEditor.EmitScalar(42L));
        Assert.Equal("plain text", FrontMatterEditor.EmitScalar("plain text"));
    }

    [Fact]
    public void Keeps_crlf_line_endings()
    {
        var crlf = Sample.Replace("\n", "\r\n");
        var editor = FrontMatterEditor.Parse(crlf);
        editor.Set("title", "Changed");
        var text = editor.ToString();
        Assert.Contains("title: Changed\r\n", text);
        Assert.DoesNotContain("\r\r", text);
    }
}

public class CmsJsonSpanEditorTests
{
    private const string Config = """
        {
          // a comment that must survive
          "Netdocs": {
            "siteName": "Site", // trailing comment
            "nav": [
              { "path": "index.md" }
            ],
            "theme": {}
          }
        }
        """;

    [Fact]
    public void Replaces_one_value_and_keeps_comments()
    {
        var updated = JsonSpanEditor.Set(Config, JsonValue.Create("New name"), "Netdocs", "siteName");
        Assert.Contains("// a comment that must survive", updated);
        Assert.Contains("\"siteName\": \"New name\", // trailing comment", updated);
        Assert.Contains("{ \"path\": \"index.md\" }", updated);
    }

    [Fact]
    public void Replaces_arrays_with_indented_json()
    {
        var nav = new JsonArray(new JsonObject { ["path"] = "a.md" }, new JsonObject { ["path"] = "b.md" });
        var updated = JsonSpanEditor.Set(Config, nav, "Netdocs", "nav");
        Assert.Equal("b.md", JsonSpanEditor.Get(updated, "Netdocs", "nav")![1]!["path"]!.GetValue<string>());
        Assert.Contains("// trailing comment", updated);
    }

    [Fact]
    public void Inserts_missing_keys_and_parents()
    {
        var updated = JsonSpanEditor.Set(Config, JsonValue.Create("overrides"), "Netdocs", "theme", "customDir");
        Assert.Equal("overrides", JsonSpanEditor.Get(updated, "Netdocs", "theme", "customDir")!.GetValue<string>());
        var deeper = JsonSpanEditor.Set(Config, JsonValue.Create(true), "Netdocs", "optimize", "minifyHtml");
        Assert.True(JsonSpanEditor.Get(deeper, "Netdocs", "optimize", "minifyHtml")!.GetValue<bool>());
        Assert.Equal("Site", JsonSpanEditor.Get(deeper, "Netdocs", "siteName")!.GetValue<string>());
    }

    [Fact]
    public void Removes_keys()
    {
        var updated = JsonSpanEditor.Set(Config, null, "Netdocs", "nav");
        Assert.Null(JsonSpanEditor.Get(updated, "Netdocs", "nav"));
        Assert.NotNull(JsonSpanEditor.Get(updated, "Netdocs", "theme"));
        Assert.Equal("Site", JsonSpanEditor.Get(updated, "Netdocs", "siteName")!.GetValue<string>());
    }
}

public class CmsMarkdownBlockTests
{
    private static MarkdownPipeline Pipeline()
    {
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        return MarkdownPipelineFactory.Build(site, []);
    }

    private const string Body = """
        # Title

        First paragraph
        continues here.

        !!! note "Heads up"

            Inside the note.

            Still inside.

        - item one
        - item two

        ```python
        print("x")

        print("y")
        ```

        === "Tab A"

            A content

        === "Tab B"

            B content

        Last line.

        """;

    [Fact]
    public void Splits_top_level_blocks_including_material_extensions()
    {
        var (_, blocks) = MarkdownBlocks.Split(Body, Pipeline());
        Assert.Equal(["heading", "paragraph", "admonition", "list", "code", "tabs", "paragraph"], blocks.Select(b => b.Kind));
        Assert.Equal("!!! note \"Heads up\"\n\n    Inside the note.\n\n    Still inside.", blocks[2].Source);
        Assert.Contains("print(\"y\")", blocks[4].Source);
    }

    [Fact]
    public void Keeping_every_block_reproduces_the_body()
    {
        var (_, blocks) = MarkdownBlocks.Split(Body, Pipeline());
        var edits = blocks.Select(b => new BlockEdit(b.Index, null)).ToList();
        Assert.Equal(Body, MarkdownBlocks.Apply(Body, Pipeline(), edits));
    }

    [Fact]
    public void Editing_one_block_changes_only_that_block()
    {
        var (_, blocks) = MarkdownBlocks.Split(Body, Pipeline());
        var edits = blocks.Select(b => b.Index == 1 ? new BlockEdit(null, "First paragraph, fixed.") : new BlockEdit(b.Index, null)).ToList();
        var result = MarkdownBlocks.Apply(Body, Pipeline(), edits);
        Assert.Equal(Body.Replace("First paragraph\ncontinues here.", "First paragraph, fixed."), result);
    }

    [Fact]
    public void Inserts_moves_and_deletes_blocks()
    {
        var (_, blocks) = MarkdownBlocks.Split(Body, Pipeline());
        var edits = new List<BlockEdit> { new(0, null), new(null, "New block."), new(6, null), new(3, null) };
        var result = MarkdownBlocks.Apply(Body, Pipeline(), edits);
        Assert.Equal("# Title\n\nNew block.\n\nLast line.\n\n- item one\n- item two\n", result);
    }
}

public class CmsLinkRewriterTests
{
    [Fact]
    public void Rewrites_inline_reference_and_html_links_but_not_code()
    {
        var md = "See [a](img/a.png \"t\") and ![b](img/a.png#x).\n\n[ref]: img/a.png\n\n<img src=\"img/a.png\">\n\n```\n[c](img/a.png)\n```\n";
        var result = MarkdownLinkRewriter.Rewrite(md, t => t == "img/a.png" ? "img/a.webp" : null);
        Assert.Contains("[a](img/a.webp \"t\")", result);
        Assert.Contains("![b](img/a.webp#x)", result);
        Assert.Contains("[ref]: img/a.webp", result);
        Assert.Contains("<img src=\"img/a.webp\">", result);
        Assert.Contains("[c](img/a.png)", result);
    }

    [Fact]
    public void Leaves_external_links_alone()
    {
        var md = "[x](https://example.com/a.png) [y](/abs.png) [z](#anchor)";
        Assert.Equal(md, MarkdownLinkRewriter.Rewrite(md, _ => "changed"));
    }

    [Theory]
    [InlineData("blog/post.md", "../img/a.png", "img/a.png")]
    [InlineData("index.md", "img/a.png", "img/a.png")]
    [InlineData("a/b/c.md", "../../x.md", "x.md")]
    public void Resolves_relative_targets(string from, string target, string expected) =>
        Assert.Equal(expected, MarkdownLinkRewriter.Resolve(from, target));

    [Theory]
    [InlineData("blog/post.md", "img/a.png", "../img/a.png")]
    [InlineData("blog/post.md", "blog/img/a.png", "img/a.png")]
    [InlineData("index.md", "blog/a.md", "blog/a.md")]
    public void Builds_relative_links(string from, string target, string expected) =>
        Assert.Equal(expected, MarkdownLinkRewriter.Relative(from, target));
}

public class CmsSiteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-cms-" + Guid.NewGuid().ToString("N"));
    private readonly CmsWorkspace _workspace;
    private readonly ContentStore _content;
    private readonly NavService _nav;

    public CmsSiteTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs", "blog", "img"));
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), """
            {
              // keep me
              "Netdocs": {
                "siteName": "Test",
                "nav": [
                  { "path": "index.md" },
                  { "title": "Blog", "children": [ { "path": "blog/post.md" } ] }
                ]
              }
            }
            """);
        File.WriteAllText(Path.Combine(_root, "docs", "index.md"), "---\ntags:\n  - Docker\n  - docker\n---\n\n# Home\n\nRead [the post](blog/post.md).\n");
        File.WriteAllText(Path.Combine(_root, "docs", "blog", "post.md"), "---\ntitle: Post\ntags: [Dockers, kubernetes]\n---\n\n![pic](img/pic.png)\n\nBack [home](../index.md).\n");
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(40, 20))
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, Path.Combine(_root, "docs", "blog", "img", "pic.png"));

        var configPath = Path.Combine(_root, "appsettings.json");
        _workspace = new CmsWorkspace(configPath, Configuration.JsonConfigLoader.Load(configPath));
        _nav = new NavService(_workspace);
        _content = new ContentStore(_workspace, _nav);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void Lists_pages_with_metadata()
    {
        var pages = _content.List();
        Assert.Equal(["blog/post.md", "index.md"], pages.Select(p => p.Path));
        Assert.Equal("Post", pages[0].Title);
        Assert.Equal("Home", pages[1].Title);
        Assert.All(pages, p => Assert.True(p.InNav));
    }

    [Fact]
    public void Refuses_paths_outside_the_docs_folder()
    {
        Assert.Throws<CmsException>(() => _content.Read("../appsettings.json"));
        Assert.Throws<CmsException>(() => _workspace.ResolveDocsPath("../../etc/passwd"));
    }

    [Fact]
    public void Save_refuses_a_stale_hash()
    {
        var doc = _content.Read("index.md");
        File.AppendAllText(_workspace.ResolveDocsPath("index.md"), "\nchanged elsewhere\n");
        Assert.Throws<CmsConflictException>(() => _content.Save("index.md", doc.Hash, null, "new body"));
    }

    [Fact]
    public void Moving_a_page_updates_nav_and_links_both_ways()
    {
        var result = _content.Move("blog/post.md", "articles/2024/post.md");
        Assert.True(result.NavUpdated);
        Assert.Equal(1, result.UpdatedPages);
        Assert.Contains("[the post](articles/2024/post.md)", File.ReadAllText(_workspace.ResolveDocsPath("index.md")));
        var moved = File.ReadAllText(_workspace.ResolveDocsPath("articles/2024/post.md"));
        Assert.Contains("![pic](../../blog/img/pic.png)", moved);
        Assert.Contains("[home](../../index.md)", moved);
        var config = File.ReadAllText(_workspace.ConfigPath);
        Assert.Contains("articles/2024/post.md", config);
        Assert.Contains("// keep me", config);
    }

    [Fact]
    public void Tag_tools_find_duplicates_and_merge_them()
    {
        var taxonomy = new TaxonomyService(_workspace, _content);
        var (similar, _) = taxonomy.Report("tags");
        Assert.Contains(similar, s => s.Names.Contains("Docker") && s.Names.Contains("docker") && s.Names.Contains("Dockers"));

        var changed = taxonomy.Rename("tags", ["docker", "Dockers"], "Docker");
        Assert.Equal(2, changed);
        var tags = taxonomy.List("tags");
        Assert.Equal(2, tags.Single(t => t.Name == "Docker").Count);
        Assert.Contains("tags:\n  - Docker\n---", File.ReadAllText(_workspace.ResolveDocsPath("index.md")));
    }

    [Fact]
    public async Task Converting_to_webp_rewrites_references_and_removes_the_original()
    {
        var media = new MediaService(_workspace, _content);
        Assert.Equal(1, media.List().Single().References);
        var results = await media.ConvertToWebpAsync(["blog/img/pic.png"], 80, null, CancellationToken.None);
        var r = Assert.Single(results);
        Assert.Null(r.Error);
        Assert.Equal("blog/img/pic.webp", r.To);
        Assert.False(File.Exists(_workspace.ResolveDocsPath("blog/img/pic.png")));
        Assert.Contains("![pic](img/pic.webp)", File.ReadAllText(_workspace.ResolveDocsPath("blog/post.md")));
    }

    [Fact]
    public void Deleting_referenced_media_needs_force()
    {
        var media = new MediaService(_workspace, _content);
        Assert.Throws<CmsConflictException>(() => media.Delete("blog/img/pic.png", force: false));
        media.Delete("blog/img/pic.png", force: true);
        Assert.Empty(media.List());
    }

    [Fact]
    public void Template_override_sets_up_custom_dir()
    {
        var templates = new TemplateService(_workspace);
        templates.Save("partials/footer.html", "<footer>{{ config.site_name }}</footer>");
        Assert.Equal("overrides", _workspace.Config.Theme.CustomDir);
        Assert.True(File.Exists(Path.Combine(_root, "overrides", "partials", "footer.html")));
        Assert.Throws<CmsException>(() => templates.Save("partials/footer.html", "{{ if }"));
        templates.Revert("partials/footer.html");
        Assert.False(File.Exists(Path.Combine(_root, "overrides", "partials", "footer.html")));
    }

    [Fact]
    public void Settings_save_changes_only_edited_keys()
    {
        var templates = new TemplateService(_workspace);
        var settings = templates.ReadSettings();
        settings["siteName"] = "Renamed";
        ((JsonObject)settings["theme"]!)["features"] = new JsonArray("navigation.tabs");
        templates.SaveSettings(settings);
        Assert.Equal("Renamed", _workspace.Config.SiteName);
        Assert.Equal(["navigation.tabs"], _workspace.Config.Theme.Features);
        Assert.Contains("// keep me", File.ReadAllText(_workspace.ConfigPath));
    }
}

public class CmsJsonFormattingTests
{
    [Fact]
    public void Writes_nav_entries_in_the_compact_hand_written_style()
    {
        var nav = new JsonArray(
            new JsonObject { ["title"] = "Home", ["path"] = "index.md" },
            new JsonObject { ["title"] = "Guides", ["children"] = new JsonArray(new JsonObject { ["path"] = "a.md" }) });
        Assert.Equal("""
            [
              { "title": "Home", "path": "index.md" },
              {
                "title": "Guides",
                "children": [
                  { "path": "a.md" }
                ]
              }
            ]
            """, JsonSpanEditor.Serialize(nav, ""));
    }

    [Fact]
    public void Rewriting_an_unchanged_nav_leaves_the_file_identical()
    {
        const string config = """
            {
              "Netdocs": {
                "nav": [
                  { "title": "Home", "path": "index.md", "icon": "home" },
                  {
                    "title": "Setup",
                    "children": [
                      { "path": "setup.md" }
                    ]
                  }
                ]
              }
            }
            """;
        var nav = JsonSpanEditor.Get(config, "Netdocs", "nav")!.DeepClone();
        Assert.Equal(config, JsonSpanEditor.Set(config, nav, "Netdocs", "nav"));
    }
}

public class CmsIconCatalogTests
{
    [Theory]
    [InlineData("material-home", "material/home")]
    [InlineData("octicons-arrow-right-24", "octicons/arrow-right-24")]
    [InlineData("fontawesome-brands-github", "fontawesome/brands/github")]
    public void ShortcodesBecomeFrontMatterNames(string shortcode, string name) =>
        Assert.Equal(name, Netdocs.Core.Cms.IconCatalog.ToName(shortcode));

    [Fact]
    public void SearchPutsExactMaterialMatchFirst()
    {
        var hits = Netdocs.Core.Cms.IconCatalog.Search("home", 10);
        Assert.NotEmpty(hits);
        Assert.Equal("material/home", hits[0].Name);
        Assert.StartsWith("<svg", hits[0].Svg);
    }
}
