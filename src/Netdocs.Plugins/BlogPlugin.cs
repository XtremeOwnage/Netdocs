using System.Globalization;
using System.Text;
using Netdocs.Abstractions;
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
    }

    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        var postsPrefix = _blogDir + "posts/";
        foreach (var page in site.Pages)
        {
            if (!page.RelativePath.StartsWith(postsPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var date = ReadDate(page);
            var slug = Slug.Make(Path.GetFileNameWithoutExtension(page.RelativePath));
            var url = $"{_blogDir}{date.ToString(_urlDateFormat, CultureInfo.InvariantCulture)}/{slug}/";
            page.Url = url;
            page.OutputPath = Path.Combine(site.Config.AbsoluteSiteDir, ContentDiscovery.OutputFileFor(url));
            page.Created = date;

            var categories = GetCategories(page);
            _posts.Add(new BlogPost(page, date, categories, ReadExcerpt(page)));
            page.RawMarkdown = PostMetaHtml(date, categories, page.RawMarkdown) + page.RawMarkdown;
        }

        _posts.Sort((a, b) => b.Date.CompareTo(a.Date));
        site.State["blog_posts"] = _posts;

        // Turn the existing blog index into page 1 of the paginated list.
        var index = site.Pages.FirstOrDefault(p =>
            p.RelativePath.Equals(_blogDir + "index.md", StringComparison.OrdinalIgnoreCase));
        if (index is not null)
        {
            var pageItems = _posts.Take(_perPage).ToList();
            index.RawMarkdown = RenderList("Blog", pageItems, _posts.Count > _perPage ? $"{_blogDir}page/2/" : null);
            index.Title = "Blog";
        }

        return Task.CompletedTask;
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
            yield return Generated(site, url, $"Blog - Page {p}", RenderList($"Blog - Page {p}", items, next));
        }

        // Category pages.
        if (_categoriesEnabled)
        {
            foreach (var group in _posts
                .SelectMany(post => post.Categories.Select(cat => (cat, post)))
                .GroupBy(x => x.cat, StringComparer.OrdinalIgnoreCase))
            {
                var slug = Slug.Make(group.Key);
                var url = $"{_blogDir}category/{slug}/";
                yield return Generated(site, url, group.Key,
                    RenderList($"Category: {group.Key}", group.Select(x => x.post).ToList(), null));
            }
        }

        // Archive pages by year.
        if (_archiveEnabled)
        {
            foreach (var group in _posts.GroupBy(post => post.Date.Year).OrderByDescending(g => g.Key))
            {
                var url = $"{_blogDir}archive/{group.Key}/";
                yield return Generated(site, url, group.Key.ToString(),
                    RenderList($"Archive: {group.Key}", group.ToList(), null));
            }
        }
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
        foreach (var post in posts)
        {
            var rootUrl = "/" + post.Page.Url;
            sb.Append("## [").Append(post.Page.Title).Append("](").Append(rootUrl).AppendLine(")");
            sb.Append("*").Append(post.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)).Append("*");
            if (post.Categories.Count > 0)
                sb.Append(" &middot; ").Append(string.Join(", ", post.Categories));
            sb.AppendLine().AppendLine();
            if (post.Excerpt.Length > 0) sb.AppendLine(RewriteExcerptLinks(post.Excerpt, post.Page.RelativePath)).AppendLine();
            sb.Append("[Continue reading](").Append(rootUrl).AppendLine(")").AppendLine();
        }
        if (nextPageUrl is not null)
            sb.Append("[Older posts →](/").Append(nextPageUrl).AppendLine(")");
        return sb.ToString();
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

    /// <summary>Small metadata header (date · reading time · categories) prepended to a post.</summary>
    private static string PostMetaHtml(DateTimeOffset date, IReadOnlyList<string> categories, string markdown)
    {
        var words = markdown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var minutes = Math.Max(1, (int)Math.Round(words / 238.0));
        var cats = categories.Count > 0 ? " &middot; in " + string.Join(", ", categories) : "";
        return $"""
            <div class="md-post__meta md-typeset" style="color:var(--md-default-fg-color--light);font-size:.72rem;margin-bottom:1rem">
            <time datetime="{date:yyyy-MM-dd}">{date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}</time> &middot; {minutes} min read{cats}
            </div>


            """;
    }

    private static string ReadExcerpt(Page page)
    {
        var md = page.RawMarkdown;
        var marker = md.IndexOf("<!-- more -->", StringComparison.OrdinalIgnoreCase);
        if (marker > 0) return md[..marker].Trim();

        // First non-heading paragraph.
        foreach (var para in md.Replace("\r\n", "\n").Split("\n\n"))
        {
            var trimmed = para.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#')) return trimmed;
        }
        return "";
    }

    private static string Normalize(string dir) => dir.Trim('/').Length == 0 ? "" : dir.Trim('/') + "/";

    /// <summary>Rewrites relative image/file links in an excerpt to absolute source-based paths,
    /// since the excerpt is embedded in the generated list page (a different directory).</summary>
    private static string RewriteExcerptLinks(string markdown, string postRelativePath)
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
            if (url.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return match.Value;

            var isImage = pre.StartsWith('!');
            var lastSegment = url.Split('/')[^1];
            if (!isImage && !lastSegment.Contains('.')) return match.Value;

            var combined = dir.Length == 0 ? url : dir + "/" + url;
            return pre + "/" + NormalizeRel(combined) + post;
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
