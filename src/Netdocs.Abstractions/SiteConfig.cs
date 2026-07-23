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

    /// <summary>Path fragment appended to <see cref="RepoUrl"/> to build per-page "edit" links,
    /// e.g. <c>edit/main/docs/</c>. When null, edit/view action links are not emitted even if
    /// the <c>content.action.edit</c>/<c>content.action.view</c> theme features are enabled.</summary>
    public string? EditUri { get; set; }

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

    /// <summary>Behaviour of the abbreviations feature (<c>&lt;abbr&gt;</c> tooltips).</summary>
    public AbbreviationsConfig Abbreviations { get; set; } = new();

    /// <summary>Optional post-build deployment target (filesystem copy or git branch publish).</summary>
    public DeployConfig Deploy { get; set; } = new();

    /// <summary>Output optimization toggles (HTML minification, etc.).</summary>
    public OptimizeConfig Optimize { get; set; } = new();

    /// <summary>Optional build-time validation (internal links, anchors, orphaned files).</summary>
    public ValidationConfig Validation { get; set; } = new();

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
    /// <summary>Selects the client-side syntax-highlighting renderer for fenced code blocks.
    /// The Markdig fence parser (titles, line numbers, hl_lines, mermaid) always runs in core
    /// and emits neutral <c>&lt;pre&gt;&lt;code class="language-x"&gt;</c> HTML; this only chooses
    /// how that HTML is colourised in the browser. Built-in values: <c>highlightjs</c> (default,
    /// highlight.js via CDN with line numbers) and <c>none</c> (plain, un-highlighted blocks).
    /// Any other value is treated as <c>custom</c>: the theme injects no highlighter and you supply
    /// your own via <c>extra_javascript</c>/<c>extra_css</c> (e.g. Prism, Shiki).</summary>
    public string Highlight { get; set; } = "highlightjs";
    public IReadOnlyDictionary<string, object?> Font { get; set; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> Icon { get; set; } = new Dictionary<string, object?>();
}

public sealed class PaletteConfig
{
    public string? Media { get; set; }
    public string? Scheme { get; set; }
    public string? Primary { get; set; }
    public string? Accent { get; set; }

    /// <summary>Optional palette-switcher toggle (Material icon name + accessible label). When any
    /// palette declares a toggle, the theme renders a light/dark switcher in the header.</summary>
    public string? ToggleIcon { get; set; }
    public string? ToggleName { get; set; }
}

public sealed class PluginConfig
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, object?> Options { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Optional override for this plugin's markdown-preprocessor stage order (the built-in
    /// defaults are snippets=10, table-reader=20, abbreviations=20, macros=25). Lower runs
    /// earlier. When null, the plugin's natural <see cref="IMarkdownPreprocessor.Order"/> is used.
    /// Other hook types run in the order plugins are listed in config.
    /// </summary>
    public int? Order { get; init; }
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

/// <summary>Behaviour of the abbreviations feature that renders <c>*[TERM]: definition</c>
/// entries as <c>&lt;abbr&gt;</c> tooltips.</summary>
public sealed class AbbreviationsConfig
{
    /// <summary>When true (default), only the first occurrence of each abbreviation term on a page
    /// is wrapped in an <c>&lt;abbr&gt;</c> tooltip; later occurrences render as plain text. This
    /// avoids blanketing a page in dotted-underline placeholders when a term repeats many times.
    /// Set to false to restore the classic behaviour of marking every occurrence.</summary>
    public bool FirstInstanceOnly { get; set; } = true;
}

/// <summary>
/// Post-build deployment. <see cref="Target"/> selects the publish mechanism:
/// <c>none</c> (default), <c>filesystem</c> (copy output to <see cref="Path"/>),
/// <c>git</c> (commit and push the output to <see cref="Branch"/> on <see cref="Remote"/>),
/// or <c>s3</c> (sync output to <see cref="Bucket"/> via the AWS CLI).
/// </summary>
public sealed class DeployConfig
{
    /// <summary><c>none</c> | <c>filesystem</c> | <c>git</c> | <c>s3</c>.</summary>
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

    /// <summary>Target S3 bucket name for the <c>s3</c> target.</summary>
    public string? Bucket { get; set; }

    /// <summary>Optional key prefix (sub-path) within the bucket for the <c>s3</c> target.</summary>
    public string? Prefix { get; set; }

    /// <summary>Optional AWS region for the <c>s3</c> target (otherwise the AWS CLI default).</summary>
    public string? Region { get; set; }
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

    /// <summary>
    /// Generate a <c>.webp</c> sibling for each raster image (png/jpg) and wrap
    /// <c>&lt;img&gt;</c> tags in a <c>&lt;picture&gt;</c> so browsers prefer webp with the
    /// original as fallback. Non-destructive: originals are kept.
    /// </summary>
    public bool ConvertImagesToWebp { get; set; }

    /// <summary>Quality (1-100) for generated webp images. Default 80.</summary>
    public int WebpQuality { get; set; } = 80;

    /// <summary>
    /// Self-host external CDN assets for offline use. When enabled, the build downloads every
    /// external <c>&lt;script&gt;</c>/<c>&lt;link rel=stylesheet&gt;</c>/<c>&lt;img&gt;</c> asset
    /// (highlight.js, Mermaid, web fonts, Twemoji, …), stores them under
    /// <c>assets/external/</c>, and rewrites the pages to reference the local copies so the site
    /// works without internet (including from <c>file://</c>). Requires network access at build
    /// time; assets that fail to download keep their CDN URL and log a warning.
    /// </summary>
    public bool Offline { get; set; }
}

/// <summary>
/// Build-time validation. Every enabled check emits a <c>warning</c> per problem; combine with
/// <c>--strict</c> (or <c>MKDOCS_STRICT=1</c>) to turn those warnings into a non-zero build exit.
/// All checks are opt-in and default to <c>false</c>.
/// </summary>
public sealed class ValidationConfig
{
    /// <summary>Verify that internal links and asset references (<c>href</c>/<c>src</c>) in the
    /// rendered pages resolve to a file that exists in the output. External URLs, anchors, and
    /// <c>mailto:</c>/<c>tel:</c>/<c>data:</c> links are skipped.</summary>
    public bool Links { get; set; }

    /// <summary>Verify that <c>#fragment</c> anchors in internal links point at an element that
    /// actually has that <c>id</c> on the target page. Requires <see cref="Links"/>.</summary>
    public bool Anchors { get; set; }

    /// <summary>Warn about image assets under the docs directory that no rendered page references.</summary>
    public bool UnusedImages { get; set; }

    /// <summary>Warn about markdown pages that are not reachable from the navigation tree.</summary>
    public bool OrphanPages { get; set; }
}

/// <summary>An authored navigation entry: either a link to a page or a titled section.</summary>
public sealed class NavItem
{
    public string? Title { get; init; }
    public string? Path { get; init; }
    public string? Icon { get; init; }
    public IReadOnlyList<NavItem> Children { get; init; } = [];
    public bool IsSection => Children.Count > 0 || Path is null;
}
