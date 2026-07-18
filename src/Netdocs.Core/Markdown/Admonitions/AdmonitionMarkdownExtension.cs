using Markdig;
using Markdig.Renderers;

namespace Netdocs.Core.Markdown.Admonitions;

/// <summary>Registers the admonition block parser + HTML renderer.</summary>
public sealed class AdmonitionExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<AdmonitionBlockParser>())
            pipeline.BlockParsers.Insert(0, new AdmonitionBlockParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<AdmonitionRenderer>())
            html.ObjectRenderers.Insert(0, new AdmonitionRenderer());
    }
}
