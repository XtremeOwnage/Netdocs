using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class AffiliateLinksPluginTests
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

    private static AffiliateLinksPlugin Configured(params Dictionary<string, object?>[] programs)
    {
        var opts = new Dictionary<string, object?> { ["programs"] = programs.Cast<object?>().ToList() };
        var plugin = new AffiliateLinksPlugin();
        plugin.Configure(new FakeContext(opts));
        return plugin;
    }

    private static Dictionary<string, object?> Program(string name, string[] domains, string disclosure, string? query = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["domains"] = domains.Cast<object?>().ToList(),
            ["disclosure"] = disclosure,
        };
        if (query is not null) d["query_contains"] = query;
        return d;
    }

    private static string Run(AffiliateLinksPlugin plugin, string markdown)
    {
        var site = new SiteContext { Config = new SiteConfig(), Options = new BuildOptions(), LoggerFactory = NullLoggerFactory.Instance };
        var page = new Page { SourcePath = "blog/posts/x.md", RelativePath = "blog/posts/x.md", RawMarkdown = markdown };
        return plugin.ProcessAsync(page, markdown, site, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void EbayLink_GetsFootnoteRefAndDisclosure()
    {
        var plugin = Configured(Program("ebay", ["ebay.us"], "This post uses eBay affiliate links."));
        var result = Run(plugin, "Check this [eBay Link](https://ebay.us/abc123) out.");

        Assert.Contains("[eBay Link](https://ebay.us/abc123)[^affiliate-ebay]", result);
        Assert.Contains("[^affiliate-ebay]: This post uses eBay affiliate links.", result);
    }

    [Fact]
    public void EbayLink_WithAttrList_KeepsAttributesBeforeRef()
    {
        var plugin = Configured(Program("ebay", ["ebay.us"], "eBay disclosure."));
        var result = Run(plugin, "[eBay Link](https://ebay.us/abc){target=_blank}");

        Assert.Contains("[eBay Link](https://ebay.us/abc){target=_blank}[^affiliate-ebay]", result);
    }

    [Fact]
    public void NonAffiliateLink_IsUntouched()
    {
        var plugin = Configured(Program("ebay", ["ebay.us"], "eBay disclosure."));
        var result = Run(plugin, "A normal [link](https://example.com/page).");

        Assert.DoesNotContain("affiliate", result);
    }

    [Fact]
    public void AmazonLink_RequiresAffiliateTag()
    {
        var plugin = Configured(Program("amazon", ["amazon.com"], "Amazon disclosure.", query: "tag="));
        var plain = Run(plugin, "Plain [product](https://amazon.com/dp/B000).");
        Assert.DoesNotContain("affiliate", plain);

        var tagged = Run(plugin, "Tagged [product](https://amazon.com/dp/B000?tag=xo-20).");
        Assert.Contains("[^affiliate-amazon]", tagged);
        Assert.Contains("[^affiliate-amazon]: Amazon disclosure.", tagged);
    }

    [Fact]
    public void MultipleLinks_EmitDisclosureOnce()
    {
        var plugin = Configured(Program("ebay", ["ebay.us"], "eBay disclosure."));
        var result = Run(plugin,
            "[A](https://ebay.us/a) and [B](https://ebay.us/b) and [C](https://ebay.us/c)");

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(result, @"\[\^affiliate-ebay\](?!:)").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, @"\[\^affiliate-ebay\]:"));
    }

    [Fact]
    public void LinksInFencedCode_AreIgnored()
    {
        var plugin = Configured(Program("ebay", ["ebay.us"], "eBay disclosure."));
        var md = "```\n[eBay](https://ebay.us/abc)\n```";
        var result = Run(plugin, md);

        Assert.DoesNotContain("affiliate", result);
    }

    [Fact]
    public void LinkWithExistingFootnote_IsNotDoubleAnnotated()
    {
        var plugin = Configured(Program("ebay", ["ebay.us"], "eBay disclosure."));
        var result = Run(plugin, "[eBay Link](https://ebay.us/abc){target=_blank}[^ebay]\n\n[^ebay]: manual.");

        Assert.DoesNotContain("[^affiliate-ebay]", result);
    }

    [Fact]
    public void PerDomainQueryRules_MixShortenerAndTaggedDomain()
    {
        var opts = new Dictionary<string, object?>
        {
            ["programs"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "amazon",
                    ["disclosure"] = "Amazon disclosure.",
                    ["domains"] = new List<object?>
                    {
                        "amzn.to",
                        new Dictionary<string, object?> { ["domain"] = "amazon.com", ["query_contains"] = "tag=" },
                    },
                },
            },
        };
        var plugin = new AffiliateLinksPlugin();
        plugin.Configure(new FakeContext(opts));

        Assert.Contains("[^affiliate-amazon]", Run(plugin, "[a](https://amzn.to/xyz)"));
        Assert.Contains("[^affiliate-amazon]", Run(plugin, "[a](https://amazon.com/dp/B0?tag=xo-20)"));
        Assert.DoesNotContain("affiliate", Run(plugin, "[a](https://amazon.com/dp/B0)"));
    }

    [Fact]
    public void LinksInPipeTable_UseStandaloneDisclosureNotFootnoteRef()
    {
        var plugin = Configured(Program("amazon", ["amzn.to"], "Amazon disclosure."));
        var md = "| Item | Buy |\n| --- | --- |\n| Widget | [Amazon](https://amzn.to/xyz) |";
        var result = Run(plugin, md);

        // No footnote ref injected into the table cell (would break Markdig table rendering).
        Assert.DoesNotContain("[^affiliate-amazon]", result);
        // But the footer disclosure is still guaranteed via a standalone admonition.
        Assert.Contains("!!! info \"Affiliate links\"", result);
        Assert.Contains("Amazon disclosure.", result);
    }

    [Fact]
    public void InlineLinkWinsOverTableOnly_NoDuplicateDisclosure()
    {
        var plugin = Configured(Program("amazon", ["amzn.to"], "Amazon disclosure."));
        var md = "Inline [buy](https://amzn.to/a) here.\n\n| x | [Amazon](https://amzn.to/b) |";
        var result = Run(plugin, md);

        Assert.Contains("[^affiliate-amazon]: Amazon disclosure.", result);
        // Program already referenced inline, so no standalone admonition too.
        Assert.DoesNotContain("!!! info \"Affiliate links\"", result);
    }

    [Fact]
    public void SubdomainOfConfiguredDomain_Matches()
    {
        var plugin = Configured(Program("ebay", ["ebay.com"], "eBay disclosure."));
        var result = Run(plugin, "[item](https://www.ebay.com/itm/123)");
        Assert.Contains("[^affiliate-ebay]", result);
    }
}
