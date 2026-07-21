using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Netdocs.Core.Markdown;

/// <summary>
/// Renders fenced code blocks in a Material-compatible structure and turns
/// mermaid fences into a pre.mermaid element for client-side rendering. Parses the
/// pymdownx.highlight fence options that the source site relies on
/// (<c>linenums="N"</c>, <c>hl_lines="…"</c>, <c>title="…"</c>) and emits them as data
/// attributes the client highlighter consumes.
/// </summary>
public sealed class MaterialCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        var fenced = block as FencedCodeBlock;
        var info = fenced?.Info?.Trim() ?? "";
        var args = fenced?.Arguments?.Trim();
        if (!string.IsNullOrEmpty(args)) info = (info + " " + args).Trim();
        var opts = FenceOptions.Parse(info).MergeAttributes(block.TryGetAttributes());

        if (string.Equals(opts.Language, "mermaid", StringComparison.OrdinalIgnoreCase))
        {
            renderer.EnsureLine();
            renderer.WriteLine("<pre class=\"mermaid\">");
            WriteRawLines(renderer, block);
            renderer.WriteLine("</pre>");
            return;
        }

        renderer.EnsureLine();
        renderer.Write("<div class=\"highlight\">");
        if (!string.IsNullOrEmpty(opts.Title))
            renderer.Write("<span class=\"filename\">").WriteEscape(opts.Title).Write("</span>");

        renderer.Write("<pre><span></span><code");
        if (!string.IsNullOrEmpty(opts.Language))
            renderer.Write(" class=\"language-").Write(opts.Language).Write('"');
        if (opts.LineNumbersStart is { } start)
            renderer.Write(" data-linenums-start=\"").Write(start.ToString()).Write('"');
        if (!string.IsNullOrEmpty(opts.HlLines))
            renderer.Write(" data-hl-lines=\"").WriteEscape(opts.HlLines).Write('"');
        renderer.Write('>');
        WriteEscapedLines(renderer, block);
        renderer.WriteLine("</code></pre></div>");
    }

    private static void WriteRawLines(HtmlRenderer renderer, LeafBlock block)
    {
        var lines = block.Lines.Lines;
        for (var i = 0; i < block.Lines.Count; i++)
            renderer.WriteLine(lines[i].Slice.ToString());
    }

    private static void WriteEscapedLines(HtmlRenderer renderer, LeafBlock block)
    {
        var lines = block.Lines.Lines;
        for (var i = 0; i < block.Lines.Count; i++)
        {
            renderer.WriteEscape(lines[i].Slice.AsSpan());
            renderer.WriteLine();
        }
    }
}

/// <summary>Parsed subset of pymdownx.highlight fence attributes.</summary>
internal readonly struct FenceOptions
{
    public string? Language { get; init; }
    public int? LineNumbersStart { get; init; }
    public string? HlLines { get; init; }
    public string? Title { get; init; }

    /// <summary>
    /// Handles both the bare form (<c>py linenums="1"</c>) and the attr-list brace form
    /// (<c>{ .python .no-copy linenums="1" hl_lines="2 4-5" title="x" }</c>).
    /// </summary>
    public static FenceOptions Parse(string info)
    {
        if (string.IsNullOrEmpty(info)) return default;

        var body = info;
        var brace = info.IndexOf('{');
        if (brace >= 0)
        {
            var end = info.IndexOf('}', brace + 1);
            body = end > brace ? info.Substring(brace + 1, end - brace - 1) : info[(brace + 1)..];
        }

        string? language = null;
        int? linenumsStart = null;
        string? hlLines = null;
        string? title = null;

        foreach (var token in Tokenize(body))
        {
            var eq = token.IndexOf('=');
            if (eq < 0)
            {
                // A class (.python) or bare language name; first one wins.
                var name = token.TrimStart('.');
                if (name.Length == 0) continue;
                if (name.Equals("linenums", StringComparison.OrdinalIgnoreCase)) { linenumsStart ??= 1; continue; }
                language ??= name;
                continue;
            }

            var key = token[..eq].TrimStart('.').Trim();
            var value = token[(eq + 1)..].Trim().Trim('"', '\'');
            switch (key.ToLowerInvariant())
            {
                case "linenums":
                    linenumsStart = int.TryParse(value.Split(' ')[0], out var n) ? n : 1;
                    break;
                case "hl_lines":
                    hlLines = value;
                    break;
                case "title":
                    title = value;
                    break;
            }
        }

        return new FenceOptions
        {
            Language = language,
            LineNumbersStart = linenumsStart,
            HlLines = hlLines,
            Title = title,
        };
    }

    /// <summary>Fills any options not already set from attr_list attributes (the brace form,
    /// which Markdig's GenericAttributes captures separately from Info/Arguments).</summary>
    public FenceOptions MergeAttributes(Markdig.Renderers.Html.HtmlAttributes? attr)
    {
        if (attr is null) return this;

        var language = Language;
        if (language is null && attr.Classes is { Count: > 0 } classes)
            language = classes[0];

        var linenumsStart = LineNumbersStart;
        var hlLines = HlLines;
        var title = Title;
        if (attr.Properties is { } props)
        {
            foreach (var (key, value) in props)
            {
                var v = value?.Trim('"', '\'') ?? "";
                switch (key.ToLowerInvariant())
                {
                    case "linenums":
                        linenumsStart ??= int.TryParse(v.Split(' ')[0], out var n) ? n : 1;
                        break;
                    case "hl_lines":
                        hlLines ??= v;
                        break;
                    case "title":
                        title ??= v;
                        break;
                }
            }
        }

        return new FenceOptions
        {
            Language = language,
            LineNumbersStart = linenumsStart,
            HlLines = hlLines,
            Title = title,
        };
    }

    /// <summary>Splits on whitespace while keeping quoted values (e.g. <c>title="a b"</c>) intact.</summary>
    private static IEnumerable<string> Tokenize(string s)
    {
        var sb = new StringBuilder();
        var quote = '\0';
        foreach (var ch in s)
        {
            if (quote != '\0')
            {
                sb.Append(ch);
                if (ch == quote) quote = '\0';
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}

/// <summary>
/// Adds pymdownx.inlinehilite parity: an inline code span beginning with a
/// <c>#!lang</c> shebang (e.g. <c>`#!python range(10)`</c>) is emitted as
/// <c>&lt;code class="language-lang"&gt;</c> so the client highlighter colors it.
/// </summary>
public sealed class InlineHiliteRenderer : HtmlObjectRenderer<CodeInline>
{
    protected override void Write(HtmlRenderer renderer, CodeInline code)
    {
        var content = code.Content;
        string? language = null;

        if (content.StartsWith("#!", StringComparison.Ordinal))
        {
            var space = content.IndexOf(' ');
            if (space > 2)
            {
                language = content[2..space];
                content = content[(space + 1)..];
            }
        }

        if (renderer.EnableHtmlForInline)
        {
            renderer.Write("<code");
            if (language is { Length: > 0 })
                renderer.Write(" class=\"language-").Write(language).Write('"');
            renderer.Write('>');
        }
        renderer.WriteEscape(content);
        if (renderer.EnableHtmlForInline)
            renderer.Write("</code>");
    }
}

public sealed class MaterialCodeBlockExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline) { }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html)
        {
            html.ObjectRenderers.RemoveAll(r => r is CodeBlockRenderer);
            html.ObjectRenderers.Insert(0, new MaterialCodeBlockRenderer());

            html.ObjectRenderers.RemoveAll(r => r is CodeInlineRenderer);
            html.ObjectRenderers.Insert(0, new InlineHiliteRenderer());
        }
    }
}
