using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Netdocs.Core.Markdown;

/// <summary>
/// Renders fenced code blocks in a Material-compatible structure and turns
/// mermaid fences into a pre.mermaid element for client-side rendering.
/// </summary>
public sealed class MaterialCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        var info = (block as FencedCodeBlock)?.Info?.Trim();
        var language = string.IsNullOrEmpty(info) ? null : info.Split(' ', '{')[0];

        if (string.Equals(language, "mermaid", StringComparison.OrdinalIgnoreCase))
        {
            renderer.EnsureLine();
            renderer.WriteLine("<pre class=\"mermaid\">");
            WriteRawLines(renderer, block);
            renderer.WriteLine("</pre>");
            return;
        }

        renderer.EnsureLine();
        renderer.Write("<div class=\"highlight\"><pre><span></span><code");
        if (!string.IsNullOrEmpty(language))
            renderer.Write(" class=\"language-").Write(language).Write('"');
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

public sealed class MaterialCodeBlockExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline) { }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html)
        {
            html.ObjectRenderers.RemoveAll(r => r is CodeBlockRenderer);
            html.ObjectRenderers.Insert(0, new MaterialCodeBlockRenderer());
        }
    }
}
