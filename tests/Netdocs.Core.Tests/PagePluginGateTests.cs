using Netdocs.Abstractions;
using Netdocs.Core.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers per-page enable/disable of plugins via front matter.</summary>
public class PagePluginGateTests
{
    private static Page PageWith(IReadOnlyDictionary<string, object?> meta) => new()
    {
        SourcePath = "",
        RelativePath = "test.md",
        FrontMatter = meta,
    };

    [Fact]
    public void EnabledByDefault_WhenNoFrontMatter()
    {
        var page = PageWith(new Dictionary<string, object?>());
        Assert.True(PagePluginGate.IsEnabled(page, "macros"));
    }

    [Fact]
    public void MapForm_DisablesNamedPlugin()
    {
        var page = PageWith(new Dictionary<string, object?>
        {
            ["plugins"] = new Dictionary<string, object?> { ["macros"] = false },
        });
        Assert.False(PagePluginGate.IsEnabled(page, "macros"));
        Assert.True(PagePluginGate.IsEnabled(page, "snippets"));
    }

    [Fact]
    public void ListForm_DisablesNamedPlugin()
    {
        var page = PageWith(new Dictionary<string, object?>
        {
            ["disable_plugins"] = new List<object?> { "table-reader", "snippets" },
        });
        Assert.False(PagePluginGate.IsEnabled(page, "table-reader"));
        Assert.False(PagePluginGate.IsEnabled(page, "snippets"));
        Assert.True(PagePluginGate.IsEnabled(page, "macros"));
    }

    [Fact]
    public void MapForm_OverridesListForm_CanReEnable()
    {
        var page = PageWith(new Dictionary<string, object?>
        {
            ["disable_plugins"] = new List<object?> { "macros" },
            ["plugins"] = new Dictionary<string, object?> { ["macros"] = true },
        });
        Assert.True(PagePluginGate.IsEnabled(page, "macros"));
    }

    [Fact]
    public void PluginNameMatch_IsCaseInsensitive()
    {
        var page = PageWith(new Dictionary<string, object?>
        {
            ["plugins"] = new Dictionary<string, object?> { ["Macros"] = false },
        });
        Assert.False(PagePluginGate.IsEnabled(page, "macros"));
    }
}
