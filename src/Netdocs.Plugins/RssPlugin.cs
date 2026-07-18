using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Netdocs.Abstractions;
using Netdocs.Core.Configuration;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>
/// Generates an RSS 2.0 feed (and, optionally, an Atom 1.0 feed) from blog posts, ordered by
/// creation date. Supports per-post title/description/image overrides via front matter, a
/// channel image, and full-content items. Requires the blog plugin to have collected posts.
/// </summary>
public sealed partial class RssPlugin : IPlugin, IBuildHook
{
    private const string ContentNs = "http://purl.org/rss/1.0/modules/content/";
    private const string AtomNs = "http://www.w3.org/2005/Atom";

    private string _rssFile = "feed_rss_created.xml";
    private string _atomFile = "feed_atom_created.xml";
    private bool _atom;
    private int _limit = 20;
    private bool _fullContent;
    private string? _feedTitle;
    private string? _feedDescription;
    private string? _channelImage;
    private int _ttl;

    public string Name => "rss";

    public void Configure(IPluginContext ctx)
    {
        var o = ctx.PluginOptions;
        // `length` (mkdocs-rss) with `limit` accepted as an alias.
        if (o.Get("length") is { } len) _limit = len.AsInt(_limit);
        else if (o.Get("limit") is { } lim) _limit = lim.AsInt(_limit);

        if (o.Get("rss_file").AsString() is { Length: > 0 } rf) _rssFile = rf;
        if (o.Get("atom_file").AsString() is { Length: > 0 } af) _atomFile = af;
        _atom = o.Get("atom").AsBool(_atom);
        _fullContent = o.Get("full_content").AsBool(_fullContent);
        _feedTitle = o.Get("feed_title").AsString();
        _feedDescription = o.Get("feed_description").AsString();
        _channelImage = o.Get("image").AsString();
        _ttl = o.Get("ttl").AsInt(0);

        // `social_icon: true` surfaces the feed as an RSS icon in the header/footer social row.
        // `social_feed: atom` links the Atom feed instead of the default RSS feed.
        if (o.Get("social_icon").AsBool(false))
        {
            var useAtom = string.Equals(o.Get("social_feed").AsString(), "atom", StringComparison.OrdinalIgnoreCase);
            var feedFile = useAtom ? _atomFile : _rssFile;
            var siteUrl = (ctx.Config.SiteUrl ?? "").TrimEnd('/');
            var link = siteUrl.Length > 0 ? $"{siteUrl}/{feedFile}" : "/" + feedFile;
            AddSocialEntry(ctx.Config, "fontawesome/solid/rss", link);
        }
    }

    /// <summary>Appends a <c>{ icon, link }</c> entry to <c>extra.social</c> so the theme renders it
    /// alongside the other social links. Config's <c>extra</c> map is treated as immutable, so a
    /// shallow copy is written back with the augmented social list.</summary>
    private static void AddSocialEntry(SiteConfig config, string icon, string link)
    {
        var extra = new Dictionary<string, object?>(config.Extra, StringComparer.OrdinalIgnoreCase);
        var social = extra.TryGetValue("social", out var existing)
            ? new List<object?>(existing.AsList())
            : [];
        social.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["icon"] = icon,
            ["link"] = link,
        });
        extra["social"] = social;
        config.Extra = extra;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        if (site.State.GetValueOrDefault("blog_posts") is not List<BlogPost> posts || posts.Count == 0)
            return;

        var siteUrl = (site.Config.SiteUrl ?? "").TrimEnd('/');
        var items = posts.Take(_limit).Select(p => ToItem(p, siteUrl)).ToList();

        var rss = BuildRss(site, siteUrl, items);
        await OutputWriter.WriteTextIfChangedAsync(site, Path.Combine(site.Config.AbsoluteSiteDir, _rssFile), rss, ct);

        if (_atom)
        {
            var atom = BuildAtom(site, siteUrl, items);
            await OutputWriter.WriteTextIfChangedAsync(site, Path.Combine(site.Config.AbsoluteSiteDir, _atomFile), atom, ct);
        }
    }

    private FeedItem ToItem(BlogPost post, string siteUrl)
    {
        var page = post.Page;
        var url = $"{siteUrl}/{page.Url}";
        var title = FrontMatterString(page, "rss_title") ?? page.Title;
        var description = FrontMatterString(page, "rss_description") ?? post.Excerpt;
        var image = ResolveImage(page, url, siteUrl);
        var content = _fullContent && page.HtmlContent.Length > 0 ? page.HtmlContent : null;
        return new FeedItem(title, url, post.Date, post.Categories, description, image, content);
    }

    private string BuildRss(SiteContext site, string siteUrl, List<FeedItem> items)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, XmlSettings());

        writer.WriteStartDocument();
        writer.WriteStartElement("rss");
        writer.WriteAttributeString("version", "2.0");
        writer.WriteAttributeString("xmlns", "atom", null, AtomNs);
        writer.WriteAttributeString("xmlns", "content", null, ContentNs);
        writer.WriteStartElement("channel");

        writer.WriteElementString("title", _feedTitle ?? site.Config.SiteName);
        writer.WriteElementString("link", siteUrl + "/");
        writer.WriteElementString("description", _feedDescription ?? site.Config.SiteDescription ?? site.Config.SiteName);
        if (items.Count > 0)
            writer.WriteElementString("lastBuildDate", items[0].Date.ToString("r"));
        if (_ttl > 0)
            writer.WriteElementString("ttl", _ttl.ToString());

        // atom:link rel="self" is recommended so readers can find the canonical feed URL.
        writer.WriteStartElement("atom", "link", AtomNs);
        writer.WriteAttributeString("href", $"{siteUrl}/{_rssFile}");
        writer.WriteAttributeString("rel", "self");
        writer.WriteAttributeString("type", "application/rss+xml");
        writer.WriteEndElement();

        if (!string.IsNullOrEmpty(_channelImage))
        {
            writer.WriteStartElement("image");
            writer.WriteElementString("url", AbsoluteUrl(_channelImage!, siteUrl));
            writer.WriteElementString("title", _feedTitle ?? site.Config.SiteName);
            writer.WriteElementString("link", siteUrl + "/");
            writer.WriteEndElement();
        }

        foreach (var item in items)
        {
            writer.WriteStartElement("item");
            writer.WriteElementString("title", item.Title);
            writer.WriteElementString("link", item.Url);
            writer.WriteStartElement("guid");
            writer.WriteAttributeString("isPermaLink", "true");
            writer.WriteString(item.Url);
            writer.WriteEndElement();
            writer.WriteElementString("pubDate", item.Date.ToString("r"));
            foreach (var category in item.Categories)
                writer.WriteElementString("category", category);
            writer.WriteElementString("description", item.Description);
            if (item.Content is { } html)
                writer.WriteElementString("encoded", ContentNs, html);
            if (item.Image is { } img)
            {
                writer.WriteStartElement("enclosure");
                writer.WriteAttributeString("url", img);
                writer.WriteAttributeString("type", MimeForImage(img));
                writer.WriteAttributeString("length", "0");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        return sb.ToString();
    }

    private string BuildAtom(SiteContext site, string siteUrl, List<FeedItem> items)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, XmlSettings());

        writer.WriteStartDocument();
        writer.WriteStartElement("feed", AtomNs);
        writer.WriteElementString("title", _feedTitle ?? site.Config.SiteName);
        writer.WriteElementString("subtitle", _feedDescription ?? site.Config.SiteDescription ?? site.Config.SiteName);
        writer.WriteElementString("id", siteUrl + "/");
        writer.WriteElementString("updated", (items.Count > 0 ? items[0].Date : DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:sszzz"));

        writer.WriteStartElement("link");
        writer.WriteAttributeString("href", siteUrl + "/");
        writer.WriteAttributeString("rel", "alternate");
        writer.WriteEndElement();
        writer.WriteStartElement("link");
        writer.WriteAttributeString("href", $"{siteUrl}/{_atomFile}");
        writer.WriteAttributeString("rel", "self");
        writer.WriteEndElement();

        foreach (var item in items)
        {
            writer.WriteStartElement("entry");
            writer.WriteElementString("title", item.Title);
            writer.WriteElementString("id", item.Url);
            writer.WriteElementString("updated", item.Date.ToString("yyyy-MM-ddTHH:mm:sszzz"));
            writer.WriteElementString("published", item.Date.ToString("yyyy-MM-ddTHH:mm:sszzz"));
            writer.WriteStartElement("link");
            writer.WriteAttributeString("href", item.Url);
            writer.WriteAttributeString("rel", "alternate");
            writer.WriteEndElement();
            foreach (var category in item.Categories)
            {
                writer.WriteStartElement("category");
                writer.WriteAttributeString("term", category);
                writer.WriteEndElement();
            }
            if (item.Content is { } html)
            {
                writer.WriteStartElement("content");
                writer.WriteAttributeString("type", "html");
                writer.WriteString(html);
                writer.WriteEndElement();
            }
            else
            {
                writer.WriteElementString("summary", item.Description);
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        return sb.ToString();
    }

    private static XmlWriterSettings XmlSettings() =>
        new() { Indent = true, Encoding = new UTF8Encoding(false) };

    private static string? FrontMatterString(Page page, string key) =>
        page.FrontMatter.TryGetValue(key, out var v) ? v?.ToString() : null;

    /// <summary>
    /// Resolves a post image: front-matter <c>image</c> when present, otherwise the first
    /// <c>&lt;img src&gt;</c> found in the rendered content. Relative URLs are made absolute.
    /// </summary>
    private static string? ResolveImage(Page page, string postUrl, string siteUrl)
    {
        var image = FrontMatterString(page, "image");
        if (string.IsNullOrEmpty(image))
        {
            var match = FirstImage().Match(page.HtmlContent);
            if (match.Success) image = match.Groups[1].Value;
        }
        if (string.IsNullOrEmpty(image)) return null;

        if (image.Contains("://")) return image;
        if (image.StartsWith('/')) return siteUrl + image;
        // Relative to the post's output directory.
        return postUrl.TrimEnd('/') + "/" + image;
    }

    private static string AbsoluteUrl(string url, string siteUrl) =>
        url.Contains("://") ? url : siteUrl + "/" + url.TrimStart('/');

    private static string MimeForImage(string url) =>
        Path.GetExtension(url).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".avif" => "image/avif",
            _ => "image/jpeg",
        };

    [GeneratedRegex("""<img[^>]+src=["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex FirstImage();

    private sealed record FeedItem(
        string Title,
        string Url,
        DateTimeOffset Date,
        IReadOnlyList<string> Categories,
        string Description,
        string? Image,
        string? Content);
}
