using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Sets per-page created/last-updated dates from git history (with a filesystem
/// fallback). Uses a single reverse-history walk to collect first/last commit dates
/// for every tracked path, so cost is one traversal regardless of page count.
/// </summary>
public sealed class GitRevisionDatePlugin : IPlugin, IBuildHook
{
    private bool _enableCreationDate;
    private ILogger _log = null!;

    public string Name => "git-revision-date-localized";

    public void Configure(IPluginContext ctx)
    {
        _log = ctx.Logger;
        if (ctx.PluginOptions.TryGetValue("enable_creation_date", out var c) && c is bool b)
            _enableCreationDate = b;
    }

    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        // Skip the git-history walk during `serve` to keep incremental rebuilds fast.
        if (site.Options.IsServe)
        {
            ApplyFilesystem(site);
            return Task.CompletedTask;
        }

        var repoPath = Repository.Discover(site.Config.ProjectRoot);
        if (repoPath is null)
        {
            _log.LogDebug("No git repository found; using filesystem timestamps");
            ApplyFilesystem(site);
            return Task.CompletedTask;
        }

        try
        {
            var (created, updated) = CollectDates(repoPath, ct);
            var workDir = new Repository(repoPath).Info.WorkingDirectory;
            var matched = 0;
            foreach (var page in site.Pages)
            {
                if (page.IsGenerated || !File.Exists(page.SourcePath)) continue;
                var rel = Path.GetRelativePath(workDir, page.SourcePath).Replace('\\', '/');
                if (updated.TryGetValue(rel, out var u)) { page.Updated = u; matched++; }
                else page.Updated ??= File.GetLastWriteTimeUtc(page.SourcePath);
                if (_enableCreationDate && created.TryGetValue(rel, out var cr)) page.Created ??= cr;
            }
            _log.LogDebug("git-revision-date: resolved dates for {Matched} pages", matched);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "git-revision-date failed; using filesystem timestamps");
            ApplyFilesystem(site);
        }
        return Task.CompletedTask;
    }

    private static (Dictionary<string, DateTimeOffset> Created, Dictionary<string, DateTimeOffset> Updated)
        CollectDates(string repoPath, CancellationToken ct)
    {
        var created = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var updated = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        using var repo = new Repository(repoPath);
        var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            ct.ThrowIfCancellationRequested();
            var when = commit.Author.When;
            var parent = commit.Parents.FirstOrDefault();
            using var changes = repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree);
            foreach (var change in changes)
            {
                var path = change.Path;
                updated.TryAdd(path, when); // first seen (newest commit) = last modified
                created[path] = when;        // last write wins (oldest commit) = created
            }
        }
        return (created, updated);
    }

    private static void ApplyFilesystem(SiteContext site)
    {
        foreach (var page in site.Pages)
        {
            if (page.IsGenerated || !File.Exists(page.SourcePath)) continue;
            page.Updated ??= File.GetLastWriteTimeUtc(page.SourcePath);
            page.Created ??= File.GetCreationTimeUtc(page.SourcePath);
        }
    }
}
