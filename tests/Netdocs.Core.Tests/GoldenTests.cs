using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Content;
using Netdocs.Core.Markdown;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Golden-file tests that lock in the HTML produced by the Markdown pipeline for a set of
/// representative inputs. Set the environment variable <c>UPDATE_GOLDEN=1</c> to (re)write
/// the expected <c>.html</c> files after an intentional change.
/// </summary>
public class GoldenTests
{
    private static string GoldenDir([CallerFilePath] string file = "")
        => Path.Combine(Path.GetDirectoryName(file)!, "golden");

    public static IEnumerable<object[]> Cases()
    {
        foreach (var md in Directory.EnumerateFiles(GoldenDir(), "*.md").OrderBy(f => f))
            yield return new object[] { Path.GetFileNameWithoutExtension(md) };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Markdown_MatchesGolden(string name)
    {
        var dir = GoldenDir();
        var markdown = File.ReadAllText(Path.Combine(dir, name + ".md"));
        var actual = Render(markdown).Replace("\r\n", "\n").TrimEnd() + "\n";

        var goldenPath = Path.Combine(dir, name + ".html");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1" || !File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actual);
            return;
        }

        var expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n").TrimEnd() + "\n";
        Assert.Equal(expected, actual);
    }

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
}
