using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Netdocs.Core.Markdown.Admonitions;
using Netdocs.Core.Markdown.Tabs;

namespace Netdocs.Core.Cms;

/// <summary>One top-level block of a page's markdown body, with its exact source text.</summary>
/// <param name="Index">Position in the page.</param>
/// <param name="Kind">heading, paragraph, list, quote, code, table, html, hr, admonition, tabs,
/// footnotes, references, abbreviations, math, or another Markdig block name.</param>
/// <param name="Source">The block's markdown, exactly as in the file (no trailing blank lines).</param>
/// <param name="Gap">The blank lines that followed the block in the file.</param>
public sealed record MarkdownBlock(int Index, string Kind, string Source, string Gap);

/// <summary>An edit to a page body, expressed as the new block order. Each entry either keeps an
/// original block untouched (<see cref="Keep"/>) or supplies new markdown (<see cref="Source"/>).</summary>
public sealed record BlockEdit(int? Keep, string? Source);

/// <summary>
/// Splits a markdown body into top-level blocks and splices edited blocks back in. Blocks that
/// were not edited are written back byte for byte, so a visual edit of one paragraph changes only
/// that paragraph in the file.
/// </summary>
public static partial class MarkdownBlocks
{
    public static (string Prefix, IReadOnlyList<MarkdownBlock> Blocks) Split(string body, MarkdownPipeline pipeline)
    {
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var document = Markdig.Markdown.Parse(normalized, pipeline);

        // Start line of every top-level block, in order. Footnote groups are collected by Markdig
        // at the end of the document, so their position comes from their first footnote.
        var starts = new SortedDictionary<int, string>();
        foreach (var block in document)
        {
            var line = block switch
            {
                FootnoteGroup g when g.Count > 0 => g.Min(f => f.Line),
                // Tab sets are created after parsing and carry no line of their own.
                TabbedSetBlock t when t.Count > 0 => t[0].Line,
                _ => block.Line,
            };
            if (block is FootnoteGroup fg)
            {
                // Each footnote definition is its own block in the source.
                foreach (var f in fg) starts.TryAdd(f.Line, "footnotes");
                continue;
            }
            if (line < 0 || line >= lines.Length) continue;
            starts.TryAdd(line, KindOf(block, lines[line]));
        }

        var ordered = starts.ToList();
        var prefix = ordered.Count == 0 ? normalized : string.Join('\n', lines[..ordered[0].Key]) + (ordered[0].Key > 0 ? "\n" : "");
        var blocks = new List<MarkdownBlock>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var start = ordered[i].Key;
            var end = i + 1 < ordered.Count ? ordered[i + 1].Key : lines.Length;
            var content = lines[start..end].ToList();
            var gap = new List<string>();
            while (content.Count > 1 && string.IsNullOrWhiteSpace(content[^1]))
            {
                gap.Insert(0, content[^1]);
                content.RemoveAt(content.Count - 1);
            }
            var source = string.Join('\n', content);
            // Rebuild the separator: one newline ends the block's last line, then the blank lines.
            var isLast = i + 1 == ordered.Count;
            var gapText = isLast
                ? (gap.Count > 0 ? "\n" + string.Join('\n', gap) : "")
                : "\n" + string.Concat(gap.Select(g => g + "\n"));
            var kind = ordered[i].Value;
            if (kind == "paragraph" && AbbreviationLines().IsMatch(source)) kind = "abbreviations";
            blocks.Add(new MarkdownBlock(i, kind, source, gapText));
        }
        return (prefix, blocks);
    }

    /// <summary>Reassembles a body from the original blocks and an edit list.</summary>
    public static string Apply(string body, MarkdownPipeline pipeline, IReadOnlyList<BlockEdit> edits)
    {
        var crlf = body.Contains("\r\n", StringComparison.Ordinal);
        var (prefix, blocks) = Split(body, pipeline);
        var sb = new StringBuilder(prefix);
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            var isLast = i + 1 == edits.Count;
            string source;
            string gap;
            if (edit.Keep is int keep)
            {
                if (keep < 0 || keep >= blocks.Count)
                    throw new CmsConflictException("The page changed since it was opened. Reload it and try again.");
                source = blocks[keep].Source;
                gap = blocks[keep].Gap;
            }
            else
            {
                source = (edit.Source ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n', ' ');
                if (source.Trim().Length == 0) continue;
                gap = "\n\n";
            }

            // A kept block that was the last one in the file has no separator; give it one when
            // something now follows it. Likewise, the final block ends the file with one newline.
            if (!isLast && !gap.Contains("\n\n", StringComparison.Ordinal) && !gap.EndsWith('\n')) gap = "\n\n";
            if (!isLast && gap == "\n") gap = "\n\n";
            if (isLast) gap = blocks.Count > 0 ? TrailingOf(body) : "\n";
            sb.Append(source).Append(gap);
        }
        var result = sb.ToString();
        return crlf ? result.Replace("\n", "\r\n", StringComparison.Ordinal) : result;
    }

    private static string TrailingOf(string body)
    {
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var trimmed = normalized.TrimEnd('\n', ' ', '\t');
        var tail = normalized[trimmed.Length..];
        return tail.Contains('\n') ? tail : "\n";
    }

    private static string KindOf(Block block, string firstLine) => block switch
    {
        HeadingBlock => "heading",
        ParagraphBlock when firstLine.TrimStart().StartsWith("$$", StringComparison.Ordinal) => "math",
        ParagraphBlock => "paragraph",
        ListBlock => "list",
        QuoteBlock => "quote",
        Markdig.Extensions.Mathematics.MathBlock => "math",
        FencedCodeBlock => "code",
        CodeBlock => "code",
        HtmlBlock => "html",
        Markdig.Extensions.Figures.Figure => "figure",
        ThematicBreakBlock => "hr",
        Table => "table",
        AdmonitionBlock => "admonition",
        TabbedSetBlock or TabItemBlock => "tabs",
        LinkReferenceDefinitionGroup => "references",
        _ => Camel(block.GetType().Name.Replace("Block", "", StringComparison.Ordinal)),
    };

    private static string Camel(string name) => name.Length == 0 ? "block" : char.ToLowerInvariant(name[0]) + name[1..];

    [GeneratedRegex(@"^(\*\[[^\]]+\]:.*(\n|$))+$")]
    private static partial Regex AbbreviationLines();
}
