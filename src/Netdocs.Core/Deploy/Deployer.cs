using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Core.Deploy;

/// <summary>
/// Publishes an already-built output directory to a deployment target
/// (<c>filesystem</c> copy, <c>git</c> branch, or <c>s3</c> bucket sync). Runs after a successful build.
/// </summary>
public sealed class Deployer(SiteConfig config, ILogger log)
{
    /// <summary>Deploys the built site according to <see cref="SiteConfig.Deploy"/>.</summary>
    public async Task<int> DeployAsync(CancellationToken ct = default)
    {
        var deploy = config.Deploy;
        var source = config.AbsoluteSiteDir;

        if (!Directory.Exists(source))
        {
            log.LogError("Nothing to deploy: output directory '{Dir}' does not exist. Run a build first.", source);
            return 1;
        }

        return deploy.Target.ToLowerInvariant() switch
        {
            "none" or "" => NothingToDo(),
            "filesystem" or "fs" or "local" => DeployToFilesystem(source, deploy),
            "git" or "git-branch" or "ghpages" or "gh-pages" => await DeployToGitAsync(source, deploy, ct),
            "s3" or "aws" or "aws-s3" => await DeployToS3Async(source, deploy, ct),
            _ => UnknownTarget(deploy.Target),
        };
    }

    private int NothingToDo()
    {
        log.LogInformation("Deploy target is 'none'; skipping deployment.");
        return 0;
    }

    private int UnknownTarget(string target)
    {
        log.LogError("Unknown deploy target '{Target}'. Use 'filesystem', 'git', or 's3'.", target);
        return 1;
    }

    private int DeployToFilesystem(string source, DeployConfig deploy)
    {
        if (string.IsNullOrWhiteSpace(deploy.Path))
        {
            log.LogError("Filesystem deploy requires 'deploy.path' to be set.");
            return 1;
        }

        var dest = Path.IsPathRooted(deploy.Path)
            ? deploy.Path
            : Path.GetFullPath(Path.Combine(config.ProjectRoot, deploy.Path));

        if (string.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                dest.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            log.LogError("Filesystem deploy destination is the same as the build output; choose a different path.");
            return 1;
        }

        Directory.CreateDirectory(dest);
        var written = CopyDirectory(source, dest);

        if (deploy.Clean)
        {
            var pruned = PruneExtraneous(source, dest);
            if (pruned > 0) log.LogInformation("Pruned {Count} stale file(s) from '{Dest}'", pruned, dest);
        }

        log.LogInformation("Deployed {Count} file(s) to '{Dest}'", written, dest);
        return 0;
    }

    private static int CopyDirectory(string source, string dest)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            count++;
        }
        return count;
    }

    private static int PruneExtraneous(string source, string dest)
    {
        var pruned = 0;
        foreach (var file in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dest, file);
            if (!File.Exists(Path.Combine(source, rel)))
            {
                File.Delete(file);
                pruned++;
            }
        }
        // Remove now-empty directories (deepest first).
        foreach (var dir in Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        return pruned;
    }

    private async Task<int> DeployToGitAsync(string source, DeployConfig deploy, CancellationToken ct)
    {
        var repoRoot = config.ProjectRoot;

        // Publish the built output onto a branch using a temporary worktree so the main
        // working tree is never disturbed. Requires git on PATH and a repo at ProjectRoot.
        if (await RunGitAsync(repoRoot, ct, "rev-parse", "--is-inside-work-tree") != 0)
        {
            log.LogError("Git deploy requires a git repository at '{Root}'.", repoRoot);
            return 1;
        }

        var worktree = Path.Combine(Path.GetTempPath(), "netdocs-deploy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var branchExists = await RunGitAsync(repoRoot, ct, "show-ref", "--verify", "--quiet", $"refs/heads/{deploy.Branch}") == 0;
            if (branchExists)
                await RunGitAsync(repoRoot, ct, "worktree", "add", "--force", worktree, deploy.Branch);
            else
                await RunGitAsync(repoRoot, ct, "worktree", "add", "--force", "--orphan", "-b", deploy.Branch, worktree);

            // Replace the worktree contents with the fresh build (keep .git metadata).
            foreach (var entry in Directory.EnumerateFileSystemEntries(worktree))
            {
                if (Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            CopyDirectory(source, worktree);

            await RunGitAsync(worktree, ct, "add", "-A");
            var msg = $"{deploy.Message} ({DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)";
            var commit = await RunGitAsync(worktree, ct, "commit", "-m", msg);
            if (commit != 0)
            {
                log.LogInformation("Git deploy: no changes to publish on '{Branch}'.", deploy.Branch);
                return 0;
            }

            if (deploy.Push)
            {
                if (await RunGitAsync(worktree, ct, "push", deploy.Remote, deploy.Branch) != 0)
                {
                    log.LogError("Git deploy: push to '{Remote}/{Branch}' failed.", deploy.Remote, deploy.Branch);
                    return 1;
                }
                log.LogInformation("Deployed to '{Remote}/{Branch}'.", deploy.Remote, deploy.Branch);
            }
            else
            {
                log.LogInformation("Committed build to local branch '{Branch}' (push disabled).", deploy.Branch);
            }
            return 0;
        }
        finally
        {
            await RunGitAsync(repoRoot, CancellationToken.None, "worktree", "remove", "--force", worktree);
            if (Directory.Exists(worktree))
                try { Directory.Delete(worktree, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task<int> DeployToS3Async(string source, DeployConfig deploy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deploy.Bucket))
        {
            log.LogError("S3 deploy requires 'deploy.bucket' to be set.");
            return 1;
        }

        // s3://bucket[/prefix]
        var prefix = (deploy.Prefix ?? "").Trim('/');
        var destination = prefix.Length > 0
            ? $"s3://{deploy.Bucket}/{prefix}"
            : $"s3://{deploy.Bucket}";

        var args = new List<string> { "s3", "sync", source, destination };
        if (deploy.Clean) args.Add("--delete");
        if (!string.IsNullOrWhiteSpace(deploy.Region)) { args.Add("--region"); args.Add(deploy.Region); }

        var exit = await RunProcessAsync("aws", source, ct, [.. args]);
        if (exit == 127 || exit == -1)
        {
            log.LogError("S3 deploy requires the AWS CLI ('aws') on PATH. Install it or use a different deploy target.");
            return 1;
        }
        if (exit != 0)
        {
            log.LogError("S3 deploy: 'aws s3 sync' to '{Dest}' failed (exit {Exit}).", destination, exit);
            return 1;
        }

        log.LogInformation("Deployed to '{Dest}'.", destination);
        return 0;
    }

    private async Task<int> RunGitAsync(string workingDir, CancellationToken ct, params string[] args)
        => await RunProcessAsync("git", workingDir, ct, args);

    private async Task<int> RunProcessAsync(string fileName, string workingDir, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Executable not found on PATH.
            log.LogTrace("{File} not found on PATH.", fileName);
            return 127;
        }
        if (proc is null) { log.LogError("Failed to start {File}.", fileName); return 1; }

        using (proc)
        {
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0 && stderr.Length > 0)
                log.LogTrace("{File} {Args}: {Err}", fileName, string.Join(' ', args), stderr.Trim());
            return proc.ExitCode;
        }
    }
}
