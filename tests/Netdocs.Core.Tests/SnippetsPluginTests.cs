using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers the pymdownx.snippets preprocessor: includes, named sections, auto_append.</summary>
public class SnippetsPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netdocs-snip-" + Guid.NewGuid().ToString("N"));

    public SnippetsPluginTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static SiteContext Site() => new()
    {
        Config = new SiteConfig(),
        Options = new BuildOptions(),
        LoggerFactory = NullLoggerFactory.Instance,
    };

    private static readonly Page Page = new() { SourcePath = "x", RelativePath = "index.md", Url = "" };

    private SnippetsPlugin PluginWith(params (string key, object? value)[] options)
    {
        var plugin = new SnippetsPlugin();
        var dict = new Dictionary<string, object?>();
        foreach (var (k, v) in options) dict[k] = v;
        plugin.Configure(new FakeContext(dict) { Config = new SiteConfig { ProjectRoot = _dir } });
        return plugin;
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content.Replace("\r\n", "\n"));

    [Fact]
    public async Task IncludesWholeFile()
    {
        Write("notice.md", "Shared notice text.");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var result = await plugin.ProcessAsync(Page, "--8<-- \"notice.md\"", Site(), default);

        Assert.Contains("Shared notice text.", result);
    }

    [Fact]
    public async Task IncludesWholeFile_WithCrlfSourceLine()
    {
        // Windows-authored markdown uses CRLF line endings; the block-include directive must
        // still resolve when the line ends with \r\n rather than \n.
        Write("notice.md", "Shared notice text.");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var markdown = "Intro\r\n--8<-- \"notice.md\"\r\nOutro\r\n";
        var result = await plugin.ProcessAsync(Page, markdown, Site(), default);

        Assert.Contains("Shared notice text.", result);
        Assert.DoesNotContain("--8<--", result);
    }

    [Fact]
    public async Task ExtractsNamedSectionOnly()
    {
        Write("sample.py", "before\n# --8<-- [start:setup]\nimport os\n# --8<-- [end:setup]\nafter\n");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var result = await plugin.ProcessAsync(Page, "--8<-- \"sample.py:setup\"", Site(), default);

        Assert.Contains("import os", result);
        Assert.DoesNotContain("before", result);
        Assert.DoesNotContain("after", result);
        Assert.DoesNotContain("--8<--", result);
    }

    [Fact]
    public async Task MissingSnippetEmitsComment()
    {
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var result = await plugin.ProcessAsync(Page, "--8<-- \"nope.md\"", Site(), default);

        Assert.Contains("snippet not found", result);
    }

    [Fact]
    public async Task AutoAppendAddsFileToEveryPage()
    {
        Write("abbr.md", "*[HTML]: HyperText Markup Language");
        var plugin = PluginWith(
            ("base_path", new object?[] { _dir }),
            ("auto_append", new object?[] { "abbr.md" }));

        var result = await plugin.ProcessAsync(Page, "Body content.", Site(), default);

        Assert.Contains("Body content.", result);
        Assert.Contains("HyperText Markup Language", result);
    }

    [Fact]
    public async Task InlineParameterizedIncludeSubstitutesPlaceholders()
    {
        Write("ebay.html", "<a href=\"${url}\" class=\"aff\">${text}</a>");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var input = "| --8<-- \"ebay.html\" text=\"Cool Part\" url=\"https://ebay.us/x\" | 1 | $5 |";
        var result = await plugin.ProcessAsync(Page, input, Site(), default);

        Assert.Contains("<a href=\"https://ebay.us/x\" class=\"aff\">Cool Part</a>", result);
        // Stays inline within the table row (no injected newlines).
        Assert.DoesNotContain("--8<--", result);
        Assert.Contains("| 1 | $5 |", result);
    }

    [Fact]
    public async Task InlineParameterizedIncludeHtmlEscapesValues()
    {
        Write("box.html", "<span title=\"${label}\">x</span>");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var result = await plugin.ProcessAsync(Page, "--8<-- \"box.html\" label=\"A & B <c>\"", Site(), default);

        Assert.Contains("A &amp; B &lt;c&gt;", result);
        Assert.DoesNotContain("A & B <c>", result);
    }

    [Fact]
    public async Task ResolvesFromDefaultRootSnippetsDirectory()
    {
        var snippetsDir = Path.Combine(_dir, "snippets");
        Directory.CreateDirectory(snippetsDir);
        File.WriteAllText(Path.Combine(snippetsDir, "note.md"), "Default snippets dir works.");
        // No base_path configured at all — should still find <root>/snippets/note.md.
        var plugin = PluginWith();

        var result = await plugin.ProcessAsync(Page, "--8<-- \"note.md\"", Site(), default);

        Assert.Contains("Default snippets dir works.", result);
    }

    [Fact]
    public async Task PreservesIndentation_ForIndentedInclude()
    {
        // A `--8<--` indented inside a code fence within an admonition must keep that
        // indentation on every included line. Otherwise the fence closes empty and the
        // file spills out as top-level markdown (script comments become <h1> headers).
        Write("script.sh", "#!/usr/bin/env bash\n# a comment\nset -euo pipefail\necho hi");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var markdown = "    --8<-- \"script.sh\"";
        var result = await plugin.ProcessAsync(Page, markdown, Site(), default);

        // Every non-blank included line is prefixed with the marker's 4-space indent.
        Assert.Contains("    #!/usr/bin/env bash", result);
        Assert.Contains("    # a comment", result);
        Assert.Contains("    set -euo pipefail", result);
        Assert.Contains("    echo hi", result);
        Assert.DoesNotContain("--8<--", result);
    }

    [Fact]
    public async Task PreservesIndentation_BlankLinesNotIndented()
    {
        // Blank lines in the included file must not gain trailing whitespace.
        Write("script.sh", "line one\n\nline two");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var result = await plugin.ProcessAsync(Page, "    --8<-- \"script.sh\"", Site(), default);

        Assert.Contains("    line one", result);
        Assert.Contains("    line two", result);
        // The blank line stays blank (no "    \n" with trailing spaces).
        Assert.DoesNotContain("    \n", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task NoIndentation_UnindentedIncludeUnchanged()
    {
        // An include at column 0 must not gain any indentation (regression guard so the
        // reindent logic is a no-op for the common case).
        Write("notice.md", "line a\nline b");
        var plugin = PluginWith(("base_path", new object?[] { _dir }));

        var result = await plugin.ProcessAsync(Page, "--8<-- \"notice.md\"", Site(), default);

        Assert.Contains("line a\nline b", result.Replace("\r\n", "\n"));
        Assert.DoesNotContain("  line a", result);
    }

    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options) : IPluginContext
    {
        public SiteConfig Config { get; init; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }
}
