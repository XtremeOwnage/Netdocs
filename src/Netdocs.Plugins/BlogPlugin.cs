using System.Globalization;
using System.Text;
using Netdocs.Abstractions;
using Netdocs.Core.Configuration;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>
/// Blog plugin: rewrites post URLs, builds a paginated index, category and archive
/// listing pages from posts under <c>{blog_dir}/posts/</c>.
/// </summary>
public sealed class BlogPlugin : IPlugin, IBuildHook, IContentGenerator
{
    private string _blogDir = "blog/";
    private string _urlDateFormat = "yyyy";
    private int _perPage = 20;
    private bool _categoriesEnabled = true;
    private bool _archiveEnabled = true;
    private SiteConfig _config = null!;

    private readonly List<BlogPost> _posts = [];
    private readonly Dictionary<string, Author> _authors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BlogPost>> _authorPosts = new(StringComparer.OrdinalIgnoreCase);
    // Maps a normalized source path (e.g. "blog/posts/2026/foo.md") to its root-relative output
    // URL, so excerpt `.md` links (relative to the post, not the listing page) can be resolved.
    private readonly Dictionary<string, string> _pageUrls = new(StringComparer.OrdinalIgnoreCase);
    private List<Dictionary<string, object?>> _archivesNav = [];
    private List<Dictionary<string, object?>> _categoriesNav = [];
    private List<Dictionary<string, object?>> _authorsNav = [];

    public string Name => "blog";

    public void Configure(IPluginContext ctx)
    {
        _config = ctx.Config;
        var o = ctx.PluginOptions;
        if (o.TryGetValue("blog_dir", out var bd) && bd is string bds) _blogDir = Normalize(bds);
        if (o.TryGetValue("post_url_date_format", out var df) && df is string dfs) _urlDateFormat = dfs;
        if (o.TryGetValue("pagination_per_page", out var pp) && pp is long ppl) _perPage = (int)ppl;
        if (o.TryGetValue("categories", out var c) && c is bool cb) _categoriesEnabled = cb;
        if (o.TryGetValue("archive", out var a) && a is bool ab) _archiveEnabled = ab;

        LoadAuthors(ctx);
    }

    private void LoadAuthors(IPluginContext ctx)
    {
        var path = Path.Combine(ctx.Config.AbsoluteDocsDir, _blogDir.Replace('/', Path.DirectorySeparatorChar), ".authors.yml");
        if (!File.Exists(path)) return;
        var root = YamlTree.Parse(File.ReadAllText(path)).AsMap();
        foreach (var (id, value) in root.Get("authors").AsMap())
        {
            var a = value.AsMap();
            _authors[id] = new Author(
                id,
                a.Get("name").AsString() ?? id,
                a.Get("description").AsString() ?? "",
                a.Get("avatar").AsString() ?? "",
                $"/{_blogDir}author/{Slug.Make(id, _config.Slugify)}/");
        }
    }

    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        var postsPrefix = _blogDir + "posts/";
        foreach (var page in site.Pages)
        {
            if (!page.RelativePath.StartsWith(postsPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var date = ReadDate(page);
            var slug = ResolvePostSlug(page);
            var url = $"{_blogDir}{date.ToString(_urlDateFormat, CultureInfo.InvariantCulture)}/{slug}/";
            page.Url = url;
            page.OutputPath = Path.Combine(site.Config.AbsoluteSiteDir, ContentDiscovery.OutputFileFor(url));
            page.Created = date;

            var categories = GetCategories(page);
            _posts.Add(new BlogPost(page, date, categories, ReadExcerpt(page)));
            ApplyPostMeta(page, date, categories);
        }

        _posts.Sort((a, b) => b.Date.CompareTo(a.Date));
        site.State["blog_posts"] = _posts;

        // Snapshot every page's source path -> output URL now that post URLs are assigned, so
        // excerpt links that point at other pages (.md) resolve to the correct published URL.
        _pageUrls.Clear();
        foreach (var page in site.Pages)
            _pageUrls[NormalizeRel(page.RelativePath.Replace('\\', '/'))] = "/" + page.Url.TrimStart('/');

        // Group posts by author (respecting front-matter order + single-author fallback).
        foreach (var post in _posts)
            foreach (var author in ResolveAuthors(post.Page))
            {
                if (!_authorPosts.TryGetValue(author.Id, out var list))
                    _authorPosts[author.Id] = list = [];
                list.Add(post);
            }
        _authorsNav = _authorPosts
            .OrderBy(kv => _authors[kv.Key].Name, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new Dictionary<string, object?>
            {
                ["name"] = _authors[kv.Key].Name,
                ["url"] = _authors[kv.Key].Url,
                ["count"] = kv.Value.Count,
            }).ToList();

        // Precompute the blog-listing sidebar nav (years + categories).
        _archivesNav = _archiveEnabled
            ? _posts.GroupBy(p => p.Date.Year).OrderByDescending(g => g.Key)
                .Select(g => new Dictionary<string, object?>
                {
                    ["year"] = g.Key.ToString(CultureInfo.InvariantCulture),
                    ["url"] = $"/{_blogDir}archive/{g.Key}/",
                    ["count"] = g.Count(),
                }).ToList()
            : [];
        _categoriesNav = _categoriesEnabled
            ? _posts.SelectMany(p => p.Categories).GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new Dictionary<string, object?>
                {
                    ["name"] = g.Key,
                    ["url"] = $"/{_blogDir}category/{Slug.Make(g.Key, _config.Slugify)}/",
                    ["count"] = g.Count(),
                }).ToList()
            : [];

        // Attach the shared blog navigation (Archive/Categories/Authors) to every post now that
        // it is fully computed. This must run AFTER the lists above are built: `ApplyPostMeta`
        // runs in the discovery loop above, before these lists exist, so assigning there would
        // capture the empty initial lists (the references are replaced, not mutated, below).
        foreach (var post in _posts)
        {
            post.Page.Meta["blog_archives"] = _archivesNav;
            post.Page.Meta["blog_categories"] = _categoriesNav;
            post.Page.Meta["blog_authors"] = _authorsNav;
        }

        // Turn the existing blog index into page 1 of the paginated list.
        var index = site.Pages.FirstOrDefault(p =>
            p.RelativePath.Equals(_blogDir + "index.md", StringComparison.OrdinalIgnoreCase));
        if (index is not null)
        {
            var pageItems = _posts.Take(_perPage).ToList();
            index.RawMarkdown = RenderList("Blog", pageItems, _posts.Count > _perPage ? $"{_blogDir}page/2/" : null);
            index.Title = "Blog";
            ApplyListingMeta(index);
        }

        return Task.CompletedTask;
    }

    /// <summary>Flags a blog-listing page and attaches the Archive/Categories sidebar nav data.</summary>
    private void ApplyListingMeta(Page page)
    {
        page.Meta["is_blog_listing"] = true;
        page.Meta["blog_index_url"] = "/" + _blogDir;
        page.Meta["blog_archives"] = _archivesNav;
        page.Meta["blog_categories"] = _categoriesNav;
        page.Meta["blog_authors"] = _authorsNav;
    }

    /// <summary>Sets blog-post metadata on the page for the theme to render (sidebar + tag chips).</summary>
    private void ApplyPostMeta(Page page, DateTimeOffset date, IReadOnlyList<string> categories)
    {
        var minutes = ReadTimeMinutes(page);

        page.Meta["is_post"] = true;
        page.Meta["post_date"] = date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        page.Meta["post_readtime"] = minutes;
        page.Meta["post_categories"] = categories;
        page.Meta["post_categories_links"] = categories.Select(c => new Dictionary<string, object?>
        {
            ["name"] = c,
            ["url"] = $"/{_blogDir}category/{Slug.Make(c, _config.Slugify)}/",
        }).ToList();
        page.Meta["post_tags"] = ReadList(page, "tags");
        page.Meta["blog_index_url"] = "/" + _blogDir;

        var authors = ResolveAuthors(page);
        if (authors.Count > 0)
        {
            page.Meta["post_authors"] = authors.Select(a => new Dictionary<string, object?>
            {
                ["name"] = a.Name,
                ["role"] = a.Role,
                ["avatar"] = a.Avatar,
                ["url"] = a.Url,
            }).ToList();

            var primary = authors[0];
            page.Meta["post_author_name"] = primary.Name;
            page.Meta["post_author_role"] = primary.Role;
            page.Meta["post_author_avatar"] = primary.Avatar;
            page.Meta["post_author_url"] = primary.Url;
        }
    }

    private List<Author> ResolveAuthors(Page page)
    {
        var result = new List<Author>();
        foreach (var id in ReadList(page, "authors"))
            if (_authors.TryGetValue(id, out var a) && !result.Contains(a))
                result.Add(a);
        // Default to the sole author when a post doesn't specify one.
        if (result.Count == 0 && _authors.Count == 1) result.Add(_authors.Values.First());
        return result;
    }

    public async IAsyncEnumerable<Page> GenerateAsync(
        SiteContext site, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;

        // Pagination pages 2..N.
        var totalPages = (int)Math.Ceiling(_posts.Count / (double)_perPage);
        for (var p = 2; p <= totalPages; p++)
        {
            var items = _posts.Skip((p - 1) * _perPage).Take(_perPage).ToList();
            var next = p < totalPages ? $"{_blogDir}page/{p + 1}/" : null;
            var url = $"{_blogDir}page/{p}/";
            yield return Listing(Generated(site, url, $"Blog - Page {p}", RenderList($"Blog - Page {p}", items, next)));
        }

        // Category pages.
        if (_categoriesEnabled)
        {
            foreach (var group in _posts
                .SelectMany(post => post.Categories.Select(cat => (cat, post)))
                .GroupBy(x => x.cat, StringComparer.OrdinalIgnoreCase))
            {
                var slug = Slug.Make(group.Key, _config.Slugify);
                var url = $"{_blogDir}category/{slug}/";
                yield return Listing(Generated(site, url, group.Key,
                    RenderList($"Category: {group.Key}", group.Select(x => x.post).ToList(), null)));
            }
        }

        // Archive pages by year.
        if (_archiveEnabled)
        {
            foreach (var group in _posts.GroupBy(post => post.Date.Year).OrderByDescending(g => g.Key))
            {
                var url = $"{_blogDir}archive/{group.Key}/";
                yield return Listing(Generated(site, url, group.Key.ToString(),
                    RenderList($"Archive: {group.Key}", group.ToList(), null)));
            }
        }

        // Author pages.
        foreach (var (id, posts) in _authorPosts)
        {
            var author = _authors[id];
            var url = author.Url.TrimStart('/');
            yield return Listing(Generated(site, url, author.Name, RenderAuthorList(author, posts)));
        }
    }

    /// <summary>Attaches blog-listing sidebar metadata to a generated listing page.</summary>
    private Page Listing(Page page)
    {
        ApplyListingMeta(page);
        return page;
    }

    private static Page Generated(SiteContext site, string url, string title, string markdown) => new()
    {
        SourcePath = "",
        RelativePath = url + "index.md",
        IsGenerated = true,
        Url = url,
        Title = title,
        RawMarkdown = markdown,
        OutputPath = Path.Combine(site.Config.AbsoluteSiteDir, ContentDiscovery.OutputFileFor(url)),
    };

    private string RenderList(string title, IReadOnlyList<BlogPost> posts, string? nextPageUrl)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(title).AppendLine();
        AppendPosts(sb, posts, nextPageUrl);
        return sb.ToString();
    }

    private string RenderAuthorList(Author author, IReadOnlyList<BlogPost> posts)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(author.Name).AppendLine();
        if (author.Role.Length > 0) sb.Append('*').Append(author.Role).AppendLine("*").AppendLine();
        AppendPosts(sb, posts, null);
        return sb.ToString();
    }

    private void AppendPosts(StringBuilder sb, IReadOnlyList<BlogPost> posts, string? nextPageUrl)
    {
        foreach (var post in posts)
        {
            var rootUrl = "/" + post.Page.Url;
            sb.Append("## [").Append(post.Page.Title).Append("](").Append(rootUrl).AppendLine(")");
            sb.Append("*").Append(post.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)).Append("*");
            if (post.Categories.Count > 0)
            {
                sb.Append(" &middot; in ");
                for (var i = 0; i < post.Categories.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var cat = post.Categories[i];
                    sb.Append('[').Append(cat).Append("](/").Append(_blogDir)
                      .Append("category/").Append(Slug.Make(cat, _config.Slugify)).Append("/)");
                }
            }
            sb.Append(" &middot; ").Append(ReadTimeMinutes(post.Page)).Append(" min read");
            sb.AppendLine().AppendLine();
            if (post.Excerpt.Length > 0) sb.AppendLine(RewriteExcerptLinks(post.Excerpt, post.Page.RelativePath)).AppendLine();
            sb.Append("[Continue reading](").Append(rootUrl).AppendLine(")").AppendLine();
        }
        if (nextPageUrl is not null)
            sb.Append("[Older posts →](/").Append(nextPageUrl).AppendLine(")");
    }

    /// <summary>Estimated reading time in minutes (~238 wpm), shared by post pages and listings.</summary>
    private static int ReadTimeMinutes(Page page)
    {
        var words = page.RawMarkdown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Round(words / 238.0));
    }

    private static DateTimeOffset ReadDate(Page page)
    {
        if (page.FrontMatter.TryGetValue("date", out var d))
        {
            if (d is IReadOnlyDictionary<string, object?> map && map.TryGetValue("created", out var created))
                d = created;
            if (d is DateTime dt) return dt;
            if (d is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return File.Exists(page.SourcePath) ? File.GetLastWriteTimeUtc(page.SourcePath) : DateTimeOffset.UtcNow;
    }

    // Matches an ATX level-1 heading line (optional trailing closing hashes).
    private static readonly System.Text.RegularExpressions.Regex H1Regex = new(
        @"^\s{0,3}#\s+(?<title>.+?)\s*#*\s*$",
        System.Text.RegularExpressions.RegexOptions.Multiline |
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Collapses inline markdown links `[text](url)` / `![alt](src)` down to their visible text
    // so URLs don't leak words into the slug.
    private static readonly System.Text.RegularExpressions.Regex InlineLinkRegex = new(
        @"!?\[(?<text>[^\]]*)\]\([^)]*\)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Resolves a blog post's URL slug the way mkdocs-material does: an explicit front-matter
    /// <c>slug</c> wins, otherwise the post <em>title</em> (front-matter title or first H1) is
    /// slugified. The file name is only a last-resort fallback. This keeps Netdocs URLs identical
    /// to the ones MkDocs produced (e.g. a post titled "Hacking KVM with IP Control" published as
    /// <c>2025-02-24-KVM-Esphome.md</c> lives at <c>/blog/2025/hacking-kvm-with-ip-control/</c>).
    /// </summary>
    private string ResolvePostSlug(Page page)
    {
        if (page.FrontMatter.TryGetValue("slug", out var s) && s is string slug && slug.Trim().Length > 0)
            return slug.Trim().Trim('/');

        var title = PostTitle(page);
        return !string.IsNullOrWhiteSpace(title)
            ? Slug.Make(title, _config.Slugify)
            : Slug.Make(Path.GetFileNameWithoutExtension(page.RelativePath), _config.Slugify);
    }

    /// <summary>
    /// The post title as mkdocs-material sees it: the front-matter <c>title</c>, else the first
    /// level-1 heading in the source. H1 extraction runs here because page titles derived from
    /// headings are not populated until the later render step.
    /// </summary>
    private static string? PostTitle(Page page)
    {
        if (!string.IsNullOrWhiteSpace(page.Title)) return page.Title;

        var match = H1Regex.Match(page.RawMarkdown ?? "");
        if (!match.Success) return null;

        var text = InlineLinkRegex.Replace(match.Groups["title"].Value, "${text}");
        return text.Trim();
    }

    private static List<string> ReadList(Page page, string key)
    {
        if (page.FrontMatter.TryGetValue(key, out var v) && v is IEnumerable<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        return [];
    }

    /// <summary>Categories from front matter, else derived from the post's category folder.</summary>
    private List<string> GetCategories(Page page)
    {
        var fromMeta = ReadList(page, "categories");
        if (fromMeta.Count > 0) return fromMeta;

        var rel = page.RelativePath.Replace('\\', '/');
        var prefix = _blogDir + "posts/";
        if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var segments = rel[prefix.Length..].Split('/');
            var folders = segments[..^1].Where(s => !int.TryParse(s, out _)).ToList();
            if (folders.Count > 0) return [folders[0]];
        }
        return fromMeta;
    }

    /// <summary>A concise teaser for the blog list: content up to <c>&lt;!-- more --&gt;</c> (or the
    /// first paragraph), with any leading H1 stripped and length capped so it never becomes a
    /// "wall of text" that duplicates the post title.</summary>
    private static string ReadExcerpt(Page page)
    {
        var md = page.RawMarkdown;
        var marker = md.IndexOf("<!-- more -->", StringComparison.OrdinalIgnoreCase);
        var region = marker > 0 ? md[..marker] : md;
        region = StripLeadingH1(region);

        // First non-heading paragraph from the region.
        string teaser = "";
        foreach (var para in region.Replace("\r\n", "\n").Split("\n\n"))
        {
            var trimmed = para.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#')) { teaser = trimmed; break; }
        }
        return CapLength(teaser, 320);
    }

    /// <summary>Removes a leading level-1 ATX (<c>#</c>) or Setext (underlined) heading.</summary>
    private static string StripLeadingH1(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").TrimStart('\n', ' ', '\t');
        var lines = text.Split('\n');
        if (lines.Length == 0) return text;

        if (lines[0].TrimStart().StartsWith("# ", StringComparison.Ordinal))
            return string.Join('\n', lines[1..]).TrimStart('\n');
        // Setext H1: a line followed by a line of '=' characters.
        if (lines.Length > 1 && lines[1].Trim().Length > 0 && lines[1].Trim().All(c => c == '='))
            return string.Join('\n', lines[2..]).TrimStart('\n');
        return text;
    }

    /// <summary>Caps a teaser at a word boundary near <paramref name="max"/> characters.</summary>
    private static string CapLength(string text, int max)
    {
        text = text.Trim();
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', Math.Min(max, text.Length - 1));
        if (cut <= 0) cut = max;
        return text[..cut].TrimEnd() + " …";
    }

    private static string Normalize(string dir) => dir.Trim('/').Length == 0 ? "" : dir.Trim('/') + "/";

    /// <summary>Rewrites relative links in an excerpt so they resolve from the generated listing
    /// page: <c>.md</c> page links become the target's published URL (via the page map), and
    /// relative image/file links become absolute source-based paths (the excerpt is embedded in a
    /// different directory than the post it came from).</summary>
    private string RewriteExcerptLinks(string markdown, string postRelativePath)
    {
        var dir = Path.GetDirectoryName(postRelativePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        return System.Text.RegularExpressions.Regex.Replace(markdown, @"(!?\[[^\]]*\]\()([^)\s]+)(\))", match =>
        {
            var pre = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            var post = match.Groups[3].Value;
            if (url.Contains("://") || url.StartsWith('/') || url.StartsWith('#') ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return match.Value;

            var hash = url.IndexOf('#');
            var anchor = hash >= 0 ? url[hash..] : "";
            var path = hash >= 0 ? url[..hash] : url;

            // A link to another page (.md): resolve it relative to the post's directory and map it
            // to the published URL. Leave it untouched if the target isn't a known page.
            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                var combinedMd = dir.Length == 0 ? path : dir + "/" + path;
                return _pageUrls.TryGetValue(NormalizeRel(combinedMd), out var target)
                    ? pre + target + anchor + post
                    : match.Value;
            }

            var isImage = pre.StartsWith('!');
            var lastSegment = path.Split('/')[^1];
            if (!isImage && !lastSegment.Contains('.')) return match.Value;

            var combined = dir.Length == 0 ? path : dir + "/" + path;
            return pre + "/" + NormalizeRel(combined) + anchor + post;
        });
    }

    private static string NormalizeRel(string path)
    {
        var parts = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(segment);
        }
        return string.Join('/', parts);
    }
}

public sealed record BlogPost(Page Page, DateTimeOffset Date, IReadOnlyList<string> Categories, string Excerpt);

public sealed record Author(string Id, string Name, string Role, string Avatar, string Url);
