namespace Netdocs.Abstractions;

/// <summary>A single content page flowing through the build pipeline.</summary>
public sealed class Page
{
    /// <summary>Absolute path to the source markdown file (empty for generated pages).</summary>
    public required string SourcePath { get; init; }

    /// <summary>Path relative to the docs dir, using forward slashes (e.g. "blog/post.md").</summary>
    public required string RelativePath { get; init; }

    /// <summary>Site-relative output URL with trailing slash (e.g. "blog/my-post/").</summary>
    public string Url { get; set; } = "";

    /// <summary>Absolute output file path (e.g. site/blog/my-post/index.html).</summary>
    public string OutputPath { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Raw markdown as read from disk.</summary>
    public string RawMarkdown { get; set; } = "";

    /// <summary>Markdown after preprocessors (snippets, abbreviations, macros).</summary>
    public string ProcessedMarkdown { get; set; } = "";

    /// <summary>Rendered inner HTML (content only, before templating).</summary>
    public string HtmlContent { get; set; } = "";

    /// <summary>Plain text of the page (HTML stripped) for search indexing.</summary>
    public string PlainText { get; set; } = "";

    public IReadOnlyDictionary<string, object?> FrontMatter { get; set; } = new Dictionary<string, object?>();

    public IReadOnlyList<TocEntry> Toc { get; set; } = [];

    /// <summary>Arbitrary per-page state shared between plugins.</summary>
    public Dictionary<string, object?> Meta { get; } = [];

    /// <summary>True when produced by an <see cref="IContentGenerator"/> rather than a source file.</summary>
    public bool IsGenerated { get; init; }

    public DateTimeOffset? Created { get; set; }
    public DateTimeOffset? Updated { get; set; }

    public override string ToString() => $"{RelativePath} -> {Url}";
}

public sealed class TocEntry
{
    public required int Level { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<TocEntry> Children { get; set; } = [];
}
