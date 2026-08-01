using System.Text.RegularExpressions;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Handles federated documentation imports from external repositories.
/// Supports both push-based (docs pushed to a staging directory) and pull-based
/// (this site pulls from external git repositories) integration modes.
/// </summary>
public sealed class ImportedDocsPlugin : IPlugin, IImportHook
{
    private string _projectRoot = "";
    private string? _pushedDocsDir;
    private IReadOnlyList<ImportedDocsPullSource> _pullSources = [];
    private ILogger _logger = null!;

    public string Name => "imported-docs";

    public void Configure(IPluginContext ctx)
    {
        _projectRoot = ctx.Config.ProjectRoot;
        _logger = ctx.Logger;

        var config = ctx.Config.ImportedDocs;
        _pushedDocsDir = config.PushedDocsDir;
        _pullSources = config.PullSources;

        _logger.LogInformation(
            "Imported docs plugin configured: {PushedDir} (pushed), {PullSources} pull sources",
            _pushedDocsDir ?? "disabled",
            _pullSources.Count);
    }

    public async Task OnImportAsync(SiteContext site, CancellationToken ct)
    {
        var imported = 0;

        // 1. Import pushed docs (workflow-delivered).
        if (_pushedDocsDir is not null)
        {
            imported += await ImportPushedDocsAsync(site, ct);
        }

        // 2. Import pulled docs (from external repos).
        foreach (var source in _pullSources)
        {
            imported += await ImportPullSourceAsync(site, source, ct);
        }

        if (imported > 0)
        {
            _logger.LogInformation("Imported {Count} pages from external sources", imported);
        }
    }

    private async Task<int> ImportPushedDocsAsync(SiteContext site, CancellationToken ct)
    {
        var pushedPath = Path.Combine(_projectRoot, _pushedDocsDir!);
        if (!Directory.Exists(pushedPath))
        {
            _logger.LogDebug("Pushed docs directory does not exist: {Path}", pushedPath);
            return 0;
        }

        var count = 0;
        var mdFiles = Directory.EnumerateFiles(pushedPath, "*.md", SearchOption.AllDirectories);

        foreach (var file in mdFiles)
        {
            if (ct.IsCancellationRequested) break;

            var page = await LoadPageFromFileAsync(file, _projectRoot, null, site);
            if (page is not null)
            {
                site.Pages.Add(page);
                count++;
                _logger.LogTrace("Imported pushed page {Url} from {File}", page.Url, file);
            }
        }

        return count;
    }

    private async Task<int> ImportPullSourceAsync(
        SiteContext site,
        ImportedDocsPullSource source,
        CancellationToken ct)
    {
        try
        {
            // Setup temporary directory for cloning
            var tempDir = Path.Combine(Path.GetTempPath(), "netdocs-import", Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            try
            {
                return await PullAndImportAsync(site, source, tempDir, ct);
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    _logger.LogWarning("Failed to clean up temporary directory: {Path}", tempDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import from pull source {Repository}", source.Repository);
            return 0;
        }
    }

    private async Task<int> PullAndImportAsync(
        SiteContext site,
        ImportedDocsPullSource source,
        string tempDir,
        CancellationToken ct)
    {
        _logger.LogInformation("Cloning {Repository} (ref: {Reference})", 
            source.Repository, source.Reference ?? "default");

        try
        {
            // Prepare clone options
            var cloneOptions = new CloneOptions();

            // Set branch if specified
            if (!string.IsNullOrEmpty(source.Reference))
            {
                cloneOptions.BranchName = source.Reference;
            }

            // Clone the repository
            // Note: For private repos, ensure SSH key is configured or use https with token in URL
            Repository.Clone(source.Repository, tempDir, cloneOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository {Repository}", source.Repository);
            return 0;
        }

        using var repo = new Repository(tempDir);

        // Discover and import markdown files
        var sourcePath = source.SourcePath ?? "docs";
        var docsPath = Path.Combine(tempDir, sourcePath);

        if (!Directory.Exists(docsPath))
        {
            _logger.LogWarning("Source path does not exist in {Repository}: {SourcePath}", 
                source.Repository, sourcePath);
            return 0;
        }

        var count = 0;
        var excludePatterns = source.Exclude?.ToList() ?? [];
        var mdFiles = Directory.EnumerateFiles(docsPath, "*.md", SearchOption.AllDirectories);

        foreach (var file in mdFiles)
        {
            if (ct.IsCancellationRequested) break;

            // Check if file matches exclusion patterns
            var relPath = Path.GetRelativePath(docsPath, file);
            if (ShouldExclude(relPath, excludePatterns))
            {
                _logger.LogTrace("Skipping excluded file: {File}", relPath);
                continue;
            }

            var page = await LoadPageFromFileAsync(file, docsPath, source, site);
            if (page is not null)
            {
                site.Pages.Add(page);
                count++;
                _logger.LogTrace("Imported pulled page {Url} from {File}", page.Url, file);
            }
        }

        _logger.LogInformation("Imported {Count} pages from {Repository}", count, source.Repository);
        return count;
    }

    private bool ShouldExclude(string relativePath, List<string> patterns)
    {
        if (patterns.Count == 0) return false;

        var pathForwardSlash = relativePath.Replace('\\', '/');
        foreach (var pattern in patterns)
        {
            // Simple glob matching: * = any chars in segment, ** = any dirs
            if (GlobMatch(pathForwardSlash, pattern))
                return true;
        }
        return false;
    }

    private bool GlobMatch(string path, string pattern)
    {
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*\*", "(?:.*/)?")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".") + "$";

        return Regex.IsMatch(path, regexPattern);
    }

    private async Task<Page?> LoadPageFromFileAsync(
        string filePath,
        string baseDir,
        ImportedDocsPullSource? source,
        SiteContext site)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var relPath = Path.GetRelativePath(baseDir, filePath).Replace('\\', '/');
            var url = ComputeUrl(relPath, source?.DestinationPath);

            var page = new Page
            {
                SourcePath = filePath,
                RelativePath = relPath,
                Url = url,
                OutputPath = Path.Combine(site.Config.AbsoluteSiteDir, url.TrimStart('/'), "index.html"),
                RawMarkdown = content,
                IsGenerated = false
            };

            // Extract front-matter and apply overrides
            ExtractFrontMatterAndApplyOverrides(page, source);

            if (source?.IncludeSourceMarker == true)
            {
                page.Meta["import_source"] = source.Repository;
                page.Meta["import_url"] = source.Repository;
            }

            return page;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load imported page from {File}", filePath);
            return null;
        }
    }

    private void ExtractFrontMatterAndApplyOverrides(Page page, ImportedDocsPullSource? source)
    {
        var lines = page.RawMarkdown.Split('\n');
        var meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Simple YAML front-matter parsing (---...---)
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var endIdx = Array.FindIndex(lines, 1, l => l.Trim() == "---");
            if (endIdx > 1)
            {
                for (int i = 1; i < endIdx; i++)
                {
                    var line = lines[i];
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var key = line[..colonIdx].Trim();
                        var value = line[(colonIdx + 1)..].Trim();
                        meta[key] = ParseYamlValue(value);
                    }
                }

                // Remove front-matter from raw markdown
                page.RawMarkdown = string.Join("\n", lines[(endIdx + 1)..]);
            }
        }

        // Apply front-matter defaults from the import source config
        if (source?.FrontMatterDefaults is not null)
        {
            foreach (var (key, value) in source.FrontMatterDefaults)
            {
                meta.TryAdd(key, value);
            }
        }

        page.FrontMatter = meta;

        // Populate title if present in front-matter
        if (meta.TryGetValue("title", out var titleObj) && titleObj is string title)
        {
            page.Title = title;
        }
        else if (string.IsNullOrEmpty(page.Title))
        {
            page.Title = Path.GetFileNameWithoutExtension(page.SourcePath);
        }
    }

    private object? ParseYamlValue(string value)
    {
        value = value.Trim('"', '\'');
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (int.TryParse(value, out var intVal)) return intVal;
        return value;
    }

    private string ComputeUrl(string relativePath, string? destinationPath)
    {
        // Remove .md extension, use forward slashes, add trailing slash
        var path = relativePath[..^3]; // Remove ".md"
        path = path.Replace('\\', '/');

        if (!string.IsNullOrEmpty(destinationPath))
        {
            path = destinationPath.TrimEnd('/') + "/" + Path.GetFileName(path);
        }

        return "/" + path.Trim('/') + "/";
    }
}
