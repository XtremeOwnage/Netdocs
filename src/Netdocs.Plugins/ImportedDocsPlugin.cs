using System.Text.RegularExpressions;
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
        // TODO: Implement git pulling, cloning, branch/tag/commit checkout
        // For now, log that pull-based imports are not yet implemented
        _logger.LogWarning("Pull-based imports not yet implemented for {Repository}", source.Repository);
        return 0;
    }

    private async Task<Page?> LoadPageFromFileAsync(
        string filePath,
        string projectRoot,
        ImportedDocsPullSource? source,
        SiteContext site)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var relPath = Path.GetRelativePath(projectRoot, filePath).Replace('\\', '/');
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
