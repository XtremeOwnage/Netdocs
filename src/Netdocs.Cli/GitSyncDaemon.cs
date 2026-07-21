using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core;
using Netdocs.Core.Configuration;
using Netdocs.Core.Plugins;

namespace Netdocs.Cli;

/// <summary>
/// Long-running publish daemon (distinct from <c>serve</c>): polls a git remote for new commits
/// on a target branch and, when the branch advances, fast-forwards the working tree and rebuilds
/// the site in place.
///
/// <para>Rather than attempting a risky per-page partial update, every sync runs a full build.
/// That build is cheap because the incremental render cache reuses unchanged pages, and the
/// output writer only rewrites files whose contents actually changed — so a small content change
/// republishes a small diff, while any navigation/blog/tags structural change is still reflected
/// correctly across every page (the caveat called out in the design notes).</para>
/// </summary>
public sealed class GitSyncDaemon(
    string configPath,
    SiteConfig config,
    BuildOptions options,
    Func<PluginRegistry> registryFactory,
    ILoggerFactory loggerFactory,
    string remote,
    string? branch,
    int intervalSeconds)
{
    private readonly ILogger _log = loggerFactory.CreateLogger("watch");
    private readonly string _repo = config.ProjectRoot;

    public async Task<int> RunAsync(bool once, CancellationToken ct)
    {
        if (!IsGitRepo())
        {
            _log.LogError("'{Repo}' is not a git repository; the watch daemon needs a git remote to poll.", _repo);
            return 1;
        }

        var targetBranch = branch ?? Git("rev-parse", "--abbrev-ref", "HEAD").Output.Trim();
        if (string.IsNullOrEmpty(targetBranch) || targetBranch == "HEAD")
        {
            _log.LogError("Could not determine the branch to track. Pass --branch <name>.");
            return 1;
        }

        _log.LogInformation("Watching {Remote}/{Branch} in {Repo} (every {Interval}s). Ctrl+C to stop.",
            remote, targetBranch, _repo, intervalSeconds);

        // Publish the current tree once on startup so the output reflects HEAD.
        await RebuildAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await CheckAndSyncAsync(targetBranch, ct))
                    await RebuildAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Sync cycle failed; will retry.");
            }

            if (once) break;
            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
        return 0;
    }

    /// <summary>Fetches the remote and, if the tracked branch fast-forwarded past our HEAD,
    /// advances the working tree to it. Returns true when the tree changed (i.e. a rebuild is
    /// needed). Diverged or locally-ahead branches are left untouched so no local work is lost.</summary>
    internal async Task<bool> CheckAndSyncAsync(string targetBranch, CancellationToken ct)
    {
        var fetch = Git("fetch", "--quiet", remote, targetBranch);
        if (fetch.ExitCode != 0)
        {
            _log.LogWarning("git fetch failed: {Error}", fetch.Error.Trim());
            return false;
        }

        var local = Git("rev-parse", "HEAD").Output.Trim();
        var remoteRef = Git("rev-parse", $"{remote}/{targetBranch}").Output.Trim();
        if (string.IsNullOrEmpty(remoteRef) || local == remoteRef)
            return false;

        // Only sync when the remote strictly advanced past our HEAD (a fast-forward). If the
        // branch has diverged or we are ahead, leave it alone rather than discarding local commits.
        if (Git("merge-base", "--is-ancestor", local, remoteRef).ExitCode != 0)
        {
            _log.LogWarning("Local branch has diverged from {Remote}/{Branch} ({Local} vs {Remote2}); skipping auto-sync to avoid losing local commits.",
                remote, targetBranch, Short(local), Short(remoteRef));
            return false;
        }

        var ahead = Git("rev-list", "--count", $"{local}..{remoteRef}").Output.Trim();
        _log.LogInformation("{Remote}/{Branch} advanced by {Count} commit(s): {From} -> {To}",
            remote, targetBranch, ahead, Short(local), Short(remoteRef));

        var merge = Git("merge", "--ff-only", remoteRef);
        if (merge.ExitCode != 0)
        {
            _log.LogWarning("git merge --ff-only failed: {Error}", merge.Error.Trim());
            return false;
        }
        await Task.CompletedTask;
        return true;
    }

    private async Task RebuildAsync(CancellationToken ct)
    {
        try
        {
            var freshConfig = JsonConfigLoader.Load(configPath);
            var engine = new BuildEngine(freshConfig, options, registryFactory(), loggerFactory);
            await engine.BuildAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rebuild failed; keeping the previous published output.");
        }
    }

    private bool IsGitRepo() => Git("rev-parse", "--is-inside-work-tree").ExitCode == 0;

    private static string Short(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    private (int ExitCode, string Output, string Error) Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _repo,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(_repo);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, output, error);
    }
}
