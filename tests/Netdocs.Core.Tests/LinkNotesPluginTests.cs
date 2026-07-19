using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class LinkNotesPluginTests
{
    private sealed class FakeContext(IReadOnlyDictionary<string, object?> options) : IPluginContext
    {
        public SiteConfig Config { get; init; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = options;
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }

    private static LinkNotesPlugin Configured(params Dictionary<string, object?>[] rules)
    {
        var opts = new Dictionary<string, object?> { ["rules"] = rules.Cast<object?>().ToList() };
        var plugin = new LinkNotesPlugin();
        plugin.Configure(new FakeContext(opts));
        return plugin;
    }

    private static Dictionary<string, object?> Rule(string name, string[] domains, string note, string? query = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["domains"] = domains.Cast<object?>().ToList(),
            ["note"] = note,
        };
        if (query is not null) d["query_contains"] = query;
        return d;
    }

    private static string Run(LinkNotesPlugin plugin, string markdown)
    {
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        var page = new Page { SourcePath = "blog/posts/x.md", RelativePath = "blog/posts/x.md", RawMarkdown = markdown };
        return plugin.ProcessAsync(page, markdown, site, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void EbayLink_GetsFootnoteRefAndNote()
    {
        var plugin = Configured(Rule("ebay", ["ebay.us"], "This post uses eBay affiliate links."));
        var result = Run(plugin, "Check this [eBay Link](https://ebay.us/abc123) out.");

        Assert.Contains("[eBay Link](https://ebay.us/abc123)[^linknote-ebay]", result);
        Assert.Contains("[^linknote-ebay]: This post uses eBay affiliate links.", result);
    }

    [Fact]
    public void EbayLink_WithAttrList_KeepsAttributesBeforeRef()
    {
        var plugin = Configured(Rule("ebay", ["ebay.us"], "eBay note."));
        var result = Run(plugin, "[eBay Link](https://ebay.us/abc){target=_blank}");

        Assert.Contains("[eBay Link](https://ebay.us/abc){target=_blank}[^linknote-ebay]", result);
    }

    [Fact]
    public void NonMatchingLink_IsUntouched()
    {
        var plugin = Configured(Rule("ebay", ["ebay.us"], "eBay note."));
        var result = Run(plugin, "A normal [link](https://example.com/page).");

        Assert.DoesNotContain("linknote", result);
    }

    [Fact]
    public void AmazonLink_RequiresQueryMarker()
    {
        var plugin = Configured(Rule("amazon", ["amazon.com"], "Amazon note.", query: "tag="));
        var plain = Run(plugin, "Plain [product](https://amazon.com/dp/B000).");
        Assert.DoesNotContain("linknote", plain);

        var tagged = Run(plugin, "Tagged [product](https://amazon.com/dp/B000?tag=xo-20).");
        Assert.Contains("[^linknote-amazon]", tagged);
        Assert.Contains("[^linknote-amazon]: Amazon note.", tagged);
    }

    [Fact]
    public void MultipleLinks_EmitNoteOnce()
    {
        var plugin = Configured(Rule("ebay", ["ebay.us"], "eBay note."));
        var result = Run(plugin,
            "[A](https://ebay.us/a) and [B](https://ebay.us/b) and [C](https://ebay.us/c)");

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(result, @"\[\^linknote-ebay\](?!:)").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, @"\[\^linknote-ebay\]:"));
    }

    [Fact]
    public void LinksInFencedCode_AreIgnored()
    {
        var plugin = Configured(Rule("ebay", ["ebay.us"], "eBay note."));
        var md = "```\n[eBay](https://ebay.us/abc)\n```";
        var result = Run(plugin, md);

        Assert.DoesNotContain("linknote", result);
    }

    [Fact]
    public void LinkWithExistingFootnote_IsNotDoubleAnnotated()
    {
        var plugin = Configured(Rule("ebay", ["ebay.us"], "eBay note."));
        var result = Run(plugin, "[eBay Link](https://ebay.us/abc){target=_blank}[^ebay]\n\n[^ebay]: manual.");

        Assert.DoesNotContain("[^linknote-ebay]", result);
    }

    [Fact]
    public void PerDomainQueryRules_MixShortenerAndTaggedDomain()
    {
        var opts = new Dictionary<string, object?>
        {
            ["rules"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "amazon",
                    ["note"] = "Amazon note.",
                    ["domains"] = new List<object?>
                    {
                        "amzn.to",
                        new Dictionary<string, object?> { ["domain"] = "amazon.com", ["query_contains"] = "tag=" },
                    },
                },
            },
        };
        var plugin = new LinkNotesPlugin();
        plugin.Configure(new FakeContext(opts));

        Assert.Contains("[^linknote-amazon]", Run(plugin, "[a](https://amzn.to/xyz)"));
        Assert.Contains("[^linknote-amazon]", Run(plugin, "[a](https://amazon.com/dp/B0?tag=xo-20)"));
        Assert.DoesNotContain("linknote", Run(plugin, "[a](https://amazon.com/dp/B0)"));
    }

    [Fact]
    public void LinksInPipeTable_UseStandaloneNoteNotFootnoteRef()
    {
        var plugin = Configured(Rule("amazon", ["amzn.to"], "Amazon note."));
        var md = "| Item | Buy |\n| --- | --- |\n| Widget | [Amazon](https://amzn.to/xyz) |";
        var result = Run(plugin, md);

        // No footnote ref injected into the table cell (would break Markdig table rendering).
        Assert.DoesNotContain("[^linknote-amazon]", result);
        // But the footer note is still guaranteed via a standalone admonition (default title).
        Assert.Contains("!!! info \"Links\"", result);
        Assert.Contains("Amazon note.", result);
    }

    [Fact]
    public void CustomLabel_IsUsedForStandaloneAdmonitionTitle()
    {
        var d = Rule("amazon", ["amzn.to"], "Amazon note.");
        d["label"] = "Affiliate links";
        var plugin = Configured(d);
        var md = "| Item | Buy |\n| --- | --- |\n| Widget | [Amazon](https://amzn.to/xyz) |";
        var result = Run(plugin, md);

        Assert.Contains("!!! info \"Affiliate links\"", result);
    }

    [Fact]
    public void InlineLinkWinsOverTableOnly_NoDuplicateNote()
    {
        var plugin = Configured(Rule("amazon", ["amzn.to"], "Amazon note."));
        var md = "Inline [buy](https://amzn.to/a) here.\n\n| x | [Amazon](https://amzn.to/b) |";
        var result = Run(plugin, md);

        Assert.Contains("[^linknote-amazon]: Amazon note.", result);
        // Rule already referenced inline, so no standalone admonition too.
        Assert.DoesNotContain("!!! info", result);
    }

    [Fact]
    public void SubdomainOfConfiguredDomain_Matches()
    {
        var plugin = Configured(Rule("ebay", ["ebay.com"], "eBay note."));
        var result = Run(plugin, "[item](https://www.ebay.com/itm/123)");
        Assert.Contains("[^linknote-ebay]", result);
    }

    [Fact]
    public void RegexPattern_MatchesLink()
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = "promo",
            ["note"] = "Promo note.",
            ["patterns"] = new List<object?> { @"https?://[^/]*\.example\.com/ref/\d+" },
        };
        var plugin = Configured(d);

        Assert.Contains("[^linknote-promo]", Run(plugin, "[go](https://shop.example.com/ref/42)"));
        Assert.DoesNotContain("linknote", Run(plugin, "[go](https://shop.example.com/other)"));
    }

    [Fact]
    public void RegexAndDomain_BothSelectTheRule()
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = "mix",
            ["note"] = "Mixed note.",
            ["domains"] = new List<object?> { "ebay.us" },
            ["patterns"] = new List<object?> { @"utm_source=xo" },
        };
        var plugin = Configured(d);

        Assert.Contains("[^linknote-mix]", Run(plugin, "[a](https://ebay.us/x)"));
        Assert.Contains("[^linknote-mix]", Run(plugin, "[b](https://other.example/deal?utm_source=xo)"));
    }

    [Fact]
    public void LegacyProgramsAndDisclosureKeys_StillWork()
    {
        // Backward compatibility: the old `programs` / `disclosure` config keys must keep working.
        var opts = new Dictionary<string, object?>
        {
            ["programs"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "ebay",
                    ["domains"] = new List<object?> { "ebay.us" },
                    ["disclosure"] = "Legacy eBay disclosure.",
                },
            },
        };
        var plugin = new LinkNotesPlugin();
        plugin.Configure(new FakeContext(opts));
        var result = Run(plugin, "Buy on [eBay](https://ebay.us/abc).");

        Assert.Contains("[^linknote-ebay]", result);
        Assert.Contains("[^linknote-ebay]: Legacy eBay disclosure.", result);
    }

    [Fact]
    public void InvalidRegexPattern_IsSkippedNotThrown()
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = "bad",
            ["note"] = "note",
            ["patterns"] = new List<object?> { "([unclosed" },
        };
        // Should not throw; the rule simply has no usable pattern (and no domains), so it's dropped.
        var plugin = Configured(d);
        Assert.DoesNotContain("linknote", Run(plugin, "[a](https://ebay.us/x)"));
    }
}
