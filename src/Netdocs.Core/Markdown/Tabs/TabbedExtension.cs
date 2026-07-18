using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Netdocs.Core.Markdown.Tabs;

public sealed class TabItemBlock(BlockParser parser) : ContainerBlock(parser)
{
    public required string Title { get; set; }
}

public sealed class TabbedSetBlock(BlockParser? parser) : ContainerBlock(parser)
{
    public int SetIndex { get; set; }
}

/// <summary>Parses pymdownx tabbed items: <c>=== "Title"</c> followed by indented content.</summary>
public sealed class TabItemBlockParser : BlockParser
{
    public TabItemBlockParser() => OpeningCharacters = ['='];

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent) return BlockState.None;
        var text = processor.Line.Text;
        var end = processor.Line.End;
        var pos = processor.Line.Start;

        var count = 0;
        while (pos <= end && text[pos] == '=') { pos++; count++; }
        if (count != 3) return BlockState.None;
        if (pos > end || text[pos] != ' ') return BlockState.None;
        while (pos <= end && text[pos] == ' ') pos++;
        if (pos > end || text[pos] != '"') return BlockState.None;
        pos++;
        var titleStart = pos;
        while (pos <= end && text[pos] != '"') pos++;
        var title = text.Substring(titleStart, pos - titleStart);

        processor.NewBlocks.Push(new TabItemBlock(this)
        {
            Title = title,
            Column = processor.Column,
            Span = { Start = processor.Start },
        });
        processor.Line.Start = processor.Line.End + 1;
        return BlockState.ContinueDiscard;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsBlankLine) return BlockState.Continue;
        if (processor.IsCodeIndent)
        {
            processor.GoToCodeIndent();
            return BlockState.Continue;
        }
        return BlockState.None;
    }

    public override bool Close(BlockProcessor processor, Block block)
    {
        block.Span.End = processor.Line.End;
        return true;
    }
}

public sealed class TabbedSetRenderer : HtmlObjectRenderer<TabbedSetBlock>
{
    protected override void Write(HtmlRenderer renderer, TabbedSetBlock set)
    {
        var items = set.OfType<TabItemBlock>().ToList();
        renderer.EnsureLine();
        renderer.Write("<div class=\"tabbed-set tabbed-alternate\" data-tabs=\"")
                .Write(set.SetIndex.ToString()).Write(':').Write(items.Count.ToString()).WriteLine("\">");

        for (var i = 0; i < items.Count; i++)
        {
            var id = $"__tabbed_{set.SetIndex}_{i + 1}";
            renderer.Write("<input ");
            if (i == 0) renderer.Write("checked=\"checked\" ");
            renderer.Write("id=\"").Write(id).Write("\" name=\"__tabbed_").Write(set.SetIndex.ToString())
                    .WriteLine("\" type=\"radio\" />");
        }

        renderer.WriteLine("<div class=\"tabbed-labels\">");
        for (var i = 0; i < items.Count; i++)
        {
            var id = $"__tabbed_{set.SetIndex}_{i + 1}";
            renderer.Write("<label for=\"").Write(id).Write("\">").Write(items[i].Title).WriteLine("</label>");
        }
        renderer.WriteLine("</div>");

        renderer.WriteLine("<div class=\"tabbed-content\">");
        foreach (var item in items)
        {
            renderer.WriteLine("<div class=\"tabbed-block\">");
            renderer.WriteChildren(item);
            renderer.WriteLine("</div>");
        }
        renderer.WriteLine("</div>");
        renderer.WriteLine("</div>");
    }
}

/// <summary>Groups adjacent tab items into sets and registers rendering.</summary>
public sealed class TabbedExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<TabItemBlockParser>())
            pipeline.BlockParsers.Insert(0, new TabItemBlockParser());
        pipeline.DocumentProcessed -= GroupTabs;
        pipeline.DocumentProcessed += GroupTabs;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<TabbedSetRenderer>())
            html.ObjectRenderers.Insert(0, new TabbedSetRenderer());
    }

    private static void GroupTabs(MarkdownDocument document)
    {
        var setCounter = 0;
        GroupIn(document, ref setCounter);
    }

    private static void GroupIn(ContainerBlock container, ref int setCounter)
    {
        for (var i = 0; i < container.Count; i++)
        {
            if (container[i] is TabItemBlock)
            {
                var run = new List<TabItemBlock>();
                var j = i;
                while (j < container.Count && container[j] is TabItemBlock item) { run.Add(item); j++; }

                setCounter++;
                var set = new TabbedSetBlock(null) { SetIndex = setCounter };
                foreach (var item in run)
                {
                    container.Remove(item);
                    set.Add(item);
                }
                container.Insert(i, set);
            }
            else if (container[i] is ContainerBlock child and not TabbedSetBlock)
            {
                GroupIn(child, ref setCounter);
            }
        }
    }
}
