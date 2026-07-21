using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Core.Deploy;
using Xunit;

namespace Netdocs.Core.Tests;

public class DeployerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-deploy-test-" + Guid.NewGuid().ToString("N"));

    private SiteConfig NewConfig(string dest, bool clean = true)
    {
        var siteDir = Path.Combine(_root, "site");
        Directory.CreateDirectory(siteDir);
        Directory.CreateDirectory(Path.Combine(siteDir, "sub"));
        File.WriteAllText(Path.Combine(siteDir, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(siteDir, "sub", "page.html"), "<html>sub</html>");
        return new SiteConfig
        {
            ProjectRoot = _root,
            SiteDir = "site",
            Deploy = new DeployConfig { Target = "filesystem", Path = dest, Clean = clean },
        };
    }

    [Fact]
    public async Task Filesystem_CopiesAllFiles()
    {
        var dest = Path.Combine(_root, "out");
        var config = NewConfig(dest);
        var result = await new Deployer(config, NullLogger.Instance).DeployAsync();

        Assert.Equal(0, result);
        Assert.True(File.Exists(Path.Combine(dest, "index.html")));
        Assert.True(File.Exists(Path.Combine(dest, "sub", "page.html")));
    }

    [Fact]
    public async Task Filesystem_Clean_PrunesStaleFiles()
    {
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "old.html"), "stale");

        var config = NewConfig(dest);
        await new Deployer(config, NullLogger.Instance).DeployAsync();

        Assert.False(File.Exists(Path.Combine(dest, "old.html")));
        Assert.True(File.Exists(Path.Combine(dest, "index.html")));
    }

    [Fact]
    public async Task Filesystem_NoClean_KeepsStaleFiles()
    {
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "old.html"), "stale");

        var config = NewConfig(dest, clean: false);
        await new Deployer(config, NullLogger.Instance).DeployAsync();

        Assert.True(File.Exists(Path.Combine(dest, "old.html")));
    }

    [Fact]
    public async Task Filesystem_MissingPath_Fails()
    {
        var config = NewConfig("");
        config.Deploy.Path = null;
        var result = await new Deployer(config, NullLogger.Instance).DeployAsync();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Target_None_IsNoOp()
    {
        var config = NewConfig(Path.Combine(_root, "out"));
        config.Deploy.Target = "none";
        var result = await new Deployer(config, NullLogger.Instance).DeployAsync();
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task S3_MissingBucket_Fails()
    {
        var config = NewConfig(Path.Combine(_root, "out"));
        config.Deploy.Target = "s3";
        config.Deploy.Bucket = null;
        var result = await new Deployer(config, NullLogger.Instance).DeployAsync();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task UnknownTarget_Fails()
    {
        var config = NewConfig(Path.Combine(_root, "out"));
        config.Deploy.Target = "bogus";
        var result = await new Deployer(config, NullLogger.Instance).DeployAsync();
        Assert.Equal(1, result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
