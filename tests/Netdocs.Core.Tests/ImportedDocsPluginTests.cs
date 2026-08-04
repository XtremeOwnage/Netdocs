using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Netdocs.Abstractions;
using Netdocs.Plugins;
using Xunit;

namespace Netdocs.Core.Tests;

public class ImportedDocsPluginTests
{
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
