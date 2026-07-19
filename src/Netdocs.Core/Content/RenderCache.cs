using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Netdocs.Abstractions;

namespace Netdocs.Core.Content;

/// <summary>
/// Content-hash cache for the expensive markdown parse/render step. Each page's rendered
/// artifacts (HTML, title, plain text, TOC) are a pure function of its processed markdown,
/// the markdown pipeline configuration, and the link map, so they can be safely reused
/// across builds when none of those inputs changed. The manifest lives under
/// <c>.cache/render.json</c> (gitignored).
/// </summary>
public sealed class RenderCache
{
    private sealed record TocDto(int Level, string Id, string Title, List<TocDto> Children);
    private sealed record Entry(string Key, string Title, string Html, string PlainText, List<TocDto> Toc);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly Dictionary<string, Entry> _previous;
    private readonly ConcurrentDictionary<string, Entry> _current = new(StringComparer.Ordinal);
    private int _hits;

    private RenderCache(string path, Dictionary<string, Entry> previous)
    {
        _path = path;
        _previous = previous;
    }

    public int Hits => _hits;

    public static RenderCache Load(string projectRoot)
    {
        var path = Path.Combine(projectRoot, ".cache", "render.json");
        Dictionary<string, Entry> previous = new(StringComparer.Ordinal);
        try
        {
            if (File.Exists(path))
                previous = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), JsonOptions)
                           ?? new(StringComparer.Ordinal);
        }
        catch
        {
            // A corrupt or version-incompatible cache is simply ignored (full rebuild).
            previous = new(StringComparer.Ordinal);
        }
        return new RenderCache(path, previous);
    }

    /// <summary>Per-page cache key: processed markdown + identity + pipeline salt + link map hash.</summary>
    public static string ComputeKey(Page page, string pipelineSalt, string linkMapHash)
    {
        var payload = string.Join('\0', page.ProcessedMarkdown, page.RelativePath, page.Title, pipelineSalt, linkMapHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>Restores cached artifacts onto the page when the key matches. Returns false on a miss.</summary>
    public bool TryRestore(Page page, string key)
    {
        if (!_previous.TryGetValue(page.RelativePath, out var entry) || entry.Key != key)
            return false;

        page.HtmlContent = entry.Html;
        page.PlainText = entry.PlainText;
        page.Toc = entry.Toc.Select(FromDto).ToList();
        if (string.IsNullOrEmpty(page.Title)) page.Title = entry.Title;

        Interlocked.Increment(ref _hits);
        _current[page.RelativePath] = entry;
        return true;
    }

    /// <summary>Records the freshly rendered artifacts for the next build.</summary>
    public void Store(Page page, string key)
    {
        _current[page.RelativePath] = new Entry(
            key, page.Title, page.HtmlContent, page.PlainText,
            page.Toc.Select(ToDto).ToList());
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var ordered = _current.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            File.WriteAllText(_path, JsonSerializer.Serialize(ordered, JsonOptions));
        }
        catch
        {
            // Cache is best-effort; a write failure must not fail the build.
        }
    }

    private static TocDto ToDto(TocEntry e) =>
        new(e.Level, e.Id, e.Title, e.Children.Select(ToDto).ToList());

    private static TocEntry FromDto(TocDto d) =>
        new() { Level = d.Level, Id = d.Id, Title = d.Title, Children = d.Children.Select(FromDto).ToList() };
}
