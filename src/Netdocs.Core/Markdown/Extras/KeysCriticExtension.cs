using System.Text;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Netdocs.Core.Markdown.Extras;

/// <summary>Inline node carrying pre-rendered HTML (with a plain-text fallback).</summary>
public sealed class RawInline : LeafInline
{
    public required string Html { get; init; }
    public required string PlainText { get; init; }
}

public sealed class RawInlineRenderer : HtmlObjectRenderer<RawInline>
{
    protected override void Write(HtmlRenderer renderer, RawInline obj)
        => renderer.Write(renderer.EnableHtmlForInline ? obj.Html : obj.PlainText);
}

/// <summary>
/// Parses pymdownx-style keyboard keys (<c>++ctrl+alt+del++</c>) into Material's
/// <c>&lt;span class="keys"&gt;&lt;kbd class="key-*"&gt;…&lt;/kbd&gt;&lt;/span&gt;</c> markup.
/// </summary>
public sealed class KeysInlineParser : InlineParser
{
    public KeysInlineParser() => OpeningCharacters = ['+'];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.PeekCharExtra(-1).IsAlphaNumeric()) return false;

        var text = slice.Text;
        var start = slice.Start;
        var end = slice.End;
        if (start + 1 > end || text[start + 1] != '+') return false;

        var close = -1;
        for (var p = start + 2; p < end; p++)
            if (text[p] == '+' && text[p + 1] == '+') { close = p; break; }
        if (close < 0) return false;

        var inner = text.Substring(start + 2, close - (start + 2)).Trim();
        if (inner.Length == 0) return false;

        var tokens = inner.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        var html = new StringBuilder("<span class=\"keys\">");
        var plain = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++)
        {
            var (cls, label) = KeyDatabase.Resolve(tokens[i]);
            if (i > 0) { html.Append("<span>+</span>"); plain.Append('+'); }
            html.Append("<kbd class=\"").Append(cls).Append("\">").Append(Escape(label)).Append("</kbd>");
            plain.Append(label.Length > 0 ? label : tokens[i]);
        }
        html.Append("</span>");

        Emit(processor, ref slice, start, close + 2, html.ToString(), plain.ToString());
        return true;
    }

    private static void Emit(InlineProcessor processor, ref StringSlice slice, int start, int endExclusive, string html, string plain)
    {
        var pos = processor.GetSourcePosition(start, out var line, out var column);
        processor.Inline = new RawInline
        {
            Html = html,
            PlainText = plain,
            Span = new(pos, processor.GetSourcePosition(endExclusive - 1)),
            Line = line,
            Column = column,
        };
        slice.Start = endExclusive;
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>
/// Parses CriticMarkup: <c>{++ins++}</c>, <c>{--del--}</c>, <c>{==mark==}</c>,
/// <c>{~~old~&gt;new~~}</c>, and <c>{&gt;&gt;comment&lt;&lt;}</c>.
/// </summary>
public sealed class CriticInlineParser : InlineParser
{
    public CriticInlineParser() => OpeningCharacters = ['{'];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var text = slice.Text;
        var start = slice.Start;
        var end = slice.End;
        if (start + 2 > end) return false;

        var marker = text.Substring(start + 1, 2);
        var (close, kind) = marker switch
        {
            "++" => ("++}", "ins"),
            "--" => ("--}", "del"),
            "==" => ("==}", "mark"),
            "~~" => ("~~}", "sub"),
            ">>" => ("<<}", "comment"),
            _ => (null, null),
        };
        if (close is null) return false;

        var contentStart = start + 3;
        var closeIndex = IndexOf(text, contentStart, end, close);
        if (closeIndex < 0) return false;

        var inner = text.Substring(contentStart, closeIndex - contentStart);
        var (html, plain) = kind switch
        {
            "ins" => ($"<ins class=\"critic\">{Escape(inner)}</ins>", inner),
            "del" => ($"<del class=\"critic\">{Escape(inner)}</del>", inner),
            "mark" => ($"<mark class=\"critic\">{Escape(inner)}</mark>", inner),
            "comment" => ($"<span class=\"critic comment\">{Escape(inner)}</span>", inner),
            _ => Substitution(inner),
        };

        var endExclusive = closeIndex + close.Length;
        var pos = processor.GetSourcePosition(start, out var line, out var column);
        processor.Inline = new RawInline
        {
            Html = html,
            PlainText = plain,
            Span = new(pos, processor.GetSourcePosition(endExclusive - 1)),
            Line = line,
            Column = column,
        };
        slice.Start = endExclusive;
        return true;
    }

    private static (string Html, string Plain) Substitution(string inner)
    {
        var split = inner.IndexOf("~>", StringComparison.Ordinal);
        if (split < 0)
            return ($"<ins class=\"critic\">{Escape(inner)}</ins>", inner);
        var oldText = inner[..split];
        var newText = inner[(split + 2)..];
        return ($"<del class=\"critic\">{Escape(oldText)}</del><ins class=\"critic\">{Escape(newText)}</ins>",
                newText);
    }

    private static int IndexOf(string text, int from, int end, string needle)
    {
        var last = end - needle.Length + 1;
        for (var i = from; i <= last; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
                if (text[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>Maps key tokens to a Material <c>key-*</c> class and display label.</summary>
internal static class KeyDatabase
{
    private static readonly Dictionary<string, (string Cls, string Label)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ("key-control", "Ctrl"),
        ["control"] = ("key-control", "Ctrl"),
        ["alt"] = ("key-alt", "Alt"),
        ["opt"] = ("key-option", "Opt"),
        ["option"] = ("key-option", "Opt"),
        ["shift"] = ("key-shift", "Shift"),
        ["meta"] = ("key-meta", "Meta"),
        ["cmd"] = ("key-command", "Cmd"),
        ["command"] = ("key-command", "Cmd"),
        ["super"] = ("key-super", "Super"),
        ["win"] = ("key-windows", "Win"),
        ["windows"] = ("key-windows", "Win"),
        ["tab"] = ("key-tab", "Tab"),
        ["backspace"] = ("key-backspace", "Backspace"),
        ["del"] = ("key-delete", "Del"),
        ["delete"] = ("key-delete", "Del"),
        ["ins"] = ("key-insert", "Ins"),
        ["insert"] = ("key-insert", "Ins"),
        ["enter"] = ("key-enter", "Enter"),
        ["return"] = ("key-enter", "Enter"),
        ["esc"] = ("key-escape", "Esc"),
        ["escape"] = ("key-escape", "Esc"),
        ["home"] = ("key-home", "Home"),
        ["end"] = ("key-end", "End"),
        ["pgup"] = ("key-page-up", "PgUp"),
        ["page-up"] = ("key-page-up", "PgUp"),
        ["pgdn"] = ("key-page-down", "PgDn"),
        ["page-down"] = ("key-page-down", "PgDn"),
        ["space"] = ("key-space", "Space"),
        ["caps-lock"] = ("key-caps-lock", "Caps Lock"),
        ["print-screen"] = ("key-print-screen", "PrtSc"),
        ["up"] = ("key-arrow-up", ""),
        ["arrow-up"] = ("key-arrow-up", ""),
        ["down"] = ("key-arrow-down", ""),
        ["arrow-down"] = ("key-arrow-down", ""),
        ["left"] = ("key-arrow-left", ""),
        ["arrow-left"] = ("key-arrow-left", ""),
        ["right"] = ("key-arrow-right", ""),
        ["arrow-right"] = ("key-arrow-right", ""),
    };

    public static (string Cls, string Label) Resolve(string token)
    {
        var key = token.Trim();
        if (Map.TryGetValue(key, out var entry)) return entry;

        var lower = key.ToLowerInvariant();
        if (lower.Length >= 2 && lower[0] == 'f' && int.TryParse(lower.AsSpan(1), out var n) && n is >= 1 and <= 24)
            return ($"key-f{n}", $"F{n}");

        var slug = new StringBuilder(lower.Length);
        foreach (var c in lower)
            slug.Append(char.IsLetterOrDigit(c) ? c : '-');
        var label = key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..];
        return ($"key-{slug}", label);
    }
}

/// <summary>Registers the keys + CriticMarkup inline parsers and their renderer.</summary>
public sealed class KeysCriticExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<CriticInlineParser>())
            pipeline.InlineParsers.Insert(0, new CriticInlineParser());
        if (!pipeline.InlineParsers.Contains<KeysInlineParser>())
            pipeline.InlineParsers.Insert(0, new KeysInlineParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<RawInlineRenderer>())
            html.ObjectRenderers.Insert(0, new RawInlineRenderer());
    }
}
