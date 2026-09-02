using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Content;

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
    private IReadOnlyList<ImportedDocsS3Source> _s3Sources = [];
    private SlugifyConfig? _slugify;
    private ILogger _logger = null!;

    public string Name => "imported-docs";

    public void Configure(IPluginContext ctx)
    {
        _projectRoot = ctx.Config.ProjectRoot;
        _logger = ctx.Logger;
        _slugify = ctx.Config.SlugifyUrls ? ctx.Config.Slugify : null;

        var config = ctx.Config.ImportedDocs;
        _pushedDocsDir = config.PushedDocsDir;
        _pullSources = config.PullSources;
        _s3Sources = config.S3Sources;

        _logger.LogInformation(
            "Imported docs plugin configured: {PushedDir} (pushed), {PullSources} pull sources, {S3Sources} S3 sources",
            _pushedDocsDir ?? "disabled",
            _pullSources.Count,
            _s3Sources.Count);
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

        // 3. Import S3 docs (from S3 buckets).
        foreach (var source in _s3Sources)
        {
            imported += await ImportS3SourceAsync(site, source, ct);
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

            var relPath = CombineDestination(
                Path.GetRelativePath(baseDir, filePath).Replace('\\', '/'),
                source?.DestinationPath);
            var url = ContentDiscovery.UrlFor(relPath, _slugify);

            var page = new Page
            {
                SourcePath = filePath,
                RelativePath = relPath,
                Url = url,
                OutputPath = Path.Combine(site.Config.AbsoluteSiteDir, ContentDiscovery.OutputFileFor(url)),
                RawMarkdown = content,
                IsGenerated = false
            };

            ApplyFrontMatter(page, source?.FrontMatterDefaults);

            if (source?.IncludeSourceMarker == true)
            {
                AddSourceMarker(page, source.Repository);
            }

            return page;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load imported page from {File}", filePath);
            return null;
        }
    }

    /// <summary>Parses the page's own front matter with the same reader used for discovered
    /// pages, then fills any key the source did not set from the configured defaults.</summary>
    private static void ApplyFrontMatter(Page page, IReadOnlyDictionary<string, object?>? defaults)
    {
        var (parsed, body) = FrontMatter.Split(page.RawMarkdown);
        page.RawMarkdown = body;

        var meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parsed)
            meta[key] = value;

        if (defaults is not null)
        {
            foreach (var (key, value) in defaults)
                meta.TryAdd(key, value);
        }

        page.FrontMatter = meta;

        if (meta.TryGetValue("title", out var t) && t is string title && title.Length > 0)
            page.Title = title;
        else if (string.IsNullOrEmpty(page.Title))
            page.Title = Path.GetFileNameWithoutExtension(page.RelativePath);

        if (meta.TryGetValue("page_title", out var pt) && pt is string pageTitle && pageTitle.Length > 0)
            page.PageTitle = pageTitle;
        if (meta.TryGetValue("nav_title", out var nt) && nt is string navTitle && navTitle.Length > 0)
            page.NavTitle = navTitle;
        if (meta.TryGetValue("tag_title", out var gt) && gt is string tagTitle && tagTitle.Length > 0)
            page.TagTitle = tagTitle;
    }

    private static void AddSourceMarker(Page page, string source)
    {
        var meta = new Dictionary<string, object?>(page.FrontMatter, StringComparer.OrdinalIgnoreCase);
        meta.TryAdd("import_source", source);
        meta.TryAdd("import_url", page.Url);
        page.FrontMatter = meta;
    }

    /// <summary>Nests a source-relative path beneath <paramref name="destinationPath"/>. Imported
    /// pages carry this as their <see cref="Page.RelativePath"/> so navigation, <c>.pages</c>
    /// lookups and URLs all agree on where the page lives.</summary>
    private static string CombineDestination(string relativePath, string? destinationPath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');

        return string.IsNullOrEmpty(destinationPath)
            ? path
            : destinationPath.Replace('\\', '/').Trim('/') + "/" + path;
    }

    internal string ComputeUrl(string relativePath, string? destinationPath) =>
        ContentDiscovery.UrlFor(CombineDestination(relativePath, destinationPath), _slugify);

    private async Task<int> ImportS3SourceAsync(SiteContext site, ImportedDocsS3Source source, CancellationToken ct)
    {
        _logger.LogInformation("Importing docs from S3: s3://{Bucket}/{Prefix}", source.Bucket, source.Prefix);

        try
        {
            var s3Client = CreateS3Client(source);
            var count = 0;
            var excludePatterns = source.Exclude?.ToList() ?? [];

            // List all objects in the bucket with the given prefix
            var request = new ListObjectsV2Request
            {
                BucketName = source.Bucket,
                Prefix = source.Prefix
            };

            ListObjectsV2Response? response = null;
            do
            {
                if (ct.IsCancellationRequested) break;

                response = await s3Client.ListObjectsV2Async(request, ct);

                foreach (var obj in response.S3Objects)
                {
                    if (ct.IsCancellationRequested) break;

                    // Only process markdown files
                    if (!obj.Key.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Get relative path from the prefix
                    var relPath = obj.Key.Substring(source.Prefix.Length).TrimStart('/');

                    if (ShouldExclude(relPath, excludePatterns))
                    {
                        _logger.LogTrace("Skipping excluded file: {File}", relPath);
                        continue;
                    }

                    var page = await LoadPageFromS3Async(s3Client, source, site, obj.Key, relPath, ct);
                    if (page is not null)
                    {
                        site.Pages.Add(page);
                        count++;
                        _logger.LogTrace("Imported S3 page {Url} from s3://{Bucket}/{Key}", page.Url, source.Bucket, obj.Key);
                    }
                }

                request.ContinuationToken = response.ContinuationToken;
                // AWS SDK v4 made IsTruncated nullable. Unset means the response did not claim
                // there is more to fetch, so treat anything but an explicit true as the last page
                // rather than paginating forever.
            } while (response.IsTruncated == true && !ct.IsCancellationRequested);

            _logger.LogInformation("Imported {Count} pages from S3 bucket {Bucket}", count, source.Bucket);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import from S3 bucket {Bucket}", source.Bucket);
            return 0;
        }
    }

    private IAmazonS3 CreateS3Client(ImportedDocsS3Source source)
    {
        var config = new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(source.Region) };

        // Check for explicit credentials in environment variable
        if (!string.IsNullOrEmpty(source.CredentialsEnvVar))
        {
            var credsStr = Environment.GetEnvironmentVariable(source.CredentialsEnvVar);
            if (!string.IsNullOrEmpty(credsStr))
            {
                var parts = credsStr.Split(':');
                if (parts.Length == 2)
                {
                    var credentials = new Amazon.Runtime.BasicAWSCredentials(parts[0], parts[1]);
                    return new AmazonS3Client(credentials, config);
                }
            }
        }

        // Use default credential chain (IAM role, ~/.aws/credentials, env vars, etc.)
        return new AmazonS3Client(config);
    }

    private async Task<Page?> LoadPageFromS3Async(
        IAmazonS3 s3Client,
        ImportedDocsS3Source source,
        SiteContext site,
        string s3Key,
        string relPath,
        CancellationToken ct)
    {
        try
        {
            // Download the object
            var getRequest = new GetObjectRequest { BucketName = source.Bucket, Key = s3Key };
            using var response = await s3Client.GetObjectAsync(getRequest, ct);
            using var reader = new StreamReader(response.ResponseStream);
            var content = await reader.ReadToEndAsync(ct);

            var sitePath = CombineDestination(relPath, source.DestinationPath);
            var url = ContentDiscovery.UrlFor(sitePath, _slugify);

            var page = new Page
            {
                SourcePath = $"s3://{source.Bucket}/{s3Key}",
                RelativePath = sitePath,
                Url = url,
                OutputPath = Path.Combine(site.Config.AbsoluteSiteDir, ContentDiscovery.OutputFileFor(url)),
                RawMarkdown = content,
            };

            ApplyFrontMatter(page, source.FrontMatterDefaults);

            if (source.IncludeSourceMarker)
            {
                AddSourceMarker(page, $"https://{source.Bucket}.s3.amazonaws.com/{s3Key}");
            }

            return page;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load S3 object s3://{Bucket}/{Key}", source.Bucket, s3Key);
            return null;
        }
    }
}
