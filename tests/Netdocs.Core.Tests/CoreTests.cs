using Markdig;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Markdown;
using Xunit;

namespace Netdocs.Core.Tests;

public class UrlTests
{
    [Theory]
    [InlineData("index.md", "")]
    [InlineData("about.md", "about/")]
    [InlineData("blog/index.md", "blog/")]
    [InlineData("blog/posts/hello.md", "blog/posts/hello/")]
    [InlineData("guides/setup/install.md", "guides/setup/install/")]
    public void UrlFor_ProducesDirectoryUrls(string relative, string expected)
        => Assert.Equal(expected, ContentDiscovery.UrlFor(relative));

    [Theory]
    [InlineData("", "")]
    [InlineData("about/", "../")]
    [InlineData("blog/posts/hello/", "../../../")]
    [InlineData("404.html", "")]
    [InlineData("changelogs/2025/Q2/2025.05.05/", "../../../../")]
    [InlineData("releases/v1.2.3/", "../../")]
    [InlineData("a/b/c/d/e/2025.05.05/", "../../../../../../")]
    public void BaseUrl_ComputesRelativePrefix(string url, string expected)
        => Assert.Equal(expected, PageRenderer.BaseUrl(url));
}

public class FrontMatterTests
{
    [Fact]
    public void Split_WithCrlf_DoesNotLeakTailIntoBody()
    {
        var md = "---\r\ntitle: Hello\r\nimage: /assets/cover.webP\r\n---\r\n# Heading\r\n\r\nBody paragraph.\r\n";
        var (meta, body) = FrontMatter.Split(md);

        Assert.Equal("Hello", meta["title"]);
        Assert.DoesNotContain("webP", body);
        Assert.DoesNotContain("---", body);
        Assert.StartsWith("# Heading", body);
    }

    [Fact]
    public void Split_WithLf_ParsesBodyCleanly()
    {
        var md = "---\ntitle: T\n---\nBody\n";
        var (meta, body) = FrontMatter.Split(md);

        Assert.Equal("T", meta["title"]);
        Assert.Equal("Body\n", body);
    }

    [Fact]
    public void Split_NoFrontMatter_ReturnsWholeBody()
    {
        var md = "# Just content\n\nNo front matter.\n";
        var (meta, body) = FrontMatter.Split(md);

        Assert.Empty(meta);
        Assert.Equal(md, body);
    }

    [Fact]
    public void Split_UnterminatedFrontMatter_ReturnsWholeBody()
    {
        var md = "---\ntitle: T\nno closing delimiter\n";
        var (_, body) = FrontMatter.Split(md);

        Assert.Equal(md, body);
    }
}

public class MarkdownTests
{
    private static string Render(string markdown)
    {
        var site = new SiteContext
        {
            Config = new SiteConfig(),
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        var pipeline = MarkdownPipelineFactory.Build(site, []);
        var page = new Page { SourcePath = "x", RelativePath = "x.md", ProcessedMarkdown = markdown };
        new DocumentRenderer(pipeline).Render(page);
        return page.HtmlContent;
    }

    private static string RenderWith(string markdown, params Netdocs.Abstractions.IMarkdigContributor[] contributors)
    {
        var site = new SiteContext
        {
            Config = new SiteConfig(),
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
        var pipeline = MarkdownPipelineFactory.Build(site, contributors);
        var page = new Page { SourcePath = "x", RelativePath = "x.md", ProcessedMarkdown = markdown };
        new DocumentRenderer(pipeline).Render(page);
        return page.HtmlContent;
    }

    [Fact]
    public void ExplicitAttrListId_WithColonAndSlash_BecomesHeadingId()
    {
        // The tags plugin emits headings like "### Development/C# { #tag:development/c }" so anchor
        // links match MkDocs Material. Confirm Markdig's generic-attributes support keeps the ':'
        // and '/' verbatim in the id rather than letting the auto-identifier collapse them.
        var html = Render("### Development/C# { #tag:development/c }\n");
        Assert.Contains("id=\"tag:development/c\"", html);
    }

    [Fact]
    public void Typeset_ProducesSmartPunctuation()
    {
        var html = RenderWith("She said -- wait... \"really\"?\n", new Netdocs.Plugins.TypesetPlugin());
        Assert.Contains("&ndash;", html);   // en dash from --
        Assert.Contains("&hellip;", html);  // ellipsis from ...
        Assert.Contains("&ldquo;", html);   // opening curly quote
        Assert.Contains("&rdquo;", html);   // closing curly quote
    }

    [Fact]
    public void Emoji_RendersTwemojiImage()
    {
        var html = Render("Nice work :smile:\n");
        Assert.Contains("class=\"twemoji\"", html);
        Assert.Contains("1f604.svg", html);      // :smile: => U+1F604
        Assert.Contains("title=\":smile:\"", html);
    }

    [Fact]
    public void Icons_RegistryLoadsBundledSet()
    {
        // The embedded icon dataset should decompress and expose thousands of icons.
        Assert.True(Netdocs.Core.Markdown.Emoji.IconRegistry.Count > 5000);
        Assert.NotNull(Netdocs.Core.Markdown.Emoji.IconRegistry.Get("material-home"));
        var svg = Netdocs.Core.Markdown.Emoji.IconRegistry.RenderSvg("material-home");
        Assert.NotNull(svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("<path", svg);
    }

    [Fact]
    public void Icons_MaterialShortcodeRendersInlineSvg()
    {
        var html = Render("Launch :material-home: now\n");
        Assert.Contains("class=\"twemoji", html);
        Assert.Contains("<svg", html);
        Assert.DoesNotContain(":material-home:", html); // shortcode fully resolved
    }

    [Fact]
    public void Icons_ShortcodeCarriesAttrListClasses()
    {
        var html = Render("Big :material-home:{ .lg .middle } icon\n");
        Assert.Contains("<svg", html);
        Assert.Contains("lg", html);
        Assert.Contains("middle", html);
    }

    [Fact]
    public void Emoji_ZwjSequenceKeepsVariationSelector()
    {
        // Family/zwj sequences keep FE0F; simple emoji strip it.
        Assert.Equal("1f604", Netdocs.Core.Markdown.Emoji.TwemojiRenderer.ToFileName("\U0001F604"));
        Assert.Equal("2764-fe0f-200d-1f525",
            Netdocs.Core.Markdown.Emoji.TwemojiRenderer.ToFileName("\u2764\ufe0f\u200d\U0001F525"));
    }

    [Fact]
    public void Keys_RendersKbdMarkup()
    {
        var html = Render("Press ++ctrl+alt+del++ now\n");
        Assert.Contains("<span class=\"keys\">", html);
        Assert.Contains("<kbd class=\"key-control\">Ctrl</kbd>", html);
        Assert.Contains("<kbd class=\"key-alt\">Alt</kbd>", html);
        Assert.Contains("<kbd class=\"key-delete\">Del</kbd>", html);
        Assert.Contains("<span>+</span>", html);
    }

    [Fact]
    public void Critic_RendersInsDelMarkSub()
    {
        Assert.Contains("<ins class=\"critic\">added</ins>", Render("{++added++}\n"));
        Assert.Contains("<del class=\"critic\">gone</del>", Render("{--gone--}\n"));
        Assert.Contains("<mark class=\"critic\">hi</mark>", Render("{==hi==}\n"));
        Assert.Contains("<span class=\"critic comment\">note</span>", Render("{>>note<<}\n"));
        var sub = Render("{~~old~>new~~}\n");
        Assert.Contains("<del class=\"critic\">old</del>", sub);
        Assert.Contains("<ins class=\"critic\">new</ins>", sub);
    }

    [Fact]
    public void Admonition_RendersMaterialMarkup()
    {
        var html = Render("!!! note \"Heads up\"\n    Body text here\n");
        Assert.Contains("class=\"admonition note\"", html);
        Assert.Contains("admonition-title\">Heads up", html);
        Assert.Contains("Body text here", html);
    }

    [Fact]
    public void CollapsibleAdmonition_RendersDetails()
    {
        var html = Render("???+ tip \"Open me\"\n    Hidden\n");
        Assert.Contains("<details class=\"admonition tip\" open>", html);
        Assert.Contains("<summary>Open me</summary>", html);
    }

    [Fact]
    public void ContentTabs_RenderTabbedSet()
    {
        var html = Render("=== \"A\"\n    Alpha\n\n=== \"B\"\n    Beta\n");
        Assert.Contains("tabbed-set", html);
        Assert.Contains("<label for=\"__tabbed_1_1\">A</label>", html);
        Assert.Contains("<label for=\"__tabbed_1_2\">B</label>", html);
    }

    [Fact]
    public void CodeFence_LinenumsAndHlLines_EmitDataAttributes()
    {
        var html = Render("```python linenums=\"5\" hl_lines=\"2 4-5\"\nprint(1)\n```\n");
        Assert.Contains("class=\"language-python\"", html);
        Assert.Contains("data-linenums-start=\"5\"", html);
        Assert.Contains("data-hl-lines=\"2 4-5\"", html);
    }

    [Fact]
    public void CodeFence_BraceFormWithTitle_EmitsFilename()
    {
        var html = Render("```{ .yaml .no-copy title=\"config.yml\" }\nkey: value\n```\n");
        Assert.Contains("class=\"language-yaml\"", html);
        Assert.Contains("<span class=\"filename\">config.yml</span>", html);
    }

    [Fact]
    public void CodeFence_MaxLines_EmitsCollapsibleWrapper()
    {
        var html = Render("```python max-lines=\"3\"\nprint(1)\n```\n");
        Assert.Contains("class=\"highlight nd-collapsible\"", html);
        Assert.Contains("data-max-lines=\"3\"", html);
    }

    [Fact]
    public void CodeFence_CollapseFlag_UsesDefaultMaxLines()
    {
        var html = Render("```python collapse\nprint(1)\n```\n");
        Assert.Contains("nd-collapsible", html);
        Assert.Contains("data-max-lines=\"10\"", html);
    }

    [Fact]
    public void CodeFence_NoCollapse_HasPlainHighlightWrapper()
    {
        var html = Render("```python\nprint(1)\n```\n");
        Assert.Contains("class=\"highlight\"", html);
        Assert.DoesNotContain("nd-collapsible", html);
    }

    [Fact]
    public void InlineHilite_ShebangProducesLanguageClass()
    {
        var html = Render("Use `#!python range(10)` here.\n");
        Assert.Contains("<code class=\"language-python\">range(10)</code>", html);
    }

    [Fact]
    public void InlineCode_WithoutShebang_RendersPlainCode()
    {
        var html = Render("Plain `value` code.\n");
        Assert.Contains("<code>value</code>", html);
    }

    [Fact]
    public void MermaidFence_RendersPreMermaid()
    {
        var html = Render("```mermaid\ngraph TD; A-->B;\n```\n");
        Assert.Contains("<pre class=\"mermaid\">", html);
        Assert.Contains("A-->B", html);
    }

    [Fact]
    public void BlockMath_RendersAsMathDiv_NotCodeBlock()
    {
        var html = Render("$$\n\\int_0^1 x\\,dx\n$$\n");
        Assert.Contains("<div class=\"math\">\\[", html);
        Assert.Contains("\\]</div>", html);
        Assert.DoesNotContain("language-math", html);
    }

    [Fact]
    public void InlineMath_RendersAsMathSpan()
    {
        var html = Render("Energy $E=mc^2$ here.");
        Assert.Contains("<span class=\"math\">\\(E=mc^2\\)</span>", html);
    }

    [Fact]
    public void Footnotes_RenderReferenceAndDefinition()
    {
        var html = Render("Claim.[^a]\n\n[^a]: Supporting detail.\n");
        Assert.Contains("footnote-ref", html);
        Assert.Contains("Supporting detail.", html);
    }

    [Fact]
    public void Title_ExtractedFromFirstHeading()
    {
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        var pipeline = MarkdownPipelineFactory.Build(site, []);
        var page = new Page { SourcePath = "x", RelativePath = "x.md", ProcessedMarkdown = "# Hello World\n\ntext" };
        new DocumentRenderer(pipeline).Render(page);
        Assert.Equal("Hello World", page.Title);
    }

    [Fact]
    public void MarkdownLinks_AreRewrittenRelativeToPage_ForBasePathSafety()
    {
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        var pipeline = MarkdownPipelineFactory.Build(site, []);
        var linkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["index.md"] = "",
            ["getting-started/index.md"] = "getting-started/",
            ["getting-started/quickstart.md"] = "getting-started/quickstart/",
            ["reference/cli.md"] = "reference/cli/",
        };

        // From the home page (root), links stay relative to root.
        var home = new Page { SourcePath = "index.md", RelativePath = "index.md", Url = "", ProcessedMarkdown = "See [start](getting-started/index.md)." };
        new DocumentRenderer(pipeline, linkMap).Render(home);
        Assert.Contains("href=\"getting-started/\"", home.HtmlContent);
        Assert.DoesNotContain("href=\"/getting-started/\"", home.HtmlContent);

        // From a nested page, links are prefixed with ../ back to the site root so they work
        // under a base path (e.g. GitHub project Pages served at /Repo/).
        var nested = new Page
        {
            SourcePath = "getting-started/index.md",
            RelativePath = "getting-started/index.md",
            Url = "getting-started/",
            ProcessedMarkdown = "See [cli](../reference/cli.md) and [qs](quickstart.md).",
        };
        new DocumentRenderer(pipeline, linkMap).Render(nested);
        Assert.Contains("href=\"../reference/cli/\"", nested.HtmlContent);
        Assert.Contains("href=\"../getting-started/quickstart/\"", nested.HtmlContent);
        Assert.DoesNotContain("href=\"/reference/cli/\"", nested.HtmlContent);
    }
}
