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

    /// <summary>extra: block, kept as a raw tree for templates.</summary>
    public IReadOnlyDictionary<string, object?> Extra { get; set; } = new Dictionary<string, object?>();

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

/// <summary>An authored navigation entry: either a link to a page or a titled section.</summary>
public sealed class NavItem
{
    public string? Title { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<NavItem> Children { get; init; } = [];
    public bool IsSection => Children.Count > 0 || Path is null;
}
