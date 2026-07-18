using Markdig;
using Netdocs.Abstractions;
using Netdocs.Core.Markdown.Admonitions;
using Netdocs.Core.Markdown.Tabs;

namespace Netdocs.Core.Markdown;

/// <summary>Builds the shared Markdig pipeline (built-in extras + custom + plugin contributors).</summary>
public static class MarkdownPipelineFactory
{
    public static MarkdownPipeline Build(SiteContext site, IEnumerable<IMarkdigContributor> contributors)
    {
        var builder = new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .UsePipeTables()
            .UseGridTables()
            .UseEmphasisExtras()      // ~~del~~, ~sub~, ^sup^, ==mark==
            .UseTaskLists()
            .UseFootnotes()
            .UseAutoIdentifiers()     // toc permalink ids
            .UseGenericAttributes()   // attr_list {target=_blank}
            .UseAbbreviations()
            .UseEmojiAndSmiley(false)
            .UseDefinitionLists()
            .UseAutoLinks()
            .UseListExtras()
            .UseMediaLinks()
            .UseMathematics();

        builder.Extensions.AddIfNotAlready(new AdmonitionExtension());
        builder.Extensions.AddIfNotAlready(new TabbedExtension());
        builder.Extensions.AddIfNotAlready(new MaterialCodeBlockExtension());

        foreach (var contributor in contributors)
            contributor.Extend(builder, site);

        return builder.Build();
    }
}
