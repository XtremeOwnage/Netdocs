using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Netdocs.Abstractions;

/// <summary>Global build settings and environment flags.</summary>
public sealed class BuildOptions
{
    public bool IsProduction { get; init; }
    public bool IsServe { get; init; }
    public bool Strict { get; init; }
    public bool Clean { get; init; }
    public bool NoCache { get; init; }

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

    /// <summary>
    /// Absolute paths of every file this build intends to have in the output directory.
    /// Every writer (pages, assets, plugin outputs) must register its files here via
    /// <see cref="TrackOutput"/> so the build can prune stale files without wiping the
    /// whole directory — that is what preserves unchanged files (and their timestamps)
    /// for incremental publishing.
    /// </summary>
    public ConcurrentDictionary<string, byte> WrittenOutputs { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a produced output file so it survives stale-file pruning.</summary>
    public void TrackOutput(string fullPath) =>
        WrittenOutputs[Path.GetFullPath(fullPath)] = 0;
}

/// <summary>A resolved navigation node pointing at a real page or a section of nodes.</summary>
public sealed class NavNode
{
    public required string Title { get; init; }
    public Page? Page { get; init; }
    public string? Url => Page?.Url;
    public string? Icon { get; set; }
    public IReadOnlyList<NavNode> Children { get; set; } = [];
    public bool IsSection => Page is null;

    /// <summary>For a section, the index page it links to (navigation.indexes), if any.</summary>
    public Page? SectionIndex { get; set; }
}
