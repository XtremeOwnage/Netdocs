using System.Text;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Netdocs.Abstractions;

namespace Netdocs.Core.Markdown;

/// <summary>Renders processed markdown to HTML and extracts TOC, title, and plain text.</summary>
public sealed class DocumentRenderer(MarkdownPipeline pipeline, IReadOnlyDictionary<string, string>? linkMap = null)
{
    private readonly HtmlParser _htmlParser = new();

    public void Render(Page page)
    {
        var document = Markdig.Markdown.Parse(page.ProcessedMarkdown, pipeline);

        if (linkMap is not null)
            RewriteMarkdownLinks(document, page.RelativePath);

        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            var renderer = new HtmlRenderer(writer);
            pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();
        }
        page.HtmlContent = sb.ToString();

        page.Toc = BuildToc(document);

        if (string.IsNullOrEmpty(page.Title))
            page.Title = ExtractTitle(document) ?? DeriveTitleFromPath(page.RelativePath);

        page.PlainText = ExtractPlainText(page.HtmlContent);
    }

    /// <summary>Rewrites relative <c>*.md</c> links in content to their output URLs (mkdocs-style).</summary>
    private void RewriteMarkdownLinks(MarkdownDocument document, string currentRelativePath)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage || string.IsNullOrEmpty(link.Url)) continue;
            var resolved = ResolveMarkdownLink(currentRelativePath, link.Url, linkMap!);
            if (resolved is not null) link.Url = resolved;
        }
    }

    internal static string? ResolveMarkdownLink(string currentRelativePath, string url, IReadOnlyDictionary<string, string> map)
    {
        if (url.Contains("://") || url.StartsWith('/') || url.StartsWith('#') ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = url.IndexOf('#');
        var anchor = hash >= 0 ? url[hash..] : "";
        var path = hash >= 0 ? url[..hash] : url;

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            return null;

        var currentDir = Path.GetDirectoryName(currentRelativePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        var combined = currentDir.Length == 0 ? path : currentDir + "/" + path;
        var normalized = NormalizeRelative(combined);

        return map.TryGetValue(normalized, out var targetUrl) ? "/" + targetUrl + anchor : null;
    }

    private static string NormalizeRelative(string path)
    {
        var parts = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(segment);
        }
        return string.Join('/', parts);
    }

    private static IReadOnlyList<TocEntry> BuildToc(MarkdownDocument document)
    {
        var flat = new List<TocEntry>();
        foreach (var block in document.Descendants<HeadingBlock>())
        {
            if (block.Level is < 1 or > 3) continue;
            var id = block.GetAttributes().Id;
            if (string.IsNullOrEmpty(id)) continue;
            flat.Add(new TocEntry { Level = block.Level, Id = id, Title = InlineText(block.Inline) });
        }
        return Nest(flat);
    }

    private static IReadOnlyList<TocEntry> Nest(List<TocEntry> flat)
    {
        var roots = new List<TocEntry>();
        var stack = new Stack<(TocEntry Entry, List<TocEntry> Children)>();
        foreach (var entry in flat)
        {
            var children = new List<TocEntry>();
            var node = new TocEntry { Level = entry.Level, Id = entry.Id, Title = entry.Title, Children = children };
            while (stack.Count > 0 && stack.Peek().Entry.Level >= entry.Level) stack.Pop();
            if (stack.Count == 0) roots.Add(node);
            else stack.Peek().Children.Add(node);
            stack.Push((node, children));
        }
        return roots;
    }

    private static string? ExtractTitle(MarkdownDocument document)
    {
        foreach (var heading in document.Descendants<HeadingBlock>())
            if (heading.Level == 1)
                return InlineText(heading.Inline);
        return null;
    }

    private static string DeriveTitleFromPath(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        if (name.Equals("index", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(relativePath);
            name = string.IsNullOrEmpty(dir) ? "Home" : Path.GetFileName(dir);
        }
        return name.Replace('-', ' ').Replace('_', ' ');
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return "";
        var sb = new StringBuilder();
        foreach (var descendant in inline.Descendants())
        {
            switch (descendant)
            {
                case LiteralInline literal: sb.Append(literal.Content.AsSpan()); break;
                case CodeInline code: sb.Append(code.Content); break;
            }
        }
        return sb.ToString().Trim();
    }

    private string ExtractPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var doc = _htmlParser.ParseDocument($"<body>{html}</body>");
        return doc.Body?.TextContent.Trim() ?? "";
    }
}
