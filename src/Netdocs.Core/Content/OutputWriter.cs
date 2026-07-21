using System.Security.Cryptography;
using System.Text;
using Netdocs.Abstractions;

namespace Netdocs.Core.Content;

/// <summary>
/// Writes build output only when the target file's contents actually changed, so republishing
/// after a small source change rewrites just the affected files (leaving the rest — and their
/// timestamps — untouched). This is what lets the watch daemon publish a minimal diff.
/// Every write also registers the path with the <see cref="SiteContext"/> so stale files can be
/// pruned instead of wiping the whole output directory up front.
/// </summary>
public static class OutputWriter
{
    /// <summary>Writes UTF-8 text if changed and tracks the output. Returns true when written.</summary>
    public static async Task<bool> WriteTextIfChangedAsync(SiteContext site, string path, string content, CancellationToken ct = default)
    {
        site.TrackOutput(path);
        return await WriteBytesIfChangedAsync(path, Encoding.UTF8.GetBytes(content), ct);
    }

    /// <summary>Copies a source file into the output only when its bytes differ, and tracks it.</summary>
    public static async Task<bool> CopyIfChangedAsync(SiteContext site, string source, string dest, CancellationToken ct = default)
    {
        site.TrackOutput(dest);
        return await WriteBytesIfChangedAsync(dest, await File.ReadAllBytesAsync(source, ct), ct);
    }

    /// <summary>Writes UTF-8 text if it differs from the existing file. Returns true when written.</summary>
    public static Task<bool> WriteTextIfChangedAsync(string path, string content, CancellationToken ct = default)
        => WriteBytesIfChangedAsync(path, Encoding.UTF8.GetBytes(content), ct);

    /// <summary>Writes bytes if they differ from the existing file. Returns true when written.</summary>
    public static async Task<bool> WriteBytesIfChangedAsync(string path, byte[] content, CancellationToken ct = default)
    {
        if (Unchanged(path, content)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct);
        return true;
    }

    private static bool Unchanged(string path, byte[] content)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length != content.Length) return false;
            return SHA256.HashData(existing).AsSpan().SequenceEqual(SHA256.HashData(content));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes any file under <paramref name="siteDir"/> that was not produced this build
    /// (i.e. not tracked in <paramref name="site"/>), then removes emptied directories.
    /// Returns the number of files pruned.
    /// </summary>
    public static int PruneStale(SiteContext site, string siteDir)
    {
        if (!Directory.Exists(siteDir)) return 0;
        var pruned = 0;
        foreach (var file in Directory.EnumerateFiles(siteDir, "*", SearchOption.AllDirectories))
        {
            if (site.WrittenOutputs.ContainsKey(Path.GetFullPath(file))) continue;
            try { File.Delete(file); pruned++; } catch { /* best effort */ }
        }

        // Remove directories left empty by pruning (deepest first).
        foreach (var dir in Directory.EnumerateDirectories(siteDir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch { /* best effort */ }
        }
        return pruned;
    }
}
