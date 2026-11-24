using System.Diagnostics;
using System.Text;

namespace Netdocs.Core.Cms;

/// <summary>One changed file, from <c>git status</c>.</summary>
/// <param name="Index">Staged state letter (M, A, D, R, ?, or space).</param>
/// <param name="WorkTree">Unstaged state letter.</param>
public sealed record GitFileStatus(string Path, string Index, string WorkTree, string? OriginalPath);

public sealed record GitStatus(bool IsRepository, string? Branch, string? Upstream, int Ahead, int Behind, IReadOnlyList<GitFileStatus> Files);

public sealed record GitCommit(string Hash, string Author, string Date, string Subject);

public sealed record GitResult(bool Ok, string Output);

/// <summary>
/// Source control for the site folder, through the user's own <c>git</c> executable (so their
/// credentials, signing and hooks apply). Only commands with fixed argument shapes are exposed;
/// file paths are passed after <c>--</c> and must sit inside the project.
/// </summary>
public sealed class GitService(CmsWorkspace workspace)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private string? _repoRoot;

    /// <summary>The repository's top folder. Git reports paths relative to it, so every path the
    /// CMS sends and receives is repository-relative; operations stay scoped to the site folder.</summary>
    public async Task<string?> RepoRootAsync(CancellationToken ct)
    {
        if (_repoRoot is not null) return _repoRoot;
        var r = await RunInAsync(workspace.ProjectRoot, ["rev-parse", "--show-toplevel"], ct);
        if (!r.Ok) return null;
        _repoRoot = Path.GetFullPath(r.Output.Trim());
        return _repoRoot;
    }

    /// <summary>The site folder as a pathspec relative to the repository root.</summary>
    private async Task<string> ScopeAsync(CancellationToken ct)
    {
        var root = await RepoRootAsync(ct) ?? throw new CmsException("This site is not in a git repository.");
        var rel = Path.GetRelativePath(root, workspace.ProjectRoot).Replace('\\', '/');
        return rel is "." or "" ? "." : rel;
    }

    public async Task<GitStatus> StatusAsync(CancellationToken ct)
    {
        if (await RepoRootAsync(ct) is null) return new GitStatus(false, null, null, 0, 0, []);
        var result = await RunAsync(["status", "--porcelain=v1", "-b", "-z", "--untracked-files=all", "--", await ScopeAsync(ct)], ct);
        if (!result.Ok) throw new CmsException(result.Output.Trim());

        string? branch = null, upstream = null;
        int ahead = 0, behind = 0;
        var files = new List<GitFileStatus>();
        var entries = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.StartsWith("## ", StringComparison.Ordinal))
            {
                (branch, upstream, ahead, behind) = ParseBranch(entry[3..]);
                continue;
            }
            if (entry.Length < 4) continue;
            var x = entry[0].ToString();
            var y = entry[1].ToString();
            var path = entry[3..];
            string? original = null;
            if (x is "R" or "C") original = i + 1 < entries.Length ? entries[++i] : null;
            files.Add(new GitFileStatus(path, x, y, original));
        }
        return new GitStatus(true, branch, upstream, ahead, behind, files);
    }

    private static (string?, string?, int, int) ParseBranch(string header)
    {
        // e.g. "main...origin/main [ahead 1, behind 2]" or "No commits yet on main"
        if (header.StartsWith("No commits yet on ", StringComparison.Ordinal)) return (header[18..], null, 0, 0);
        var ahead = 0; var behind = 0;
        var bracket = header.IndexOf(" [", StringComparison.Ordinal);
        if (bracket >= 0)
        {
            foreach (var part in header[(bracket + 2)..].TrimEnd(']').Split(", "))
            {
                var bits = part.Split(' ');
                if (bits.Length == 2 && int.TryParse(bits[1], out var n))
                {
                    if (bits[0] == "ahead") ahead = n;
                    else if (bits[0] == "behind") behind = n;
                }
            }
            header = header[..bracket];
        }
        var dots = header.IndexOf("...", StringComparison.Ordinal);
        return dots >= 0 ? (header[..dots], header[(dots + 3)..], ahead, behind) : (header, null, ahead, behind);
    }

    public async Task<string> DiffAsync(string? path, bool staged, CancellationToken ct)
    {
        var args = new List<string> { "diff", "--no-color", "--no-ext-diff" };
        if (staged) args.Add("--cached");
        args.Add("--");
        args.Add(path is null ? await ScopeAsync(ct) : await SafePathAsync(path, ct));
        var result = await RunAsync(args, ct);
        if (!result.Ok) throw new CmsException(result.Output.Trim());
        if (result.Output.Length == 0 && path is not null && !staged)
        {
            // An untracked file has no diff against the index; show it as all-new.
            var full = Path.Combine(_repoRoot!, path);
            if (File.Exists(full) && new FileInfo(full).Length < 512 * 1024)
            {
                var lines = (await File.ReadAllTextAsync(full, ct)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                var sb = new StringBuilder($"new file {path}\n@@ -0,0 +1,{lines.Length} @@\n");
                foreach (var l in lines) sb.Append('+').Append(l).Append('\n');
                return sb.ToString();
            }
        }
        return result.Output;
    }

    public async Task<GitResult> StageAsync(IReadOnlyList<string> paths, CancellationToken ct) =>
        await RunChecked(["add", "-A", "--", .. await PathsAsync(paths, ct)], ct);

    public async Task<GitResult> UnstageAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        // A repository with no commits yet has no HEAD to reset to.
        var hasHead = (await RunAsync(["rev-parse", "--verify", "-q", "HEAD"], ct)).Ok;
        return hasHead
            ? await RunChecked(["reset", "-q", "HEAD", "--", .. await PathsAsync(paths, ct)], ct)
            : await RunChecked(["rm", "-r", "-q", "--cached", "--", .. await PathsAsync(paths, ct)], ct);
    }

    /// <summary>Throws away working-tree changes: tracked files are restored, untracked files deleted.</summary>
    public async Task<GitResult> DiscardAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        var all = paths.Count == 0;
        var wanted = (all ? [] : await PathsAsync(paths, ct)).ToHashSet(StringComparer.Ordinal);
        var untracked = status.Files.Where(f => f.Index == "?" && (all || wanted.Contains(f.Path))).Select(f => f.Path).ToList();
        var tracked = status.Files.Where(f => f.Index != "?" && (all || wanted.Contains(f.Path))).Select(f => f.Path).ToList();
        var output = new StringBuilder();
        if (tracked.Count > 0)
        {
            var r = await RunChecked(["checkout", "HEAD", "--", .. tracked], ct);
            output.Append(r.Output);
        }
        foreach (var file in untracked)
        {
            var full = Path.GetFullPath(Path.Combine(_repoRoot!, file));
            if (CmsWorkspace.IsUnder(full, workspace.ProjectRoot) && File.Exists(full)) File.Delete(full);
        }
        return new GitResult(true, output.ToString());
    }

    public async Task<GitResult> CommitAsync(string message, bool stageAll, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new CmsException("A commit message is required.");
        if (stageAll) await RunChecked(["add", "-A", "--", await ScopeAsync(ct)], ct);
        return await RunChecked(["commit", "-m", message.Trim()], ct);
    }

    public async Task<IReadOnlyList<GitCommit>> LogAsync(int count, CancellationToken ct)
    {
        var result = await RunAsync(["log", $"-{Math.Clamp(count, 1, 500)}", "--pretty=format:%h%x1f%an%x1f%ad%x1f%s", "--date=iso-strict"], ct);
        if (!result.Ok) return [];
        return [.. result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\u001f'))
            .Where(p => p.Length == 4)
            .Select(p => new GitCommit(p[0], p[1], p[2], p[3]))];
    }

    public async Task<(string? Current, IReadOnlyList<string> Local, IReadOnlyList<string> Remote)> BranchesAsync(CancellationToken ct)
    {
        var current = (await RunAsync(["branch", "--show-current"], ct)).Output.Trim();
        var local = await RunAsync(["for-each-ref", "--format=%(refname:short)", "refs/heads"], ct);
        var remote = await RunAsync(["for-each-ref", "--format=%(refname:short)", "refs/remotes"], ct);
        static List<string> Lines(GitResult r) => r.Ok ? [.. r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())] : [];
        return (current.Length == 0 ? null : current, Lines(local), [.. Lines(remote).Where(r => !r.EndsWith("/HEAD", StringComparison.Ordinal))]);
    }

    public Task<GitResult> CheckoutAsync(string branch, bool create, CancellationToken ct)
    {
        ValidateRef(branch);
        return RunChecked(create ? ["checkout", "-b", branch] : ["checkout", branch], ct);
    }

    public Task<GitResult> PullAsync(CancellationToken ct) => RunChecked(["pull", "--ff-only"], ct);

    public async Task<GitResult> PushAsync(CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (status.Branch is null) throw new CmsException("Not on a branch.");
        return status.Upstream is null
            ? await RunChecked(["push", "-u", "origin", status.Branch], ct)
            : await RunChecked(["push"], ct);
    }

    public Task<GitResult> FetchAsync(CancellationToken ct) => RunChecked(["fetch", "--prune"], ct);

    private async Task<List<string>> PathsAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return [await ScopeAsync(ct)];
        var result = new List<string>();
        foreach (var p in paths) result.Add(await SafePathAsync(p, ct));
        return result;
    }

    /// <summary>Checks a repository-relative path sits inside the site folder.</summary>
    private async Task<string> SafePathAsync(string path, CancellationToken ct)
    {
        var root = await RepoRootAsync(ct) ?? throw new CmsException("This site is not in a git repository.");
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || path.StartsWith('-')) throw new CmsException("Invalid path.");
        var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!CmsWorkspace.IsUnder(full, workspace.ProjectRoot))
            throw new CmsException($"'{path}' is outside the site folder.");
        return Path.GetRelativePath(root, full).Replace('\\', '/');
    }

    private static void ValidateRef(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Contains("..", StringComparison.Ordinal)
            || name.Any(c => char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
            throw new CmsException($"'{name}' is not a valid branch name.");
    }

    private async Task<GitResult> RunChecked(IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await RunAsync(args, ct);
        if (!result.Ok) throw new CmsException(result.Output.Trim().Length > 0 ? result.Output.Trim() : $"git {args[0]} failed.");
        return result;
    }

    internal async Task<GitResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct) =>
        await RunInAsync(await RepoRootAsync(ct) ?? workspace.ProjectRoot, args, ct);

    private static async Task<GitResult> RunInAsync(string directory, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Never block waiting for a credential prompt the browser cannot answer.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process proc;
        try { proc = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception) { return new GitResult(false, "git was not found on PATH."); }
        using (proc)
        {
            proc.StandardInput.Close();
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            try { await proc.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new GitResult(false, $"git {args[0]} timed out.");
            }
            var output = await stdout;
            var error = await stderr;
            return proc.ExitCode == 0 ? new GitResult(true, output + (error.Length > 0 && args[0] is "push" or "pull" or "commit" or "fetch" ? error : "")) : new GitResult(false, error.Length > 0 ? error : output);
        }
    }
}
