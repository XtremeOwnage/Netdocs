using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Works out where an imported page's "edit"/"view source" buttons should point.
///
/// <para>An imported page's <see cref="Page.RelativePath"/> is where it landed in <em>this</em>
/// site, so the site-wide <c>repoUrl</c>/<c>editUri</c> pattern would build a link into the wrong
/// repository, at a path that does not exist in it. This resolves the upstream location instead —
/// explicitly configured, or derived from the repository the file was cloned from — and yields
/// null when neither is possible, so the page renders no button at all.</para>
/// </summary>
internal sealed class SourceLinkBuilder(string repoUrl, string editUri)
{
    /// <summary>Links for one file, given its path relative to the source's docs directory.</summary>
    public SourceLinks For(string pathInSource)
    {
        var rel = pathInSource.Replace('\\', '/').TrimStart('/');
        var edit = $"{repoUrl}/{editUri}/{rel}";
        // Same derivation the site-wide links use: one edit_uri drives both actions.
        return new SourceLinks(edit, edit.Replace("/edit/", "/blob/"));
    }

    /// <summary>
    /// Resolves links for a git pull source. An explicit <c>repoUrl</c>/<c>editUri</c> wins;
    /// otherwise both are derived from the clone — the repository URL for the host, and the
    /// checked-out branch plus the source's docs path for the edit prefix. Returns null when the
    /// branch is unknown (a pinned tag or commit leaves HEAD detached) and nothing was configured.
    /// </summary>
    public static SourceLinkBuilder? ForPullSource(ImportedDocsPullSource source, string? branch, ILogger logger)
    {
        var repoUrl = source.RepoUrl ?? WebUrlFor(source.Repository);
        if (repoUrl is null)
        {
            logger.LogDebug("imported-docs: no web URL for {Repository}; source links disabled", source.Repository);
            return null;
        }

        var editUri = source.EditUri;
        if (editUri is null)
        {
            if (branch is null)
            {
                logger.LogInformation(
                    "imported-docs: {Repository} is not on a branch and sets no 'editUri', so its pages get no edit/view links.",
                    source.Repository);
                return null;
            }
            var docsPath = (source.SourcePath ?? "docs").Replace('\\', '/').Trim('/');
            editUri = docsPath.Length > 0 ? $"edit/{branch}/{docsPath}" : $"edit/{branch}";
        }

        return new SourceLinkBuilder(repoUrl.TrimEnd('/'), editUri.Trim('/'));
    }

    /// <summary>
    /// Resolves links for an S3 source. A bucket carries no repository information, so both
    /// <c>repoUrl</c> and <c>editUri</c> must be configured or there are no links to give.
    /// </summary>
    public static SourceLinkBuilder? ForS3Source(ImportedDocsS3Source source) =>
        string.IsNullOrWhiteSpace(source.RepoUrl) || string.IsNullOrWhiteSpace(source.EditUri)
            ? null
            : new SourceLinkBuilder(source.RepoUrl.TrimEnd('/'), source.EditUri.Trim('/'));

    /// <summary>
    /// Browsable URL for a clone URL: strips a trailing <c>.git</c>, and rewrites the scp-style
    /// SSH form (<c>git@host:org/repo</c>) that browsers cannot follow. Returns null for anything
    /// else — a local path, say — where guessing a host would be worse than showing no button.
    /// </summary>
    internal static string? WebUrlFor(string repository)
    {
        var repo = repository.Trim();
        if (repo.Length == 0) return null;

        if (repo.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            var at = repo.IndexOf('@');
            var colon = repo.IndexOf(':', at);
            if (colon < 0) return null;
            var host = repo[(at + 1)..colon];
            var path = repo[(colon + 1)..].TrimStart('/');
            repo = $"https://{host}/{path}";
        }
        else if (repo.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            repo = "https://" + repo["ssh://".Length..];
            var at = repo.IndexOf('@');
            if (at > "https://".Length) repo = "https://" + repo[(at + 1)..];
        }
        else if (!repo.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 && !repo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return repo.TrimEnd('/');
    }
}
