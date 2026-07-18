using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Validation;

/// <summary>A page's final rendered HTML, paired with its <see cref="Page"/>, for post-build checks.</summary>
public sealed record RenderedPage(Page Page, string Html);

/// <summary>
/// Optional post-build validation. Each enabled check emits one <c>warning</c> per problem so that
/// <c>--strict</c> (or <c>MKDOCS_STRICT</c>) can turn them into a failing build. Runs after all
/// pages, assets, and plugin outputs are on disk so every link target can be resolved for real.
/// </summary>
public static partial class BuildValidator
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".avif", ".bmp", ".ico"];

    public static void Validate(SiteContext site, IReadOnlyList<RenderedPage> pages, ILogger log)
    {
        var v = site.Config.Validation;
        if (!(v.Links || v.UnusedImages || v.OrphanPages)) return;

        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = 0;

        if (v.Links)
            problems += ValidateLinks(site, pages, v.Anchors, referencedFiles, log);
        else if (v.UnusedImages)
            CollectReferences(site, pages, referencedFiles); // still need references for the image check

        if (v.UnusedImages)
            problems += ValidateUnusedImages(site, referencedFiles, log);

        if (v.OrphanPages)
            problems += ValidateOrphanPages(site, log);

        if (problems == 0)
            log.LogInformation("Validation passed: no problems found.");
        else
            log.LogInformation("Validation found {Count} problem(s).", problems);
    }

    private static int ValidateLinks(SiteContext site, IReadOnlyList<RenderedPage> pages, bool checkAnchors,
        HashSet<string> referencedFiles, ILogger log)
    {
        var siteDir = Path.GetFullPath(site.Config.AbsoluteSiteDir);
        // Map output path -> set of element ids on that page (only built when anchors are checked).
        var pageIds = checkAnchors
            ? pages.ToDictionary(p => Path.GetFullPath(p.Page.OutputPath), p => ExtractIds(p.Html), StringComparer.OrdinalIgnoreCase)
            : null;

        var problems = 0;
        foreach (var rp in pages)
        {
            var pageDir = Path.GetDirectoryName(Path.GetFullPath(rp.Page.OutputPath)) ?? siteDir;
            foreach (Match m in LinkRegex().Matches(rp.Html))
            {
                var raw = m.Groups[1].Value.Trim();
                if (!IsInternal(raw)) continue;

                var (pathPart, fragment) = SplitFragment(raw);
                string targetFile;

                if (pathPart.Length == 0)
                {
                    // Same-page anchor (e.g. "#section").
                    targetFile = Path.GetFullPath(rp.Page.OutputPath);
                }
                else
                {
                    var resolved = ResolveTarget(pathPart, pageDir, siteDir);
                    if (resolved is null || !File.Exists(resolved))
                    {
                        log.LogWarning("Broken link in {Source}: '{Link}' does not resolve to an output file.",
                            SourceLabel(rp.Page), raw);
                        problems++;
                        continue;
                    }
                    targetFile = resolved;
                    referencedFiles.Add(targetFile);
                }

                if (checkAnchors && fragment.Length > 0 && pageIds is not null
                    && pageIds.TryGetValue(targetFile, out var ids) && !ids.Contains(fragment))
                {
                    log.LogWarning("Broken anchor in {Source}: '{Link}' — no element with id '{Id}' on the target page.",
                        SourceLabel(rp.Page), raw, fragment);
                    problems++;
                }
            }
        }
        return problems;
    }

    /// <summary>Populates <paramref name="referencedFiles"/> without emitting link warnings
    /// (used when only the unused-image check is enabled).</summary>
    private static void CollectReferences(SiteContext site, IReadOnlyList<RenderedPage> pages, HashSet<string> referencedFiles)
    {
        var siteDir = Path.GetFullPath(site.Config.AbsoluteSiteDir);
        foreach (var rp in pages)
        {
            var pageDir = Path.GetDirectoryName(Path.GetFullPath(rp.Page.OutputPath)) ?? siteDir;
            foreach (Match m in LinkRegex().Matches(rp.Html))
            {
                var raw = m.Groups[1].Value.Trim();
                if (!IsInternal(raw)) continue;
                var (pathPart, _) = SplitFragment(raw);
                if (pathPart.Length == 0) continue;
                var target = ResolveTarget(pathPart, pageDir, siteDir);
                if (target is not null && File.Exists(target)) referencedFiles.Add(target);
            }
        }
    }

    private static int ValidateUnusedImages(SiteContext site, HashSet<string> referencedFiles, ILogger log)
    {
        var docsDir = Path.GetFullPath(site.Config.AbsoluteDocsDir);
        var siteDir = Path.GetFullPath(site.Config.AbsoluteSiteDir);
        if (!Directory.Exists(docsDir)) return 0;

        var problems = 0;
        foreach (var img in Directory.EnumerateFiles(docsDir, "*", SearchOption.AllDirectories))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(img), StringComparer.OrdinalIgnoreCase)) continue;
            // Static docs assets are copied to the mirrored path under the site directory.
            var rel = Path.GetRelativePath(docsDir, img);
            var outputPath = Path.GetFullPath(Path.Combine(siteDir, rel));
            if (!referencedFiles.Contains(outputPath))
            {
                log.LogWarning("Unused image: '{Rel}' is not referenced by any page.", rel.Replace('\\', '/'));
                problems++;
            }
        }
        return problems;
    }

    private static int ValidateOrphanPages(SiteContext site, ILogger log)
    {
        var inNav = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectNavUrls(site.Navigation, inNav);

        var problems = 0;
        foreach (var page in site.Pages)
        {
            if (page.IsGenerated || string.IsNullOrEmpty(page.SourcePath)) continue;
            if (!inNav.Contains(page.Url))
            {
                log.LogWarning("Orphan page: '{Rel}' is not reachable from the navigation.",
                    page.RelativePath);
                problems++;
            }
        }
        return problems;
    }

    private static void CollectNavUrls(IReadOnlyList<NavNode> nodes, HashSet<string> into)
    {
        foreach (var n in nodes)
        {
            if (n.Url is { } u) into.Add(u);
            if (n.SectionIndex?.Url is { } su) into.Add(su);
            if (n.Children.Count > 0) CollectNavUrls(n.Children, into);
        }
    }

    /// <summary>Resolves a link's path portion to an absolute output file, appending
    /// <c>index.html</c> for directory-style URLs. Returns null if it escapes the site root.</summary>
    private static string? ResolveTarget(string pathPart, string pageDir, string siteDir)
    {
        var combined = pathPart.StartsWith('/')
            ? Path.Combine(siteDir, pathPart.TrimStart('/'))
            : Path.Combine(pageDir, pathPart);
        var full = Path.GetFullPath(combined);

        // Directory-style URL ("foo/" or "foo") -> foo/index.html.
        if (pathPart.EndsWith('/') || Path.GetExtension(full).Length == 0)
        {
            var indexed = Path.Combine(full, "index.html");
            if (File.Exists(indexed)) return Path.GetFullPath(indexed);
        }
        return full;
    }

    private static bool IsInternal(string raw)
    {
        if (raw.Length == 0) return false;
        if (raw.StartsWith("//")) return false;                 // protocol-relative external
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (raw.StartsWith("{{")) return false;                  // unresolved template token
        var colon = raw.IndexOf(':');
        var slash = raw.IndexOf('/');
        // A scheme is a leading "word:" before any slash (http:, https:, mailto:, tel:, ftp:, ...).
        if (colon > 0 && (slash < 0 || colon < slash)) return false;
        return true;
    }

    private static (string Path, string Fragment) SplitFragment(string raw)
    {
        var hash = raw.IndexOf('#');
        var path = hash >= 0 ? raw[..hash] : raw;
        var fragment = hash >= 0 ? raw[(hash + 1)..] : "";
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return (Uri.UnescapeDataString(path), Uri.UnescapeDataString(fragment));
    }

    private static HashSet<string> ExtractIds(string html)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in IdRegex().Matches(html)) ids.Add(m.Groups[1].Value);
        return ids;
    }

    private static string SourceLabel(Page page) =>
        string.IsNullOrEmpty(page.SourcePath) ? page.Url : page.RelativePath;

    [GeneratedRegex("(?:href|src)\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("id\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex IdRegex();
}
