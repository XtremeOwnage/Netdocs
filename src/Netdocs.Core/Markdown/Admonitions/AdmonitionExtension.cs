using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Netdocs.Core.Markdown.Admonitions;

/// <summary>A parsed admonition/details block (pymdownx <c>!!!</c>, <c>???</c>, <c>???+</c>).</summary>
public sealed class AdmonitionBlock(BlockParser parser) : ContainerBlock(parser)
{
    public required string Kind { get; init; }
    public string? Title { get; set; }
    public bool Collapsible { get; init; }
    public bool Open { get; init; }
}

/// <summary>
/// Parses Material/pymdownx admonitions:
/// <c>!!! note "Title"</c> (static), <c>??? note</c> (collapsed), <c>???+ note</c> (open).
/// Body is the 4-space (or tab) indented block that follows.
/// </summary>
public sealed class AdmonitionBlockParser : BlockParser
{
    public AdmonitionBlockParser() => OpeningCharacters = ['!', '?'];

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent) return BlockState.None;

        var line = processor.Line;
        var start = line.Start;
        var text = line.Text;
        var end = line.End; // inclusive index of the last char on this line
        var pos = start;

        var marker = text[pos];
        var count = 0;
        while (pos <= end && text[pos] == marker) { pos++; count++; }
        if (count != 3) return BlockState.None;

        var collapsible = marker == '?';
        var open = false;
        if (collapsible && pos <= end && text[pos] == '+') { open = true; pos++; }

        // Require a space after the marker.
        if (pos > end || text[pos] != ' ') return BlockState.None;
        while (pos <= end && text[pos] == ' ') pos++;

        // Read the kind keyword(s) until a quote or end of line.
        var kindStart = pos;
        while (pos <= end && text[pos] != '"') pos++;
        var kind = text.Substring(kindStart, pos - kindStart).Trim();
        if (kind.Length == 0) kind = "note";

        string? title = null;
        if (pos <= end && text[pos] == '"')
        {
            pos++;
            var titleStart = pos;
            while (pos <= end && text[pos] != '"') pos++;
            title = text.Substring(titleStart, pos - titleStart);
        }

        var block = new AdmonitionBlock(this)
        {
            Kind = kind,
            Title = title,
            Collapsible = collapsible,
            Open = open,
            Column = processor.Column,
            Span = { Start = processor.Start },
        };
        processor.NewBlocks.Push(block);
        // Consume the whole opening line.
        processor.Line.Start = processor.Line.End + 1;
        return BlockState.ContinueDiscard;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsBlankLine) return BlockState.Continue;

        // Content belongs to the admonition while indented by at least a code indent (4 cols).
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

public sealed class AdmonitionRenderer : HtmlObjectRenderer<AdmonitionBlock>
{
    protected override void Write(HtmlRenderer renderer, AdmonitionBlock block)
    {
        var kind = block.Kind.ToLowerInvariant();
        var classes = string.Join(' ', kind.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var title = block.Title ?? Capitalize(block.Kind.Split(' ')[0]);

        renderer.EnsureLine();
        if (block.Collapsible)
        {
            renderer.Write("<details class=\"admonition ").Write(classes).Write('"');
            if (block.Open) renderer.Write(" open");
            renderer.WriteLine('>');
            if (title.Length > 0)
                renderer.Write("<summary>").Write(title).WriteLine("</summary>");
        }
        else
        {
            renderer.Write("<div class=\"admonition ").Write(classes).WriteLine("\">");
            if (title.Length > 0)
                renderer.Write("<p class=\"admonition-title\">").Write(title).WriteLine("</p>");
        }

        renderer.WriteChildren(block);
        renderer.WriteLine(block.Collapsible ? "</details>" : "</div>");
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
