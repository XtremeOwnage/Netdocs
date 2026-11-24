using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Markdown;
using Netdocs.Core.Plugins;

namespace Netdocs.Core.Cms;

/// <summary>A page body rendered block by block for the visual editor.</summary>
public sealed record RenderedBlock(int Index, string Kind, string Source, string Html);

/// <summary>
/// Renders markdown for the CMS with the site's own pipeline (plugin preprocessors, Markdig
/// extensions, link rewriting), so what the editor shows matches the built page. Links and images
/// keep their original markdown target in a <c>data-md-href</c>/<c>data-md-src</c> attribute, so
/// the visual editor can write the original target back instead of the rewritten URL.
/// </summary>
public sealed partial class CmsRenderer
{
    private readonly SiteConfig _config;
    private readonly PluginHost _host;
    private readonly SiteContext _site;

    public CmsRenderer(SiteConfig config, BuildOptions options, PluginRegistry registry, ILoggerFactory loggerFactory)
    {
        _config = config;
        _site = new SiteContext { Config = config, Options = options, LoggerFactory = loggerFactory };
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(options);
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        ExternalPluginLoader.Load(registry, provider, config.ProjectRoot, loggerFactory.CreateLogger("ExternalPlugins"));
        _host = PluginHost.Build(config, options, BuildEngine.BuildEffectivePluginList(config), registry, provider, services, loggerFactory);
        Pipeline = MarkdownPipelineFactory.Build(_site, _host.MarkdigContributors);
    }

    /// <summary>The site's Markdig pipeline.</summary>
    public MarkdownPipeline Pipeline { get; }

    /// <summary>Docs-relative source path → output URL, from the most recent build. Used to
    /// rewrite <c>.md</c> links exactly as the build does.</summary>
    public IReadOnlyDictionary<string, string> LinkMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Renders a body in full, as the page's article content.</summary>
    public async Task<string> RenderAsync(string relativePath, IReadOnlyDictionary<string, object?> frontMatter, string body, CancellationToken ct = default)
    {
        var page = NewPage(relativePath, frontMatter, body);
        var markdown = await PreprocessAsync(page, body, ct);
        return RenderHtml(page, markdown);
    }

    /// <summary>Renders a body as separate blocks. The whole body is rendered in one pass (so
    /// footnotes, abbreviations and link references resolve across blocks), with a marker comment
    /// between top-level blocks that the output is then split on.</summary>
    public async Task<IReadOnlyList<RenderedBlock>> RenderBlocksAsync(string relativePath, IReadOnlyDictionary<string, object?> frontMatter, string body, CancellationToken ct = default)
    {
        var (prefix, blocks) = MarkdownBlocks.Split(body, Pipeline);
        var marked = new StringBuilder(prefix);
        foreach (var block in blocks)
            marked.Append("\n<!--cms-block:").Append(block.Index).Append("-->\n\n").Append(block.Source).Append("\n\n");

        var page = NewPage(relativePath, frontMatter, body);
        var markdown = await PreprocessAsync(page, marked.ToString(), ct);
        var html = RenderHtml(page, markdown);

        var pieces = new Dictionary<int, string>();
        var matches = Marker().Matches(html);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            pieces[int.Parse(matches[i].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] = html[start..end].Trim();
        }

        var result = new List<RenderedBlock>();
        foreach (var block in blocks)
        {
            var piece = pieces.GetValueOrDefault(block.Index, "");
            // Markdig appends the footnote list after the last block; show it on its own.
            if (block.Index == blocks.Count - 1 && block.Kind != "footnotes")
            {
                var fn = piece.IndexOf("<div class=\"footnotes\"", StringComparison.Ordinal);
                if (fn >= 0) piece = piece[..fn];
            }
            result.Add(new RenderedBlock(block.Index, block.Kind, block.Source, piece));
        }
        return result;
    }

    private Page NewPage(string relativePath, IReadOnlyDictionary<string, object?> frontMatter, string body)
    {
        var url = LinkMap.TryGetValue(relativePath, out var u) ? u : Content.ContentDiscovery.UrlFor(relativePath, _config.SlugifyUrls ? _config.Slugify : null);
        var page = new Page
        {
            SourcePath = Path.Combine(_config.AbsoluteDocsDir, relativePath),
            RelativePath = relativePath,
            RawMarkdown = body,
            FrontMatter = frontMatter,
            Url = url,
        };
        if (frontMatter.TryGetValue("title", out var t) && t is string title) page.Title = title;
        return page;
    }

    private async Task<string> PreprocessAsync(Page page, string markdown, CancellationToken ct)
    {
        foreach (var pre in _host.Preprocessors)
        {
            if (pre is IPlugin p && !PagePluginGate.IsEnabled(page, p.Name)) continue;
            try { markdown = await pre.ProcessAsync(page, markdown, _site, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* a broken snippet must not block editing */ }
        }
        return markdown;
    }

    private string RenderHtml(Page page, string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var basePrefix = PageRenderer.BaseUrl(page.Url);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (string.IsNullOrEmpty(link.Url)) continue;
            link.GetAttributes().AddProperty(link.IsImage ? "data-md-src" : "data-md-href", link.Url);
            if (!link.IsImage)
            {
                var md = DocumentRenderer.ResolveMarkdownLink(page.RelativePath, link.Url, LinkMap);
                if (md is not null) { link.Url = basePrefix + md; continue; }
            }
            var resource = DocumentRenderer.ResolveResourceLink(page.RelativePath, link.Url, link.IsImage);
            if (resource is not null) link.Url = basePrefix + resource;
        }

        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return sb.ToString();
    }

    [GeneratedRegex(@"<!--cms-block:(\d+)-->")]
    private static partial Regex Marker();
}
