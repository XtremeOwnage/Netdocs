using System.Text;
using System.Xml;
using Netdocs.Abstractions;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>Generates an RSS 2.0 feed from blog posts (created-date ordering).</summary>
public sealed class RssPlugin : IPlugin, IBuildHook
{
    private string _feedFile = "feed_rss_created.xml";
    private int _limit = 20;

    public string Name => "rss";

    public void Configure(IPluginContext ctx)
    {
        if (ctx.PluginOptions.TryGetValue("length", out var l) && l is long ll) _limit = (int)ll;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        if (site.State.GetValueOrDefault("blog_posts") is not List<BlogPost> posts || posts.Count == 0)
            return;

        var siteUrl = (site.Config.SiteUrl ?? "").TrimEnd('/');
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Async = true, Encoding = new UTF8Encoding(false) };
        await using var writer = XmlWriter.Create(sb, settings);

        await writer.WriteStartDocumentAsync();
        writer.WriteStartElement("rss");
        writer.WriteAttributeString("version", "2.0");
        writer.WriteStartElement("channel");
        writer.WriteElementString("title", site.Config.SiteName);
        writer.WriteElementString("link", siteUrl + "/");
        writer.WriteElementString("description", site.Config.SiteDescription ?? site.Config.SiteName);

        foreach (var post in posts.Take(_limit))
        {
            writer.WriteStartElement("item");
            writer.WriteElementString("title", post.Page.Title);
            writer.WriteElementString("link", $"{siteUrl}/{post.Page.Url}");
            writer.WriteElementString("guid", $"{siteUrl}/{post.Page.Url}");
            writer.WriteElementString("pubDate", post.Date.ToString("r"));
            foreach (var category in post.Categories)
                writer.WriteElementString("category", category);
            writer.WriteElementString("description", post.Excerpt);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();

        var path = Path.Combine(site.Config.AbsoluteSiteDir, _feedFile);
        await OutputWriter.WriteTextIfChangedAsync(site, path, sb.ToString(), ct);
    }
}
