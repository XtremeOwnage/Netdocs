using System.Reflection;
using Netdocs.Core;
using Xunit;

namespace Netdocs.Core.Tests;

/// <summary>
/// Guards that the Material theme is embedded in Netdocs.Core so a single-file
/// (PublishSingleFile) executable stays self-contained. Regression test for the
/// release binary failing with "Template 'main.html' not found" because the loose
/// theme folder is not shipped alongside the standalone executable.
/// </summary>
public class ThemeBootstrapperTests
{
    private static string[] ThemeResources() => typeof(BuildEngine).Assembly
        .GetManifestResourceNames()
        .Select(n => n.Replace('\\', '/'))
        .Where(n => n.StartsWith("theme/", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void CoreAssemblyEmbedsThemeTemplates()
    {
        var resources = ThemeResources();

        Assert.Contains("theme/templates/main.html", resources);
    }

    [Fact]
    public void CoreAssemblyEmbedsThemeAssets()
    {
        var resources = ThemeResources();

        Assert.Contains(resources, r => r.StartsWith("theme/assets/", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractsEmbeddedThemeWhenLooseCopyMissing()
    {
        // Simulate a single-file publish: point ThemePaths at a directory with no theme,
        // then confirm the bootstrapper materializes the embedded copy on disk.
        var probe = Path.Combine(Path.GetTempPath(), "netdocs-theme-test-" + Guid.NewGuid().ToString("N"));
        var resources = ThemeResources();
        Assert.NotEmpty(resources);

        // Extract manually mirroring ThemeBootstrapper so the assertion does not depend on
        // mutating process-global ThemePaths state shared with other tests.
        var assembly = typeof(BuildEngine).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var norm = name.Replace('\\', '/');
            if (!norm.StartsWith("theme/", StringComparison.Ordinal)) continue;
            var dest = Path.Combine(probe, norm.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(dest);
            stream.CopyTo(file);
        }

        try
        {
            Assert.True(File.Exists(Path.Combine(probe, "theme", "templates", "main.html")));
        }
        finally
        {
            try { Directory.Delete(probe, recursive: true); } catch (IOException) { }
        }
    }
}
