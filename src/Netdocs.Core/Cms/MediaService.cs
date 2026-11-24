using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Netdocs.Core.Cms;

/// <summary>A file in the media library.</summary>
public sealed record MediaItem(string Path, string Kind, long Size, DateTimeOffset Modified, int? Width, int? Height, int References);

/// <summary>The outcome of converting one image to WebP.</summary>
public sealed record WebpResult(string From, string To, long OldSize, long NewSize, int UpdatedPages, string? Error);

/// <summary>
/// The media library: every non-markdown file under the docs folder. Upload, delete, rename (with
/// link updates) and optimization. Converting to WebP writes the new file, re-points every
/// markdown reference to it and deletes the original, so only the optimized file is committed.
/// </summary>
public sealed class MediaService(CmsWorkspace workspace, ContentStore content)
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".bmp", ".ico"];
    private static readonly string[] ConvertibleExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    /// <summary>Largest upload accepted, in bytes.</summary>
    public const long MaxUploadBytes = 100L * 1024 * 1024;

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsConvertible(string path) => ConvertibleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MediaItem> List()
    {
        var references = ReferenceCounts();
        var result = new List<MediaItem>();
        if (!Directory.Exists(workspace.DocsDir)) return result;
        foreach (var file in Directory.EnumerateFiles(workspace.DocsDir, "*", SearchOption.AllDirectories))
        {
            if (ContentStore.IsMarkdown(file)) continue;
            var rel = workspace.ToDocsRelative(file);
            if (rel.Split('/').Any(s => s.StartsWith('.'))) continue;
            var info = new FileInfo(file);
            int? width = null, height = null;
            if (IsImage(file) && !file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var identity = Image.Identify(file);
                    width = identity.Width;
                    height = identity.Height;
                }
                catch (Exception) { /* unreadable image: list it without dimensions */ }
            }
            result.Add(new MediaItem(rel, IsImage(file) ? "image" : "file", info.Length, info.LastWriteTimeUtc, width, height, references.GetValueOrDefault(rel)));
        }
        return [.. result.OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Absolute path of a media file, for serving it.</summary>
    public string Locate(string path)
    {
        var full = workspace.ResolveDocsPath(path);
        if (!File.Exists(full) || ContentStore.IsMarkdown(full)) throw new CmsNotFoundException($"'{path}' was not found.");
        return full;
    }

    public async Task<string> UploadAsync(string folder, string fileName, Stream data, bool overwrite, CancellationToken ct)
    {
        var name = Path.GetFileName(fileName ?? "");
        if (name.Length == 0 || name.StartsWith('.')) throw new CmsException("A file name is required.");
        if (ContentStore.IsMarkdown(name)) throw new CmsException("Create pages from the Pages screen, not the media library.");
        var rel = string.IsNullOrWhiteSpace(folder) ? name : folder.Trim('/') + "/" + name;
        var full = workspace.ResolveDocsPath(rel);
        if (File.Exists(full) && !overwrite) throw new CmsConflictException($"'{rel}' already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var output = File.Create(full))
            await data.CopyToAsync(output, ct);
        return workspace.ToDocsRelative(full);
    }

    /// <summary>Deletes a file. Refuses when pages still reference it, unless forced.</summary>
    public void Delete(string path, bool force)
    {
        var full = Locate(path);
        var rel = workspace.ToDocsRelative(full);
        if (!force)
        {
            var refs = content.ReferencesTo(rel);
            if (refs.Count > 0)
                throw new CmsConflictException($"'{rel}' is still used by {refs.Count} page(s): {string.Join(", ", refs.Take(5))}.");
        }
        File.Delete(full);
    }

    /// <summary>Renames or moves a file and updates every page that links to it.</summary>
    public int Move(string from, string to)
    {
        var source = Locate(from);
        var fromRel = workspace.ToDocsRelative(source);
        var target = workspace.ResolveDocsPath(to);
        var toRel = workspace.ToDocsRelative(target);
        if (ContentStore.IsMarkdown(target)) throw new CmsException("Media files cannot be renamed to markdown.");
        if (File.Exists(target)) throw new CmsConflictException($"'{toRel}' already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target);
        return content.RewriteReferences(null, t => t.Equals(fromRel, StringComparison.OrdinalIgnoreCase) ? toRel : null);
    }

    /// <summary>Converts images to WebP (optionally shrinking them to <paramref name="maxWidth"/>),
    /// re-points references and deletes the originals.</summary>
    public async Task<IReadOnlyList<WebpResult>> ConvertToWebpAsync(IReadOnlyList<string> paths, int quality, int? maxWidth, CancellationToken ct)
    {
        quality = Math.Clamp(quality, 1, 100);
        var results = new List<WebpResult>();
        foreach (var path in paths)
        {
            string fromRel = path;
            try
            {
                var full = Locate(path);
                fromRel = workspace.ToDocsRelative(full);
                if (!IsConvertible(full)) { results.Add(new WebpResult(fromRel, fromRel, 0, 0, 0, "Only PNG, JPEG, GIF and BMP images can be converted.")); continue; }
                var target = Path.ChangeExtension(full, ".webp");
                var toRel = workspace.ToDocsRelative(target);
                if (File.Exists(target)) { results.Add(new WebpResult(fromRel, toRel, 0, 0, 0, $"'{toRel}' already exists.")); continue; }

                var oldSize = new FileInfo(full).Length;
                using (var image = await Image.LoadAsync(full, ct))
                {
                    if (maxWidth is > 0 && image.Width > maxWidth)
                        image.Mutate(x => x.Resize(maxWidth.Value, 0));
                    await image.SaveAsWebpAsync(target, new WebpEncoder { Quality = quality }, ct);
                }
                var updated = content.RewriteReferences(null, t => t.Equals(fromRel, StringComparison.OrdinalIgnoreCase) ? toRel : null);
                File.Delete(full);
                results.Add(new WebpResult(fromRel, toRel, oldSize, new FileInfo(target).Length, updated, null));
            }
            catch (Exception ex) when (ex is CmsException or IOException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                results.Add(new WebpResult(fromRel, fromRel, 0, 0, 0, ex.Message));
            }
        }
        return results;
    }

    /// <summary>Shrinks an image in place to at most <paramref name="maxWidth"/> pixels wide.</summary>
    public async Task<long> ResizeAsync(string path, int maxWidth, CancellationToken ct)
    {
        var full = Locate(path);
        if (!IsImage(full) || full.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) throw new CmsException("Only raster images can be resized.");
        using var image = await Image.LoadAsync(full, ct);
        if (image.Width > maxWidth)
        {
            image.Mutate(x => x.Resize(maxWidth, 0));
            await image.SaveAsync(full, ct);
        }
        return new FileInfo(full).Length;
    }

    private Dictionary<string, int> ReferenceCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in content.AllPagePaths())
        {
            string text;
            try { text = File.ReadAllText(workspace.ResolveDocsPath(page)); }
            catch (IOException) { continue; }
            foreach (var target in MarkdownLinkRewriter.Targets(text))
            {
                var resolved = MarkdownLinkRewriter.Resolve(page, target);
                if (resolved is not null) counts[resolved] = counts.GetValueOrDefault(resolved) + 1;
            }
        }
        return counts;
    }
}
