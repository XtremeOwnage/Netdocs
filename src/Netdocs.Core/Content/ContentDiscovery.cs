using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Content;

/// <summary>Walks the docs directory and produces <see cref="Page"/> instances.</summary>
public sealed class ContentDiscovery(SiteConfig config, ILogger<ContentDiscovery> logger)
{
    private static readonly string[] MarkdownExtensions = [".md", ".markdown"];

    public IReadOnlyList<Page> Discover()
    {
        var docsDir = config.AbsoluteDocsDir;
        if (!Directory.Exists(docsDir))
            throw new DirectoryNotFoundException($"docs_dir not found: {docsDir}");

        var ignore = IgnoreRules.Load(config.ProjectRoot, docsDir, config.Exclude);
        var pages = new List<Page>();

        foreach (var file in Directory.EnumerateFiles(docsDir, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!MarkdownExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;

            var relative = Path.GetRelativePath(docsDir, file).Replace('\\', '/');
            if (ignore.IsIgnored(relative))
            {
                logger.LogDebug("Ignoring {Relative}", relative);
                continue;
            }

            var page = LoadPage(file, relative);
            pages.Add(page);
        }

        logger.LogInformation("Discovered {Count} content pages", pages.Count);
        return pages;
    }

    private Page LoadPage(string absolutePath, string relative)
    {
        var raw = File.ReadAllText(absolutePath);
        var (meta, body) = FrontMatter.Split(raw);

        var url = UrlFor(relative);
        var page = new Page
        {
            SourcePath = absolutePath,
            RelativePath = relative,
            RawMarkdown = body,
            FrontMatter = meta,
            Url = url,
            OutputPath = Path.Combine(config.AbsoluteSiteDir, OutputFileFor(url)),
        };

        if (meta.TryGetValue("title", out var t) && t is string title)
            page.Title = title;

        if (meta.TryGetValue("page_title", out var pt) && pt is string pageTitle && pageTitle.Length > 0)
            page.PageTitle = pageTitle;
        if (meta.TryGetValue("nav_title", out var nt) && nt is string navTitle && navTitle.Length > 0)
            page.NavTitle = navTitle;
        if (meta.TryGetValue("tag_title", out var gt) && gt is string tagTitle && tagTitle.Length > 0)
            page.TagTitle = tagTitle;

        return page;
    }

    /// <summary>Directory-style URLs: foo/bar.md -> foo/bar/ ; index.md -> its dir.</summary>
    public static string UrlFor(string relative)
    {
        var withoutExt = relative[..^Path.GetExtension(relative).Length];
        var segments = withoutExt.Split('/');
        if (segments[^1].Equals("index", StringComparison.OrdinalIgnoreCase)
            || segments[^1].Equals("README", StringComparison.OrdinalIgnoreCase))
        {
            var dir = string.Join('/', segments[..^1]);
            return dir.Length == 0 ? "" : dir + "/";
        }
        return withoutExt + "/";
    }

    public static string OutputFileFor(string url)
    {
        var path = url.Length == 0 ? "index.html" : url.TrimEnd('/') + "/index.html";
        return path.Replace('/', Path.DirectorySeparatorChar);
    }
}
