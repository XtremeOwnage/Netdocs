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

/// <summary>
/// Covers the gzip upload path for the s3 target. S3 returns exactly the bytes it stores and will
/// not compress on the fly the way GitHub Pages does, so a large search index is downloaded in full
/// by every visitor who opens search — these pin the staging and argument construction that let it
/// be stored compressed instead.
/// </summary>
public sealed class S3GzipDeployTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netdocs-gzip-test-" + Guid.NewGuid().ToString("N"));

    public S3GzipDeployTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, "site", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Gunzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task StagesCompressibleFilesGzipped_PreservingRelativePathAndExtension()
    {
        var index = string.Concat(Enumerable.Repeat("{\"text\":\"searchable content\"}", 500));
        Write("search/search_index.json", index);
        Write("assets/app.css", "body{color:red}");
        Write("logo.png", "not really a png");
        Write("fonts/x.woff2", "binary-ish");

        var staging = Path.Combine(_root, "staging");
        var staged = await Deployer.StageCompressedAsync(Path.Combine(_root, "site"), staging, default);

        Assert.Equal(2, staged);

        // Path and extension survive, so the AWS CLI still infers the same Content-Type.
        var stagedIndex = Path.Combine(staging, "search", "search_index.json");
        Assert.True(File.Exists(stagedIndex));
        Assert.True(File.Exists(Path.Combine(staging, "assets", "app.css")));

        // Binary assets are left for the uncompressed pass.
        Assert.False(File.Exists(Path.Combine(staging, "logo.png")));
        Assert.False(File.Exists(Path.Combine(staging, "fonts", "x.woff2")));

        // Really gzip, and really the original bytes.
        Assert.Equal(index, Gunzip(stagedIndex));
        Assert.True(new FileInfo(stagedIndex).Length < index.Length,
            "a highly repetitive index should be smaller once compressed");
    }

    [Fact]
    public async Task StagingAnEmptySiteStagesNothing()
    {
        Write("logo.png", "binary");
        var staging = Path.Combine(_root, "staging2");

        Assert.Equal(0, await Deployer.StageCompressedAsync(Path.Combine(_root, "site"), staging, default));
    }

    [Fact]
    public void PlainSyncIsUnchangedWhenGzipIsOff()
    {
        var args = Deployer.S3SyncArgs("/site", "s3://bucket", new DeployConfig { Clean = true, Region = "us-east-1" });

        Assert.Equal(["s3", "sync", "/site", "s3://bucket", "--delete", "--region", "us-east-1"], args);
    }

    [Fact]
    public void FirstPassSkipsEveryCompressibleType()
    {
        var args = Deployer.S3SyncArgs("/site", "s3://bucket", new DeployConfig(), excludeCompressible: true);

        Assert.DoesNotContain("--content-encoding", args);
        foreach (var ext in Deployer.CompressibleExtensions)
            Assert.Contains("*" + ext, args);
    }

    [Fact]
    public void SecondPassUploadsOnlyCompressibleTypesTaggedAsGzip()
    {
        var args = Deployer.S3SyncArgs("/staging", "s3://bucket", new DeployConfig(), onlyCompressible: true);

        // Exclude-all then re-include is what scopes the pass; order matters to the AWS CLI.
        Assert.True(args.IndexOf("*") < args.IndexOf("*.json"), "the catch-all exclude must come first");
        Assert.Contains("--content-encoding", args);
        Assert.Equal("gzip", args[args.IndexOf("--content-encoding") + 1]);
        foreach (var ext in Deployer.CompressibleExtensions)
            Assert.Contains("*" + ext, args);
    }

    [Fact]
    public void EveryCompressibleTypeIsFilteredByBothPasses()
    {
        // A type excluded by pass 1 but not re-included by pass 2 would never be uploaded at all.
        var first = Deployer.S3SyncArgs("/site", "s3://b", new DeployConfig(), excludeCompressible: true);
        var second = Deployer.S3SyncArgs("/staging", "s3://b", new DeployConfig(), onlyCompressible: true);

        foreach (var ext in Deployer.CompressibleExtensions)
        {
            Assert.Contains("*" + ext, first);
            Assert.Contains("*" + ext, second);
        }
    }
}
