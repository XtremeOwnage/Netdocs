using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class ImportedDocsPluginTests
{
    private sealed class FakeContext : IPluginContext
    {
        public SiteConfig Config { get; init; } = new();
        public BuildOptions Options { get; } = new();
        public ILogger Logger { get; } = NullLogger.Instance;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IReadOnlyDictionary<string, object?> PluginOptions { get; } = new Dictionary<string, object?>();
        public void AddStylesheet(string href) { }
        public void AddScript(string src, bool defer = true) { }
        public void AddInlineScript(string javascript) { }
        public void AddAsset(string sourcePath, string destRelative) { }
    }

    private static ImportedDocsPlugin Configured(SiteConfig? config = null)
    {
        var plugin = new ImportedDocsPlugin();
        plugin.Configure(new FakeContext { Config = config ?? new SiteConfig() });
        return plugin;
    }

    [Theory]
    [InlineData("guide.md", "products/api", "products/api/guide/")]
    [InlineData("integrations/citrix.md", "products/api", "products/api/integrations/citrix/")]
    [InlineData("a/b/c/deep.md", "products/api", "products/api/a/b/c/deep/")]
    [InlineData("guide.md", null, "guide/")]
    [InlineData("nested/guide.md", null, "nested/guide/")]
    public void ComputeUrl_PreservesSourceDirectoriesBeneathDestination(
        string relativePath, string? destinationPath, string expected)
    {
        Assert.Equal(expected, Configured().ComputeUrl(relativePath, destinationPath));
    }

    [Theory]
    [InlineData("index.md", "products/api", "products/api/")]
    [InlineData("integrations/index.md", "products/api", "products/api/integrations/")]
    [InlineData("README.md", "products/api", "products/api/")]
    public void ComputeUrl_CollapsesIndexOntoItsDirectory(
        string relativePath, string destinationPath, string expected)
    {
        Assert.Equal(expected, Configured().ComputeUrl(relativePath, destinationPath));
    }

    [Fact]
    public void ComputeUrl_SlugifiesSegmentsWhenSiteSlugifiesUrls()
    {
        var plugin = Configured(new SiteConfig { SlugifyUrls = true });

        Assert.Equal("products/api/getting-started/", plugin.ComputeUrl("Getting Started.md", "products/api"));
    }

    [Fact]
    public async Task OnImportAsync_PulledDocs_PlacesPagesUnderDestinationForNavigation()
    {
        var origin = Path.Combine(Path.GetTempPath(), "netdocs-origin-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(Path.GetTempPath(), "netdocs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(origin, "docs", "integrations"));
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(origin, "docs", "index.md"), "# Landing");
        await File.WriteAllTextAsync(Path.Combine(origin, "docs", "integrations", "citrix.md"), "# Citrix");

        LibGit2Sharp.Repository.Init(origin);
        using (var repo = new LibGit2Sharp.Repository(origin))
        {
            LibGit2Sharp.Commands.Stage(repo, "*");
            var who = new LibGit2Sharp.Signature("t", "t@t", DateTimeOffset.UtcNow);
            repo.Commit("init", who, who, new LibGit2Sharp.CommitOptions());
        }

        var config = new SiteConfig
        {
            ProjectRoot = projectRoot,
            ImportedDocs = new ImportedDocsConfig
            {
                PullSources =
                [
                    new ImportedDocsPullSource
                    {
                        Repository = origin,
                        SourcePath = "docs",
                        DestinationPath = "aws/iam",
                    },
                ],
            },
        };
        var site = new SiteContext
        {
            Config = config,
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };

        var plugin = new ImportedDocsPlugin();
        plugin.Configure(new FakeContext { Config = config });

        try
        {
            await plugin.OnImportAsync(site, default);

            // RelativePath drives the nav tree and .pages lookup, so it has to agree with Url.
            Assert.Equal(
                ["aws/iam/index.md", "aws/iam/integrations/citrix.md"],
                site.Pages.Select(p => p.RelativePath).Order());
            Assert.Equal(
                ["aws/iam/", "aws/iam/integrations/citrix/"],
                site.Pages.Select(p => p.Url).Order());
        }
        finally
        {
            DeleteTree(origin);
            DeleteTree(projectRoot);
        }
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    [Fact]
    public async Task OnImportAsync_PushedDocs_ParsesFullFrontMatterAndSetsOutputPath()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "netdocs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "imported", "guides"));
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "imported", "guides", "setup.md"),
            """
            ---
            title: Setup
            nav_title: Set It Up
            tags:
              - Alpha
              - Beta
            ---
            # Setup
            """);

        var config = new SiteConfig
        {
            ProjectRoot = projectRoot,
            ImportedDocs = new ImportedDocsConfig { PushedDocsDir = "imported" },
        };
        var site = new SiteContext
        {
            Config = config,
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };

        var plugin = new ImportedDocsPlugin();
        plugin.Configure(new FakeContext { Config = config });

        try
        {
            await plugin.OnImportAsync(site, default);

            var page = Assert.Single(site.Pages);
            Assert.Equal("imported/guides/setup/", page.Url);
            Assert.Equal(
                Path.Combine(config.AbsoluteSiteDir, "imported", "guides", "setup", "index.html"),
                page.OutputPath);
            Assert.Equal("Setup", page.Title);
            Assert.Equal("Set It Up", page.NavTitle);
            Assert.Equal(["Alpha", "Beta"], Assert.IsAssignableFrom<IEnumerable<object?>>(page.FrontMatter["tags"]));
            Assert.StartsWith("# Setup", page.RawMarkdown.TrimStart());
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private static SiteContext CreateTestSiteContext(string? projectRoot = null)
    {
        projectRoot ??= Path.Combine(Path.GetTempPath(), "netdocs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);

        var config = new SiteConfig { ProjectRoot = projectRoot };
        return new SiteContext
        {
            Config = config,
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };
    }

    [Fact]
    public async Task OnImportAsync_HandlesMissingImportedDocsConfig()
    {
        // Arrange
        var site = CreateTestSiteContext();
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        site.Config.ImportedDocs = null;
#pragma warning restore CS8625
        var plugin = new ImportedDocsPlugin();

        // Act
        await plugin.OnImportAsync(site, default);

        // Assert - should not throw
        Assert.NotNull(site);
    }

    [Fact]
    public async Task OnImportAsync_WithPushedDocsDir_HandlesEmptyDirectory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var projectDir = tempDir;
        var pushedDocsDir = Path.Combine(projectDir, "imported");

        // Create empty directory
        Directory.CreateDirectory(pushedDocsDir);

        var site = new SiteContext
        {
            Config = new SiteConfig { ProjectRoot = projectDir, ImportedDocs = new ImportedDocsConfig { PushedDocsDir = "imported" } },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };

        var plugin = new ImportedDocsPlugin();

        try
        {
            // Act
            await plugin.OnImportAsync(site, default);

            // Assert - should handle empty directory gracefully
            Assert.NotNull(site.Pages);
        }
        finally
        {
            if (Directory.Exists(projectDir))
                Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public async Task OnImportAsync_WithEmptyPullSources_OnlyProcessesPushed()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var site = new SiteContext
        {
            Config = new SiteConfig
            {
                ProjectRoot = tempDir,
                ImportedDocs = new ImportedDocsConfig
                {
                    PushedDocsDir = "imported",
                    PullSources = new List<ImportedDocsPullSource>()
                }
            },
            Options = new BuildOptions(),
            LoggerFactory = NullLoggerFactory.Instance,
        };

        var plugin = new ImportedDocsPlugin();

        try
        {
            // Act & Assert - should not throw
            await plugin.OnImportAsync(site, default);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task OnImportAsync_WithoutConfig_DoesNotThrow()
    {
        // Arrange
        var site = CreateTestSiteContext();
        var plugin = new ImportedDocsPlugin();

        // Act & Assert
        await plugin.OnImportAsync(site, default);
    }

    [Fact]
    public void Plugin_ImplementsIImportHook()
    {
        // Assert
        var plugin = new ImportedDocsPlugin();
        Assert.IsAssignableFrom<IImportHook>(plugin);
    }

    [Fact]
    public void ImportedDocsConfig_CanBeCreated()
    {
        // Arrange & Act
        var config = new ImportedDocsConfig
        {
            PushedDocsDir = "imported"
        };

        // Assert
        Assert.NotNull(config);
        Assert.Equal("imported", config.PushedDocsDir);
    }

    [Fact]
    public void ImportedDocsPullSource_CanBeCreated()
    {
        // Arrange & Act
        var source = new ImportedDocsPullSource
        {
            Repository = "https://github.com/owner/repo.git",
            SourcePath = "docs",
            DestinationPath = "external/docs"
        };

        // Assert
        Assert.NotNull(source);
        Assert.Equal("https://github.com/owner/repo.git", source.Repository);
        Assert.Equal("docs", source.SourcePath);
        Assert.Equal("external/docs", source.DestinationPath);
    }

    [Fact]
    public void ImportedDocsS3Source_CanBeCreated()
    {
        // Arrange & Act
        var source = new ImportedDocsS3Source
        {
            Bucket = "my-docs-bucket",
            Prefix = "docs/",
            Region = "us-east-1",
            DestinationPath = "products/api"
        };

        // Assert
        Assert.NotNull(source);
        Assert.Equal("my-docs-bucket", source.Bucket);
        Assert.Equal("docs/", source.Prefix);
        Assert.Equal("us-east-1", source.Region);
        Assert.Equal("products/api", source.DestinationPath);
    }

    [Fact]
    public void ImportedDocsS3Source_WithCredentials()
    {
        // Arrange & Act
        var source = new ImportedDocsS3Source
        {
            Bucket = "private-bucket",
            Prefix = "docs/",
            Region = "eu-west-1",
            DestinationPath = "external/docs",
            CredentialsEnvVar = "AWS_CREDENTIALS"
        };

        // Assert
        Assert.NotNull(source);
        Assert.Equal("AWS_CREDENTIALS", source.CredentialsEnvVar);
    }
}
