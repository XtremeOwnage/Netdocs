using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>Covers plugin instantiation and the configurable preprocessor order override.</summary>
public class PluginHostTests
{
    private sealed class Pre10 : IPlugin, IMarkdownPreprocessor
    {
        public string Name => "pre10";
        public int Order => 10;
        public void Configure(IPluginContext ctx) { }
        public Task<string> ProcessAsync(Page page, string md, SiteContext site, CancellationToken ct) => Task.FromResult(md);
    }

    private sealed class Pre20 : IPlugin, IMarkdownPreprocessor
    {
        public string Name => "pre20";
        public int Order => 20;
        public void Configure(IPluginContext ctx) { }
        public Task<string> ProcessAsync(Page page, string md, SiteContext site, CancellationToken ct) => Task.FromResult(md);
    }

    private static PluginHost Build(params PluginConfig[] configs)
    {
        var registry = new PluginRegistry().Register<Pre10>("pre10").Register<Pre20>("pre20");
        var services = new ServiceCollection();
        return PluginHost.Build(new SiteConfig(), new BuildOptions(), configs, registry,
            services.BuildServiceProvider(), services, NullLoggerFactory.Instance);
    }

    [Fact]
    public void Preprocessors_UseNaturalOrder_ByDefault()
    {
        var host = Build(
            new PluginConfig { Name = "pre20" },
            new PluginConfig { Name = "pre10" });

        Assert.Equal(["pre10", "pre20"], host.Preprocessors.Cast<IPlugin>().Select(p => p.Name));
    }

    [Fact]
    public void Preprocessors_HonorConfigOrderOverride()
    {
        // Force pre20 (natural 20) to run before pre10 (natural 10) via an order override.
        var host = Build(
            new PluginConfig { Name = "pre10" },
            new PluginConfig { Name = "pre20", Order = 5 });

        Assert.Equal(["pre20", "pre10"], host.Preprocessors.Cast<IPlugin>().Select(p => p.Name));
    }
}
