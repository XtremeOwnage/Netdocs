namespace Netdocs.Abstractions;

/// <summary>Parsed, engine-facing site configuration (from mkdocs.yml).</summary>
public sealed class SiteConfig
{
    public string SiteName { get; set; } = "Documentation";
    public string? SiteUrl { get; set; }
    public string? SiteAuthor { get; set; }
    public string? SiteDescription { get; set; }
    public string? Copyright { get; set; }
    public string? RepoUrl { get; set; }
    public string? RepoName { get; set; }

    public string DocsDir { get; set; } = "docs";
    public string SiteDir { get; set; } = "site";

    public ThemeConfig Theme { get; set; } = new();

    /// <summary>Ordered navigation as authored in mkdocs.yml.</summary>
    public IReadOnlyList<NavItem> Nav { get; set; } = [];

    /// <summary>markdown_extensions list with their raw option maps.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> MarkdownExtensions { get; set; }
        = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

    /// <summary>plugins list with their raw option maps (order preserved).</summary>
    public IReadOnlyList<PluginConfig> Plugins { get; set; } = [];

    public IReadOnlyList<string> ExtraCss { get; set; } = [];
    public IReadOnlyList<string> ExtraJavaScript { get; set; } = [];

    /// <summary>Glob patterns (docs-relative) to exclude from page discovery.</summary>
    public IReadOnlyList<string> Exclude { get; set; } = [];

    /// <summary>extra: block, kept as a raw tree for templates.</summary>
    public IReadOnlyDictionary<string, object?> Extra { get; set; } = new Dictionary<string, object?>();

    /// <summary>Controls how titles/ids are turned into URL slugs (blog, categories, authors, tags).</summary>
    public SlugifyConfig Slugify { get; set; } = new();

    /// <summary>Optional post-build deployment target (filesystem copy or git branch publish).</summary>
    public DeployConfig Deploy { get; set; } = new();

    /// <summary>Output optimization toggles (HTML minification, etc.).</summary>
    public OptimizeConfig Optimize { get; set; } = new();

    /// <summary>Absolute path to the project root (folder containing mkdocs.yml).</summary>
    public string ProjectRoot { get; set; } = "";

    public string AbsoluteDocsDir => Path.Combine(ProjectRoot, DocsDir);
    public string AbsoluteSiteDir => Path.Combine(ProjectRoot, SiteDir);
}

public sealed class ThemeConfig
{
    public string Name { get; set; } = "material";
    public string Language { get; set; } = "en";
    public string? Logo { get; set; }
    public string? Favicon { get; set; }
    public string? CustomDir { get; set; }
    public IReadOnlyList<PaletteConfig> Palette { get; set; } = [];
    public IReadOnlyList<string> Features { get; set; } = [];
    public IReadOnlyDictionary<string, object?> Font { get; set; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> Icon { get; set; } = new Dictionary<string, object?>();
}

public sealed class PaletteConfig
{
    public string? Media { get; set; }
    public string? Scheme { get; set; }
    public string? Primary { get; set; }
    public string? Accent { get; set; }
}

public sealed class PluginConfig
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, object?> Options { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
/// Configurable slugify behavior for generated URLs (blog posts, categories, authors, tags).
/// Mirrors the common knobs of mkdocs/pymdownx slugify without requiring a Python callable.
/// </summary>
public sealed class SlugifyConfig
{
    /// <summary>Casing applied to letters: <c>lower</c> (default), <c>upper</c>, or <c>none</c>.</summary>
    public string Case { get; set; } = "lower";

    /// <summary>Word separator inserted between tokens (default <c>-</c>).</summary>
    public string Separator { get; set; } = "-";

    /// <summary>When true, drop non-ASCII letters/digits entirely instead of keeping them.</summary>
    public bool Ascii { get; set; }
}

/// <summary>
/// Post-build deployment. <see cref="Target"/> selects the publish mechanism:
/// <c>none</c> (default), <c>filesystem</c> (copy output to <see cref="Path"/>), or
/// <c>git</c> (commit and push the output to <see cref="Branch"/> on <see cref="Remote"/>).
/// </summary>
public sealed class DeployConfig
{
    /// <summary><c>none</c> | <c>filesystem</c> | <c>git</c>.</summary>
    public string Target { get; set; } = "none";

    /// <summary>Destination directory for the <c>filesystem</c> target.</summary>
    public string? Path { get; set; }

    /// <summary>When true, delete files at the destination that the build did not produce.</summary>
    public bool Clean { get; set; } = true;

    /// <summary>Branch to publish for the <c>git</c> target (default <c>gh-pages</c>).</summary>
    public string Branch { get; set; } = "gh-pages";

    /// <summary>Remote to push to for the <c>git</c> target (default <c>origin</c>).</summary>
    public string Remote { get; set; } = "origin";

    /// <summary>Commit message template for the <c>git</c> target.</summary>
    public string Message { get; set; } = "Deploy docs";

    /// <summary>When true (default), push the branch to the remote after committing.</summary>
    public bool Push { get; set; } = true;
}

/// <summary>Output optimization toggles applied while writing rendered pages.</summary>
public sealed class OptimizeConfig
{
    /// <summary>Collapse insignificant whitespace/comments in emitted HTML.</summary>
    public bool MinifyHtml { get; set; }

    /// <summary>Collapse whitespace/comments in emitted CSS assets.</summary>
    public bool MinifyCss { get; set; }

    /// <summary>Collapse whitespace/comments in emitted JavaScript assets.</summary>
    public bool MinifyJs { get; set; }
}

/// <summary>An authored navigation entry: either a link to a page or a titled section.</summary>
public sealed class NavItem
{
    public string? Title { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<NavItem> Children { get; init; } = [];
    public bool IsSection => Children.Count > 0 || Path is null;
}
