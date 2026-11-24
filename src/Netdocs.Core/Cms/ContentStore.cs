using Netdocs.Core.Configuration;

namespace Netdocs.Core.Cms;

/// <summary>A page as listed in the CMS.</summary>
public sealed record PageSummary(
    string Path,
    string Title,
    string? Url,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Categories,
    string? Date,
    bool Draft,
    bool InNav,
    long Size,
    DateTimeOffset Modified);

/// <summary>A page opened for editing.</summary>
public sealed record PageDocument(
    string Path,
    string? Url,
    string Hash,
    IReadOnlyDictionary<string, object?> FrontMatter,
    IReadOnlyList<string> FrontMatterOrder,
    string Body,
    string Raw);

/// <summary>Lists, reads, creates, saves, moves and deletes markdown pages.</summary>
public sealed class ContentStore(CmsWorkspace workspace, NavService nav)
{
    private static readonly string[] MarkdownExtensions = [".md", ".markdown"];

    /// <summary>Docs-relative path → built URL, refreshed after each build.</summary>
    public IReadOnlyDictionary<string, string> Urls { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static bool IsMarkdown(string path) =>
        MarkdownExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> AllPagePaths()
    {
        if (!Directory.Exists(workspace.DocsDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(workspace.DocsDir, "*", SearchOption.AllDirectories))
        {
            if (!IsMarkdown(file)) continue;
            var rel = workspace.ToDocsRelative(file);
            if (rel.Split('/').Any(s => s.StartsWith('.'))) continue;
            yield return rel;
        }
    }

    public IReadOnlyList<PageSummary> List()
    {
        var listed = nav.ListedPaths();
        var result = new List<PageSummary>();
        foreach (var rel in AllPagePaths().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var full = workspace.ResolveDocsPath(rel);
            var info = new FileInfo(full);
            IReadOnlyDictionary<string, object?> meta;
            string body;
            try
            {
                var raw = File.ReadAllText(full);
                (meta, body) = Content.FrontMatter.Split(raw);
            }
            catch (Exception)
            {
                meta = YamlAccess.EmptyMap;
                body = "";
            }
            result.Add(new PageSummary(
                rel,
                TitleOf(meta, body, rel),
                Urls.TryGetValue(rel, out var url) ? url : null,
                StringList(meta, "tags"),
                StringList(meta, "categories"),
                meta.Get("date") switch { IReadOnlyDictionary<string, object?> d => d.Get("created")?.ToString(), { } v => v.ToString(), _ => null },
                meta.Get("draft").AsBool(false),
                listed.Contains(rel),
                info.Length,
                info.LastWriteTimeUtc));
        }
        return result;
    }

    public PageDocument Read(string path)
    {
        var full = RequirePage(path);
        var raw = File.ReadAllText(full);
        var editor = FrontMatterEditor.Parse(raw);
        var rel = workspace.ToDocsRelative(full);
        return new PageDocument(rel, Urls.GetValueOrDefault(rel), CmsWorkspace.Hash(raw), editor.Values, editor.Keys.ToList(), editor.Body, raw);
    }

    /// <summary>Saves a page. <paramref name="frontMatter"/> null keeps the current front matter;
    /// <paramref name="body"/> null keeps the current body. Refuses when the file changed since
    /// <paramref name="expectedHash"/> was read.</summary>
    public PageDocument Save(string path, string? expectedHash, IReadOnlyDictionary<string, object?>? frontMatter, string? body)
    {
        var full = RequirePage(path);
        var raw = File.ReadAllText(full);
        if (expectedHash is not null && CmsWorkspace.Hash(raw) != expectedHash)
            throw new CmsConflictException("This page was changed outside the editor since you opened it. Reload it to get the latest version.");
        var editor = FrontMatterEditor.Parse(raw);
        if (frontMatter is not null) editor.ReplaceAll(frontMatter);
        if (body is not null) editor.Body = MatchNewlines(body, raw);
        var updated = editor.ToString();
        if (updated != raw) CmsWorkspace.WriteText(full, updated);
        return Read(path);
    }

    /// <summary>Replaces the whole file text.</summary>
    public PageDocument SaveRaw(string path, string? expectedHash, string raw)
    {
        var full = RequirePage(path);
        var current = File.ReadAllText(full);
        if (expectedHash is not null && CmsWorkspace.Hash(current) != expectedHash)
            throw new CmsConflictException("This page was changed outside the editor since you opened it. Reload it to get the latest version.");
        CmsWorkspace.WriteText(full, MatchNewlines(raw, current));
        return Read(path);
    }

    public PageDocument Create(string path, string? title, IReadOnlyDictionary<string, object?>? frontMatter, string? body, bool addToNav)
    {
        path = NormalizeNewPath(path);
        var full = workspace.ResolveDocsPath(path);
        if (File.Exists(full)) throw new CmsConflictException($"'{path}' already exists.");
        var editor = FrontMatterEditor.Parse("");
        if (frontMatter is not null) foreach (var (k, v) in frontMatter) editor.Set(k, v);
        if (!string.IsNullOrWhiteSpace(title) && frontMatter?.ContainsKey("title") != true) editor.Set("title", title);
        editor.Body = body ?? (string.IsNullOrWhiteSpace(title) ? "\n" : $"\n# {title}\n\n");
        CmsWorkspace.WriteText(full, editor.ToString());
        if (addToNav)
        {
            var navArray = nav.Read();
            if (navArray.Count > 0)
            {
                navArray.Add(new System.Text.Json.Nodes.JsonObject { ["path"] = path });
                nav.Write(navArray);
            }
        }
        return Read(path);
    }

    /// <summary>Moves or renames a page, updating the nav and every relative link to it (and the
    /// page's own relative links, when it changes folder).</summary>
    public MoveResult Move(string from, string to)
    {
        var source = RequirePage(from);
        from = workspace.ToDocsRelative(source);
        to = NormalizeNewPath(to);
        var target = workspace.ResolveDocsPath(to);
        if (string.Equals(source, target, CmsWorkspace.PathComparison)) return new MoveResult(to, 0, false);
        if (File.Exists(target) && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new CmsConflictException($"'{to}' already exists.");

        // The page's own links are relative to its folder; re-point them from the new location.
        var text = File.ReadAllText(source);
        var moved = MarkdownLinkRewriter.Rewrite(text, t =>
        {
            var resolved = MarkdownLinkRewriter.Resolve(from, t);
            return resolved is null ? null : MarkdownLinkRewriter.Relative(to, resolved) is var rel && rel != t ? rel : null;
        });
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target, overwrite: string.Equals(source, target, StringComparison.OrdinalIgnoreCase));
        if (moved != text) CmsWorkspace.WriteText(target, moved);

        var updatedPages = RewriteReferences(to, t => t.Equals(from, StringComparison.OrdinalIgnoreCase) ? to : null);
        var navChanged = nav.RenamePath(from, to);
        return new MoveResult(to, updatedPages, navChanged);
    }

    public sealed record MoveResult(string Path, int UpdatedPages, bool NavUpdated);

    public void Delete(string path)
    {
        var full = RequirePage(path);
        var rel = workspace.ToDocsRelative(full);
        File.Delete(full);
        nav.RemovePath(rel);
    }

    /// <summary>Pages that link to <paramref name="path"/> (a page or a media file).</summary>
    public IReadOnlyList<string> ReferencesTo(string path)
    {
        var result = new List<string>();
        foreach (var page in AllPagePaths())
        {
            var text = File.ReadAllText(workspace.ResolveDocsPath(page));
            if (MarkdownLinkRewriter.Targets(text).Any(t => string.Equals(MarkdownLinkRewriter.Resolve(page, t), path, StringComparison.OrdinalIgnoreCase)))
                result.Add(page);
        }
        return result;
    }

    /// <summary>Rewrites links in every page (except <paramref name="skip"/>) whose resolved target
    /// <paramref name="map"/> renames. Returns the number of pages changed.</summary>
    public int RewriteReferences(string? skip, Func<string, string?> map)
    {
        var count = 0;
        foreach (var page in AllPagePaths())
        {
            if (skip is not null && page.Equals(skip, StringComparison.OrdinalIgnoreCase)) continue;
            var full = workspace.ResolveDocsPath(page);
            var text = File.ReadAllText(full);
            var updated = MarkdownLinkRewriter.Rewrite(text, t =>
            {
                var resolved = MarkdownLinkRewriter.Resolve(page, t);
                if (resolved is null) return null;
                var renamed = map(resolved);
                return renamed is null ? null : MarkdownLinkRewriter.Relative(page, renamed);
            });
            if (updated != text) { CmsWorkspace.WriteText(full, updated); count++; }
        }
        return count;
    }

    private string RequirePage(string path)
    {
        var full = workspace.ResolveDocsPath(path);
        if (!IsMarkdown(full)) throw new CmsException($"'{path}' is not a markdown page.");
        if (!File.Exists(full)) throw new CmsNotFoundException($"Page '{path}' was not found.");
        return full;
    }

    private static string NormalizeNewPath(string path)
    {
        path = (path ?? "").Replace('\\', '/').Trim().Trim('/');
        if (path.Length == 0) throw new CmsException("A page path is required.");
        if (!IsMarkdown(path)) path += ".md";
        return path;
    }

    private static string MatchNewlines(string text, string original)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return original.Contains("\r\n", StringComparison.Ordinal) ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal) : normalized;
    }

    internal static string TitleOf(IReadOnlyDictionary<string, object?> meta, string body, string rel)
    {
        if (meta.Get("title").AsString() is { Length: > 0 } t) return t;
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal)) return trimmed[2..].Trim();
        }
        var name = Path.GetFileNameWithoutExtension(rel);
        if (name.Equals("index", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileName(Path.GetDirectoryName(rel)) is { Length: > 0 } dir ? dir : "Home";
        return name.Replace('-', ' ').Replace('_', ' ');
    }

    internal static IReadOnlyList<string> StringList(IReadOnlyDictionary<string, object?> meta, string key) =>
        meta.Get(key) switch
        {
            string s when s.Length > 0 => [s],
            IEnumerable<object?> list => [.. list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0)],
            _ => [],
        };
}
