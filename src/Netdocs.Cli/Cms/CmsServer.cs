using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Cms;
using Netdocs.Core.Plugins;

namespace Netdocs.Cli.Cms;

/// <summary>
/// The local CMS that <c>netdocs serve</c> hosts at <c>/__cms/</c>: a JSON API over the site's
/// own files plus the bundled admin app and in-page editor. There is no database; every change is
/// written to the markdown, media and <c>appsettings.json</c> files, and the dev server's normal
/// rebuild picks it up.
/// <para>The server only listens on localhost. Every API call must carry the per-run token in the
/// <c>X-Netdocs-Cms</c> header and arrive with a localhost <c>Host</c>, so other websites open in
/// the browser cannot drive the API (no CORS, and the custom header forces a preflight that is never
/// answered) and DNS rebinding is refused.</para>
/// </summary>
public sealed class CmsServer
{
    public const string BasePath = "/__cms";
    public const string TokenHeader = "X-Netdocs-Cms";

    private readonly string _configPath;
    private readonly BuildOptions _options;
    private readonly PluginRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly Lock _lock = new();

    private CmsWorkspace _workspace;
    private NavService _nav;
    private ContentStore _content;
    private TaxonomyService _taxonomy;
    private MediaService _media;
    private GitService _git;
    private TemplateService _templates;
    private PluginService _plugins;
    private CmsRenderer? _renderer;
    private Dictionary<string, string> _urlToPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _pathToUrl = new(StringComparer.OrdinalIgnoreCase);

    public CmsServer(string configPath, SiteConfig config, BuildOptions options, PluginRegistry registry, ILoggerFactory loggerFactory)
    {
        _configPath = configPath;
        _options = options;
        _registry = registry;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger("cms");
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _workspace = new CmsWorkspace(configPath, config);
        _nav = new NavService(_workspace);
        _content = new ContentStore(_workspace, _nav);
        _taxonomy = new TaxonomyService(_workspace, _content);
        _media = new MediaService(_workspace, _content);
        _git = new GitService(_workspace);
        _templates = new TemplateService(_workspace);
        _plugins = new PluginService(_workspace);
    }

    /// <summary>Random per-run secret the admin app and in-page editor send with each API call.</summary>
    public string Token { get; }

    /// <summary>Called by the dev server after every build, with the pages it produced.</summary>
    public void OnBuilt(SiteConfig config, SiteContext? site)
    {
        lock (_lock)
        {
            if (!string.Equals(Path.GetFullPath(config.AbsoluteDocsDir), _workspace.DocsDir, CmsWorkspace.PathComparison))
            {
                _workspace = new CmsWorkspace(_configPath, config);
                _nav = new NavService(_workspace);
                _content = new ContentStore(_workspace, _nav);
                _taxonomy = new TaxonomyService(_workspace, _content);
                _media = new MediaService(_workspace, _content);
                _git = new GitService(_workspace);
                _templates = new TemplateService(_workspace);
                _plugins = new PluginService(_workspace);
            }
            else
            {
                _workspace.Reload();
            }

            if (site is not null)
            {
                var pathToUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var urlToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var page in site.Pages.Where(p => !p.IsGenerated && p.SourcePath.Length > 0))
                {
                    var full = Path.GetFullPath(page.SourcePath);
                    if (!CmsWorkspace.IsUnder(full, _workspace.DocsDir)) continue;
                    var rel = _workspace.ToDocsRelative(full);
                    pathToUrl[page.RelativePath] = page.Url;
                    pathToUrl[rel] = page.Url;
                    urlToPath[page.Url.Trim('/')] = rel;
                }
                _pathToUrl = pathToUrl;
                _urlToPath = urlToPath;
                _content.Urls = pathToUrl;
            }

            try
            {
                _renderer = new CmsRenderer(config, _options, _registry, _loggerFactory) { LinkMap = _pathToUrl };
            }
            catch (Exception ex)
            {
                _log.LogWarning("The CMS preview renderer could not start: {Message}", ex.Message);
            }
        }
    }

    private CmsRenderer Renderer => _renderer ?? throw new CmsException("The site has not finished building yet.", 503);

    /// <summary>The docs-relative source of a built page URL (site-relative, e.g. <c>blog/post/</c>).</summary>
    public string? SourceForUrl(string url) => _urlToPath.GetValueOrDefault(url.Trim('/'));

    // --- Request handling ------------------------------------------------------------------

    public void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(BasePath + "/api");
        api.AddEndpointFilter(async (ctx, next) =>
        {
            var request = ctx.HttpContext.Request;
            if (!IsLocalHost(request.Host.Host))
                return Results.Problem("The CMS only answers on localhost.", statusCode: 403);
            // <img src> and <a href> cannot send headers, so read-only file fetches may carry the
            // token in the query string instead.
            var token = request.Headers.TryGetValue(TokenHeader, out var header) ? header.ToString()
                : HttpMethods.IsGet(request.Method) && (request.Path.StartsWithSegments(BasePath + "/api/media/file") || request.Path.StartsWithSegments(BasePath + "/api/git/file")) ? request.Query["t"].ToString()
                : "";
            if (token.Length == 0 || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(Token)))
                return Results.Problem("Missing or wrong CMS token. Reload the page.", statusCode: 403);
            try
            {
                return await next(ctx);
            }
            catch (CmsException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Always answer with JSON, so the browser shows the reason instead of a network error.
                _log.LogWarning(ex, "CMS request {Path} failed", request.Path);
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        // Site
        api.MapGet("/site", () => new
        {
            siteName = _workspace.Config.SiteName,
            docsDir = _workspace.Config.DocsDir,
            configFile = Path.GetFileName(_workspace.ConfigPath),
            projectRoot = _workspace.ProjectRoot,
            customDir = _workspace.Config.Theme.CustomDir,
            features = TemplateService.KnownFeatures,
            colors = TemplateService.Colors,
            siteKeys = TemplateService.SiteKeys,
        });

        // Pages
        api.MapGet("/pages", () => _content.List());
        api.MapGet("/page", (string path) => _content.Read(path));
        api.MapGet("/page/by-url", (string url) =>
            SourceForUrl(Uri.UnescapeDataString(url)) is { } p ? Results.Ok(new { path = p }) : Results.Json(new { error = "No source page for this URL." }, statusCode: 404));
        api.MapPost("/page/save", (SavePage req) => _content.Save(req.Path, req.Hash, ToPlain(req.FrontMatter), req.Body));
        api.MapPost("/page/raw", (SaveRaw req) => _content.SaveRaw(req.Path, req.Hash, req.Raw));
        api.MapPost("/page/create", (CreatePage req) => _content.Create(req.Path, req.Title, ToPlain(req.FrontMatter), req.Body, req.AddToNav));
        api.MapPost("/page/move", (MovePage req) => _content.Move(req.From, req.To));
        api.MapPost("/page/delete", (PathRequest req) => { _content.Delete(req.Path); return Results.Ok(new { ok = true }); });
        api.MapGet("/page/references", (string path) => _content.ReferencesTo(path));

        api.MapGet("/page/blocks", async (string path, CancellationToken ct) =>
        {
            var doc = _content.Read(path);
            var blocks = await Renderer.RenderBlocksAsync(doc.Path, doc.FrontMatter, doc.Body, ct);
            return new { page = doc, blocks };
        });
        api.MapPost("/page/blocks/save", async (SaveBlocks req, CancellationToken ct) =>
        {
            var doc = _content.Read(req.Path);
            if (req.Hash is not null && req.Hash != doc.Hash)
                throw new CmsConflictException("This page was changed outside the editor since you opened it. Reload it to get the latest version.");
            var body = MarkdownBlocks.Apply(doc.Body, Renderer.Pipeline, [.. req.Blocks.Select(b => new BlockEdit(b.Keep, b.Source))]);
            var saved = _content.Save(req.Path, req.Hash, ToPlain(req.FrontMatter), body);
            var blocks = await Renderer.RenderBlocksAsync(saved.Path, saved.FrontMatter, saved.Body, ct);
            return new { page = saved, blocks };
        });
        api.MapPost("/render", async (RenderRequest req, CancellationToken ct) =>
        {
            var fm = ToPlain(req.FrontMatter) ?? new Dictionary<string, object?>();
            if (req.Blocks)
                return Results.Ok(new { blocks = await Renderer.RenderBlocksAsync(req.Path, fm, req.Body, ct) });
            return Results.Ok(new { html = await Renderer.RenderAsync(req.Path, fm, req.Body, ct) });
        });

        // Navigation
        api.MapGet("/nav", () => new { nav = _nav.Read(), pages = _content.List().Select(p => new { p.Path, p.Title, p.InNav }) });
        api.MapPost("/nav", (NavRequest req) => { _nav.Write(req.Nav); return Results.Ok(new { nav = _nav.Read() }); });

        // Tags and categories
        api.MapGet("/taxonomy", (string field) =>
        {
            var terms = _taxonomy.List(field);
            var (similar, single) = _taxonomy.Report(field);
            return new { field, terms, similar, singleUse = single.Select(s => s.Name) };
        });
        api.MapPost("/taxonomy/rename", (RenameTerms req) => new { changed = _taxonomy.Rename(req.Field, req.From, req.To) });
        api.MapPost("/taxonomy/delete", (DeleteTerms req) => new { changed = _taxonomy.Delete(req.Field, req.Names) });
        api.MapPost("/taxonomy/assign", (AssignTerms req) => new { changed = _taxonomy.Assign(req.Field, req.Pages, req.Add ?? [], req.Remove ?? []) });

        // Media
        api.MapGet("/media", () => _media.List());
        api.MapGet("/media/file", (string path) =>
        {
            var full = _media.Locate(path);
            return Results.File(full, ContentTypes.For(full), enableRangeProcessing: true);
        });
        api.MapPost("/media/upload", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) throw new CmsException("Send the files as multipart/form-data.");
            var form = await request.ReadFormAsync(ct);
            var folder = form["folder"].ToString();
            var overwrite = form["overwrite"].ToString() == "true";
            var saved = new List<string>();
            foreach (var file in form.Files)
            {
                if (file.Length > MediaService.MaxUploadBytes) throw new CmsException($"'{file.FileName}' is larger than 100 MB.");
                await using var stream = file.OpenReadStream();
                saved.Add(await _media.UploadAsync(folder, file.FileName, stream, overwrite, ct));
            }
            return Results.Ok(new { saved });
        }).DisableAntiforgery();
        api.MapPost("/media/delete", (DeleteMedia req) => { _media.Delete(req.Path, req.Force); return Results.Ok(new { ok = true }); });
        api.MapPost("/media/move", (MovePage req) => new { updatedPages = _media.Move(req.From, req.To) });
        api.MapPost("/media/webp", async (WebpRequest req, CancellationToken ct) =>
            await _media.ConvertToWebpAsync(req.Paths, req.Quality ?? 80, req.MaxWidth, ct));
        api.MapPost("/media/resize", async (ResizeRequest req, CancellationToken ct) => new { size = await _media.ResizeAsync(req.Path, req.MaxWidth, ct) });

        // Source control
        api.MapGet("/git/status", (CancellationToken ct) => _git.StatusAsync(ct));
        api.MapGet("/git/diff", async (string? path, bool? staged, CancellationToken ct) => new { diff = await _git.DiffAsync(path, staged ?? false, ct) });
        api.MapPost("/git/stage", (PathsRequest req, CancellationToken ct) => _git.StageAsync(req.Paths ?? [], ct));
        api.MapPost("/git/unstage", (PathsRequest req, CancellationToken ct) => _git.UnstageAsync(req.Paths ?? [], ct));
        api.MapPost("/git/discard", (PathsRequest req, CancellationToken ct) => _git.DiscardAsync(req.Paths ?? [], ct));
        api.MapPost("/git/commit", (CommitRequest req, CancellationToken ct) => _git.CommitAsync(req.Message, req.StageAll, ct));
        // Not "/git/log": privacy blocklists in browser extensions block URLs containing "/log?".
        api.MapGet("/git/history", (int? count, CancellationToken ct) => _git.LogAsync(count ?? 30, ct));
        api.MapGet("/git/file", async (string path, bool? committed, CancellationToken ct) =>
        {
            var bytes = await _git.ReadFileAsync(path, committed ?? false, ct);
            return bytes is null ? Results.NotFound() : Results.Bytes(bytes, ContentTypes.For(path));
        });
        api.MapGet("/git/branches", async (CancellationToken ct) =>
        {
            var (current, local, remote) = await _git.BranchesAsync(ct);
            return new { current, local, remote };
        });
        api.MapPost("/git/checkout", (CheckoutRequest req, CancellationToken ct) => _git.CheckoutAsync(req.Branch, req.Create, ct));
        api.MapPost("/git/pull", (CancellationToken ct) => _git.PullAsync(ct));
        api.MapPost("/git/push", (CancellationToken ct) => _git.PushAsync(ct));
        api.MapPost("/git/fetch", (CancellationToken ct) => _git.FetchAsync(ct));

        // Blog authors (for the author picker)
        api.MapGet("/blog/authors", () => BlogAuthors.List(_workspace));

        // Icons (for the icon picker)
        api.MapGet("/icons", (string? q, int? limit) => IconCatalog.Search(q ?? "", Math.Clamp(limit ?? 120, 1, 500)));

        // Theme, templates and settings
        api.MapGet("/templates", () => _templates.List());
        api.MapGet("/template", (string name) =>
        {
            var (text, original, overridden) = _templates.Read(name);
            return new { name, text, original, overridden };
        });
        api.MapPost("/template", (SaveTemplate req) => { _templates.Save(req.Name, req.Text); return Results.Ok(new { ok = true }); });
        api.MapPost("/template/revert", (NameRequest req) => { _templates.Revert(req.Name); return Results.Ok(new { ok = true }); });
        // Plugins
        api.MapGet("/plugins", () => PluginList(_plugins.List()));
        api.MapPost("/plugins/save", (SavePlugin req) => PluginList(_plugins.Save(req.Name, req.Enabled, req.Options)));
        api.MapPost("/plugins/remove", (NameRequest req) => PluginList(_plugins.Remove(req.Name)));
        api.MapGet("/plugins/abbreviations", (string? file) => _plugins.ReadAbbreviations(file));
        api.MapPost("/plugins/abbreviations", (SaveAbbreviations req) => _plugins.SaveAbbreviations(req.File, req.Entries ?? []));
        api.MapGet("/plugins/file-filter", () => _plugins.ReadFileFilter());
        api.MapPost("/plugins/file-filter", (SaveFileFilter req) => _plugins.SaveFileFilter(req.Yaml, req.Fields, req.IgnoreText));
        api.MapGet("/plugins/blog-authors", () => new { file = _plugins.AuthorsFile(), authors = _plugins.ReadAuthors() });
        api.MapPost("/plugins/blog-authors", (SaveAuthors req) => new { file = _plugins.AuthorsFile(), authors = _plugins.SaveAuthors(req.Authors ?? []) });
        api.MapGet("/plugins/social-links", () => new { links = _plugins.ReadSocialLinks() });
        api.MapPost("/plugins/social-links", (SaveSocialLinks req) => new { links = _plugins.SaveSocialLinks(req.Links ?? []) });

        api.MapGet("/settings", () => _templates.ReadSettings());
        api.MapPost("/settings", (JsonObject settings) => { _templates.SaveSettings(settings); return Results.Ok(_templates.ReadSettings()); });
        api.MapGet("/config", () => new { text = _templates.ReadConfigFile() });
        api.MapPost("/config", (ConfigRequest req) => { _templates.SaveConfigFile(req.Text); return Results.Ok(new { ok = true }); });

        // Admin app and in-page editor assets.
        app.MapGet(BasePath + "/{**file}", (string? file) => ServeAsset(file));
    }

    private IResult ServeAsset(string? file)
    {
        file = string.IsNullOrEmpty(file) ? "index.html" : file;
        // The admin app routes on the URL hash, so unknown paths fall back to the app shell.
        var stream = OpenAsset(file) ?? (Path.HasExtension(file) ? null : OpenAsset("index.html"));
        if (stream is null) return Results.NotFound();
        if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(file))
        {
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd().Replace("%NETDOCS_CMS_TOKEN%", Token, StringComparison.Ordinal);
            return Results.Content(html, "text/html; charset=utf-8");
        }
        return Results.Stream(stream, ContentTypes.For(file));
    }

    internal static Stream? OpenAsset(string file)
    {
        var name = "cms/" + file.Replace('\\', '/').TrimStart('/');
        if (name.Contains("..", StringComparison.Ordinal)) return null;
        var assembly = typeof(CmsServer).Assembly;
        // MSBuild's %(RecursiveDir) uses the build machine's separator in resource names.
        return assembly.GetManifestResourceStream(name) ?? assembly.GetManifestResourceStream(name.Replace('/', '\\'));
    }

    /// <summary>The script tag the dev server adds to every page, which loads the in-page editor.</summary>
    public string InlineEditorTag(string? sourcePath)
    {
        var config = JsonSerializer.Serialize(new { token = Token, api = BasePath + "/api", admin = BasePath + "/", path = sourcePath });
        return $"""
            <link rel="stylesheet" href="{BasePath}/inline.css">
            <script>window.__netdocsCms={config};</script>
            <script type="module" src="{BasePath}/inline.js"></script>
            """;
    }

    private static bool IsLocalHost(string host) =>
        host is "localhost" or "127.0.0.1" or "[::1]" or "::1" || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts JSON front matter from the browser to the plain objects the YAML writer uses.</summary>
    internal static Dictionary<string, object?>? ToPlain(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (element.Value.ValueKind != JsonValueKind.Object) throw new CmsException("Front matter must be an object.");
        return (Dictionary<string, object?>)Plain(element.Value)!;
    }

    private static object? Plain(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => Plain(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(Plain).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    // --- Request bodies -----------------------------------------------------------------------

    public sealed record PathRequest(string Path);
    public sealed record PathsRequest(List<string>? Paths);
    public sealed record NameRequest(string Name);
    public sealed record SavePlugin(string Name, bool Enabled, JsonObject? Options);
    public sealed record SaveAbbreviations(string? File, List<Abbreviation>? Entries);
    public sealed record SaveFileFilter(string? Yaml, JsonObject? Fields, string? IgnoreText);
    public sealed record SaveAuthors(List<BlogAuthor>? Authors);
    public sealed record SaveSocialLinks(JsonArray? Links);

    private object PluginList(IReadOnlyList<PluginEntry> entries) => new
    {
        plugins = entries.Select(e => new { e.Name, e.Enabled, e.Options, e.Index, known = _registry.Contains(e.Name) }),
    };
    public sealed record SavePage(string Path, string? Hash, JsonElement? FrontMatter, string? Body);
    public sealed record SaveRaw(string Path, string? Hash, string Raw);
    public sealed record CreatePage(string Path, string? Title, JsonElement? FrontMatter, string? Body, bool AddToNav);
    public sealed record MovePage(string From, string To);
    public sealed record BlockEditDto(int? Keep, string? Source);
    public sealed record SaveBlocks(string Path, string? Hash, List<BlockEditDto> Blocks, JsonElement? FrontMatter);
    public sealed record RenderRequest(string Path, string Body, JsonElement? FrontMatter, bool Blocks);
    public sealed record NavRequest(JsonArray Nav);
    public sealed record RenameTerms(string Field, List<string> From, string To);
    public sealed record DeleteTerms(string Field, List<string> Names);
    public sealed record AssignTerms(string Field, List<string> Pages, List<string>? Add, List<string>? Remove);
    public sealed record DeleteMedia(string Path, bool Force);
    public sealed record WebpRequest(List<string> Paths, int? Quality, int? MaxWidth);
    public sealed record ResizeRequest(string Path, int MaxWidth);
    public sealed record CommitRequest(string Message, bool StageAll);
    public sealed record CheckoutRequest(string Branch, bool Create);
    public sealed record SaveTemplate(string Name, string Text);
    public sealed record ConfigRequest(string Text);
}

internal static class ContentTypes
{
    public static string For(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css",
        ".js" or ".mjs" => "text/javascript",
        ".json" => "application/json",
        ".map" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".pdf" => "application/pdf",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        ".csv" => "text/csv",
        ".xml" => "application/xml",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };
}
