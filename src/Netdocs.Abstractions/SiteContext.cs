using Microsoft.Extensions.Logging;

namespace Netdocs.Abstractions;

/// <summary>Global build settings and environment flags.</summary>
public sealed class BuildOptions
{
    public bool IsProduction { get; init; }
    public bool IsServe { get; init; }
    public bool Strict { get; init; }
    public bool Clean { get; init; }

    public IReadOnlyDictionary<string, string?> Environment { get; init; }
        = new Dictionary<string, string?>();

    public string? GetEnv(string key) =>
        Environment.TryGetValue(key, out var v) ? v : System.Environment.GetEnvironmentVariable(key);
}

/// <summary>Site-wide context available to plugins during a build.</summary>
public sealed class SiteContext
{
    public required SiteConfig Config { get; init; }
    public required BuildOptions Options { get; init; }
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>All pages (source + generated), in build order. Mutable during generation.</summary>
    public List<Page> Pages { get; } = [];

    /// <summary>Resolved navigation tree (built after content generation).</summary>
    public IReadOnlyList<NavNode> Navigation { get; set; } = [];

    /// <summary>Cross-plugin shared state (e.g. tag index, author map).</summary>
    public Dictionary<string, object?> State { get; } = [];
}

/// <summary>A resolved navigation node pointing at a real page or a section of nodes.</summary>
public sealed class NavNode
{
    public required string Title { get; init; }
    public Page? Page { get; init; }
    public string? Url => Page?.Url;
    public IReadOnlyList<NavNode> Children { get; set; } = [];
    public bool IsSection => Page is null;
}
