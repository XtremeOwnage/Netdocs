using Markdig;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Renders LaTeX math, mirroring <c>pymdownx.arithmatex</c>. Markdig's mathematics
/// extension parses <c>$...$</c> / <c>$$...$$</c> (and <c>\(...\)</c> / <c>\[...\]</c>)
/// into <c>&lt;span class="math"&gt;\(...\)&lt;/span&gt;</c> and
/// <c>&lt;div class="math"&gt;\[...\]&lt;/div&gt;</c> elements — code spans and fenced
/// blocks are left untouched — and MathJax v3 typesets those delimiters client-side.
/// </summary>
/// <remarks>
/// MathJax is loaded from a CDN by default; point <c>mathjax_url</c> at a vendored copy
/// (or a KaTeX-compatible bundle) for offline builds. Re-typesets on
/// <c>navigation.instant</c> page swaps.
/// </remarks>
public sealed class ArithmatexPlugin : IPlugin, IMarkdigContributor
{
    private const string DefaultMathJaxUrl =
        "https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js";

    public string Name => "arithmatex";

    public void Configure(IPluginContext ctx)
    {
        var url = ctx.PluginOptions.TryGetValue("mathjax_url", out var u) && u is string s && s.Length > 0
            ? s
            : DefaultMathJaxUrl;

        // Config must exist before MathJax loads, so set window.MathJax first, then
        // inject the library, then re-typeset on instant-navigation content swaps.
        ctx.AddInlineScript(
            "window.MathJax=window.MathJax||{tex:{inlineMath:[['\\\\(','\\\\)']]," +
            "displayMath:[['\\\\[','\\\\]']],processEscapes:true}," +
            "options:{ignoreHtmlClass:'.*',processHtmlClass:'math'}," +
            "startup:{typeset:true}};" +
            "(function(){if(document.getElementById('__mathjax'))return;" +
            "var e=document.createElement('script');e.id='__mathjax';e.async=true;" +
            $"e.src='{url}';document.head.appendChild(e);}})();" +
            "if(window.document$&&!window.__netdocsMathBound){window.__netdocsMathBound=true;" +
            "window.document$.subscribe(function(){if(window.MathJax&&window.MathJax.typesetPromise){" +
            "window.MathJax.typesetClear&&window.MathJax.typesetClear();window.MathJax.typesetPromise();}});}");
    }

    public void Extend(MarkdownPipelineBuilder builder, SiteContext site)
        => builder.UseMathematics();
}
