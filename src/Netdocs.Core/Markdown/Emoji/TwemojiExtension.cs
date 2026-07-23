using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;
using EmojiMapping = Markdig.Extensions.Emoji.EmojiMapping;

namespace Netdocs.Core.Markdown.Emoji;

/// <summary>Inline node for an emoji shortcode resolved to its Unicode sequence.</summary>
public sealed class TwemojiInline : LeafInline
{
    public required string Unicode { get; init; }
    public required string Shortcode { get; init; }
}

/// <summary>Inline node for an icon shortcode (<c>:material-*:</c> etc.) resolved to inline SVG.</summary>
public sealed class IconInline : LeafInline
{
    public required string Name { get; init; }
    public required string Svg { get; init; }
}

/// <summary>
/// Parses <c>:shortcode:</c> emoji using Markdig's built-in shortcode table and emits a
/// <see cref="TwemojiInline"/> so it can be rendered as a Twemoji SVG image (mirrors
/// mkdocs-material's <c>pymdownx.emoji</c> + twemoji generator). Shortcodes that aren't
/// emoji but resolve to a bundled icon (<c>:material-*:</c>, <c>:octicons-*:</c>,
/// <c>:fontawesome-*:</c>) emit an <see cref="IconInline"/> with inline SVG.
/// </summary>
public sealed class TwemojiInlineParser : InlineParser
{
    private static readonly IDictionary<string, string> Map = EmojiMapping.GetDefaultEmojiShortcodeToUnicode();

    public TwemojiInlineParser() => OpeningCharacters = [':'];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // Require a boundary before the opening ':' so we don't fire inside words/URLs.
        var previous = slice.PeekCharExtra(-1);
        if (previous.IsAlphaNumeric()) return false;

        var text = slice.Text;
        var start = slice.Start;
        var end = slice.End;
        var p = start + 1;
        while (p <= end)
        {
            var c = text[p];
            if (c == ':') break;
            if (!(char.IsLetterOrDigit(c) || c is '_' or '+' or '-')) return false;
            p++;
        }
        if (p > end || text[p] != ':') return false;

        var shortcode = text.Substring(start, p - start + 1);
        var startPosition = processor.GetSourcePosition(start, out var line, out var column);

        if (Map.TryGetValue(shortcode, out var unicode))
        {
            processor.Inline = new TwemojiInline
            {
                Unicode = unicode,
                Shortcode = shortcode,
                Span = new(startPosition, processor.GetSourcePosition(p)),
                Line = line,
                Column = column,
            };
            slice.Start = p + 1;
            return true;
        }

        // Not a Unicode emoji — try the bundled icon set (material/octicons/fontawesome).
        var name = shortcode.Trim(':');
        if (IconRegistry.IsIconName(name) && IconRegistry.RenderSvg(name) is { } svg)
        {
            processor.Inline = new IconInline
            {
                Name = name,
                Svg = svg,
                Span = new(startPosition, processor.GetSourcePosition(p)),
                Line = line,
                Column = column,
            };
            slice.Start = p + 1;
            return true;
        }

        return false;
    }
}

/// <summary>Renders an <see cref="IconInline"/> as an inline SVG wrapped in a
/// <c>&lt;span class="twemoji"&gt;</c>, carrying any attr_list classes/attributes
/// (e.g. <c>{ .lg .middle }</c>) so mkdocs-material icon usage renders identically.</summary>
public sealed class IconRenderer : HtmlObjectRenderer<IconInline>
{
    protected override void Write(HtmlRenderer renderer, IconInline obj)
    {
        if (!renderer.EnableHtmlForInline) return;

        var attrs = obj.TryGetAttributes();
        renderer.Write("<span class=\"twemoji");
        if (attrs?.Classes is { Count: > 0 } classes)
            foreach (var c in classes)
                renderer.Write(' ').Write(c);
        renderer.Write('"');
        if (!string.IsNullOrEmpty(attrs?.Id))
            renderer.Write(" id=\"").Write(attrs!.Id).Write('"');
        if (attrs?.Properties is { Count: > 0 } props)
            foreach (var prop in props)
                renderer.Write(' ').Write(prop.Key).Write("=\"").Write(prop.Value ?? "").Write('"');
        renderer.Write('>').Write(obj.Svg).Write("</span>");
    }
}

/// <summary>Renders a <see cref="TwemojiInline"/> as a Twemoji SVG <c>&lt;img&gt;</c>.</summary>
public sealed class TwemojiRenderer(string baseUrl) : HtmlObjectRenderer<TwemojiInline>
{
    protected override void Write(HtmlRenderer renderer, TwemojiInline obj)
    {
        if (!renderer.EnableHtmlForInline)
        {
            renderer.Write(obj.Unicode);
            return;
        }

        var icon = ToFileName(obj.Unicode);
        renderer.Write("<img class=\"twemoji\" alt=\"").Write(obj.Unicode)
                .Write("\" title=\"").Write(obj.Shortcode)
                .Write("\" src=\"").Write(baseUrl).Write(icon).Write(".svg\" />");
    }

    /// <summary>
    /// Mirrors twemoji's <c>grabTheRightIcon</c>: strip the U+FE0F variation selector unless the
    /// emoji is a ZWJ sequence, then join code points with '-' as lowercase hex.
    /// </summary>
    public static string ToFileName(string emoji)
    {
        var source = emoji.Contains('\u200d') ? emoji : emoji.Replace("\ufe0f", string.Empty);
        var sb = new StringBuilder();
        var i = 0;
        while (i < source.Length)
        {
            var cp = char.ConvertToUtf32(source, i);
            if (sb.Length > 0) sb.Append('-');
            sb.Append(cp.ToString("x", CultureInfo.InvariantCulture));
            i += char.IsSurrogatePair(source, i) ? 2 : 1;
        }
        return sb.ToString();
    }
}

/// <summary>Markdig extension wiring the Twemoji parser + renderer with a configurable asset base.</summary>
public sealed class TwemojiExtension(string baseUrl) : IMarkdownExtension
{
    /// <summary>jsDelivr-hosted Twemoji (jdecked fork) SVG assets, pinned for reproducibility.</summary>
    public const string DefaultBaseUrl = "https://cdn.jsdelivr.net/gh/jdecked/twemoji@17.0.3/assets/svg/";

    private readonly string _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl;

    public TwemojiExtension() : this(DefaultBaseUrl) { }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<TwemojiInlineParser>())
            pipeline.InlineParsers.Insert(0, new TwemojiInlineParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html)
        {
            if (!html.ObjectRenderers.Contains<TwemojiRenderer>())
                html.ObjectRenderers.Insert(0, new TwemojiRenderer(_baseUrl));
            if (!html.ObjectRenderers.Contains<IconRenderer>())
                html.ObjectRenderers.Insert(0, new IconRenderer());
        }
    }
}
