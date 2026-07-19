using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Netdocs.Abstractions;
using Netdocs.Core.Markdown.Admonitions;
using Netdocs.Core.Markdown.Emoji;
using Netdocs.Core.Markdown.Extras;
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
            // Strikethrough/sub/sup/mark; '++' is left free for pymdownx.keys.
            .UseEmphasisExtras(EmphasisExtraOptions.Default & ~EmphasisExtraOptions.Inserted)
            .UseTaskLists()
            .UseFootnotes()
            .UseAutoIdentifiers()     // toc permalink ids
            .UseGenericAttributes()   // attr_list {target=_blank}
            .UseAbbreviations()
            .UseDefinitionLists()
            .UseAutoLinks()
            .UseListExtras()
            .UseMediaLinks()
            .UseMathematics();

        builder.Extensions.AddIfNotAlready(new AdmonitionExtension());
        builder.Extensions.AddIfNotAlready(new TabbedExtension());
        builder.Extensions.AddIfNotAlready(new MaterialCodeBlockExtension());
        builder.Extensions.AddIfNotAlready(new TwemojiExtension(ResolveTwemojiBase(site.Config)));
        builder.Extensions.AddIfNotAlready(new KeysCriticExtension());

        foreach (var contributor in contributors)
            contributor.Extend(builder, site);

        return builder.Build();
    }

    private static string ResolveTwemojiBase(SiteConfig config)
    {
        foreach (var key in (ReadOnlySpan<string>)["pymdownx.emoji", "emoji"])
            if (config.MarkdownExtensions.TryGetValue(key, out var opts)
                && opts.TryGetValue("base", out var value)
                && value?.ToString() is { Length: > 0 } custom)
                return custom;
        return TwemojiExtension.DefaultBaseUrl;
    }
}
