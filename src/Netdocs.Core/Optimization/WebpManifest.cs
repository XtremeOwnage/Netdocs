namespace Netdocs.Core.Optimization;

/// <summary>
/// The set of <c>.webp</c> files a build actually produced, keyed by site-relative path with
/// forward slashes (e.g. <c>img/photo.webp</c>).
///
/// <para>The HTML rewrite consults this so a <c>&lt;source&gt;</c> is only offered for an image
/// that really exists in the output. Without it, any image ImageSharp could not decode (or that
/// was never copied) still advertised a <c>.webp</c> sibling, and every webp-capable browser —
/// i.e. all of them — fetched a 404 and rendered nothing.</para>
///
/// <para>Immutable once constructed: the asset pipeline builds it, then the parallel page render
/// reads it from many threads.</para>
/// </summary>
public sealed class WebpManifest(IEnumerable<string> siteRelativePaths)
{
    private readonly HashSet<string> _paths =
        new(siteRelativePaths.Select(Normalize), StringComparer.OrdinalIgnoreCase);

    /// <summary>No images were converted, so nothing is eligible for rewriting.</summary>
    public static WebpManifest Empty { get; } = new([]);

    public int Count => _paths.Count;

    /// <summary>True when this build emitted the given site-relative <c>.webp</c> file.</summary>
    public bool Contains(string siteRelativePath) => _paths.Contains(Normalize(siteRelativePath));

    private static string Normalize(string path)
    {
        var p = path.Replace('\\', '/').TrimStart('/');
        return p.StartsWith("./", StringComparison.Ordinal) ? p[2..] : p;
    }
}
