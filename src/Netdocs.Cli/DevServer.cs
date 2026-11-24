using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Cli.Cms;
using Netdocs.Core;
using Netdocs.Core.Configuration;
using Netdocs.Core.Plugins;

namespace Netdocs.Cli;

/// <summary>Kestrel static host with FileSystemWatcher-driven rebuilds and WebSocket live reload.
/// With <paramref name="cms"/> on (the default for <c>netdocs serve</c>), it also hosts the local
/// CMS at <c>/__cms/</c> and adds the in-page editor to every served page.</summary>
public sealed class DevServer(
    string configPath,
    SiteConfig config,
    BuildOptions options,
    PluginRegistry registry,
    ILoggerFactory loggerFactory,
    int port,
    bool cms = false)
{
    private readonly List<WebSocket> _clients = [];
    private readonly Lock _clientsLock = new();
    private readonly ILogger _log = loggerFactory.CreateLogger("serve");
    private readonly CmsServer? _cms = cms ? new CmsServer(configPath, config, options, registry, loggerFactory) : null;
    private readonly SemaphoreSlim _buildLock = new(1, 1);

    public async Task RunAsync()
    {
        await RebuildAsync();

        var listenPort = FindFreePort(port);
        if (listenPort != port)
            _log.LogWarning("Port {Requested} is in use; using {Actual} instead", port, listenPort);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{listenPort}");
        var app = builder.Build();

        app.UseWebSockets();
        app.Map("/__livereload", HandleWebSocket);
        app.Use(ServeContent);
        _cms?.Map(app);

        using var watcher = CreateWatcher();
        _log.LogInformation("Serving {Site} at http://localhost:{Port} (Ctrl+C to stop)", config.SiteName, listenPort);
        if (_cms is not null)
            _log.LogInformation("CMS at http://localhost:{Port}{Path}/ (turn off with --no-cms)", listenPort, CmsServer.BasePath);
        await app.RunAsync();
    }

    private static int FindFreePort(int start)
    {
        for (var candidate = start; candidate < start + 20; candidate++)
        {
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, candidate);
                listener.Start();
                listener.Stop();
                return candidate;
            }
            catch (System.Net.Sockets.SocketException) { /* in use, try next */ }
        }
        return start;
    }

    private async Task ServeContent(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/__livereload", StringComparison.Ordinal) || path.StartsWith(CmsServer.BasePath, StringComparison.Ordinal))
        {
            await next(context);
            return;
        }
        var relative = path.TrimStart('/');
        if (relative.Length == 0 || relative.EndsWith('/')) relative += "index.html";
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        var file = Path.Combine(config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(file)) file = Path.Combine(file, "index.html");

        if (!File.Exists(file))
        {
            var notFound = Path.Combine(config.AbsoluteSiteDir, "404.html");
            if (File.Exists(notFound))
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(InjectScripts(await File.ReadAllTextAsync(notFound), null));
                return;
            }
            await next(context);
            return;
        }

        if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/html";
            // The CMS preview loads pages untouched, without the live-reload and editor scripts.
            if (context.Request.Query.ContainsKey("__cms"))
            {
                await context.Response.WriteAsync(await File.ReadAllTextAsync(file));
                return;
            }
            var source = _cms?.SourceForUrl(Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? "");
            await context.Response.WriteAsync(InjectScripts(await File.ReadAllTextAsync(file), source));
        }
        else
        {
            context.Response.ContentType = ContentType(file);
            await context.Response.Body.WriteAsync(await File.ReadAllBytesAsync(file));
        }
    }

    private async Task HandleWebSocket(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        lock (_clientsLock) _clients.Add(socket);
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
                await socket.ReceiveAsync(buffer, CancellationToken.None);
        }
        catch (WebSocketException) { }
        finally { lock (_clientsLock) _clients.Remove(socket); }
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(config.ProjectRoot) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        var debounce = new System.Timers.Timer(250) { AutoReset = false };
        debounce.Elapsed += async (_, _) => { await RebuildAsync(); await NotifyClientsAsync(); };

        // Directories the build itself writes into. Changes under these must never trigger a
        // rebuild, otherwise each build's own output re-arms the watcher and the server rebuilds
        // in an endless loop. Resolved to absolute paths so the check is independent of the
        // configured site/docs directory names.
        var ignoredRoots = new[]
        {
            Path.GetFullPath(config.AbsoluteSiteDir),
            Path.GetFullPath(Path.Combine(config.ProjectRoot, ".cache")),
            Path.GetFullPath(Path.Combine(config.ProjectRoot, "artifacts")),
            Path.GetFullPath(Path.Combine(config.ProjectRoot, ".git")),
        };

        bool IsIgnored(string fullPath)
        {
            var normalized = Path.GetFullPath(fullPath);
            foreach (var root in ignoredRoots)
            {
                if (normalized.Equals(root, PathComparison) ||
                    normalized.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
                    return true;
            }
            return false;
        }

        void OnChange(object _, FileSystemEventArgs e)
        {
            if (IsIgnored(e.FullPath)) return;
            // The CMS checks a config edit by loading a scratch copy; that copy is not a change.
            if (Path.GetFileName(e.FullPath).StartsWith(".appsettings.cms-", StringComparison.Ordinal)) return;
            debounce.Stop();
            debounce.Start();
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += OnChange;
        return watcher;
    }

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private async Task RebuildAsync()
    {
        await _buildLock.WaitAsync();
        try
        {
            var freshConfig = JsonConfigLoader.Load(configPath);
            var engine = new BuildEngine(freshConfig, options, registry, loggerFactory);
            var site = await engine.BuildAsync();
            _cms?.OnBuilt(freshConfig, site);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rebuild failed");
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private async Task NotifyClientsAsync()
    {
        var message = Encoding.UTF8.GetBytes("reload");
        List<WebSocket> snapshot;
        lock (_clientsLock) snapshot = [.. _clients];
        foreach (var socket in snapshot.Where(s => s.State == WebSocketState.Open))
        {
            try { await socket.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch (WebSocketException) { }
        }
    }

    private string InjectScripts(string html, string? sourcePath)
    {
        // The in-page editor can hold back a reload while it has unsaved changes.
        const string script = """
            <script>
            (function(){var s=new WebSocket("ws://"+location.host+"/__livereload");
            function reload(){if(window.__netdocsCmsHoldReload&&window.__netdocsCmsHoldReload())return;location.reload();}
            s.onmessage=function(e){if(e.data==="reload")reload();};
            s.onclose=function(){setTimeout(reload,1000);};})();
            </script>
            """;
        var injected = _cms is null ? script : script + _cms.InlineEditorTag(sourcePath);
        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? html.Insert(idx, injected) : html + injected;
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".xml" => "application/xml",
        _ => "application/octet-stream",
    };
}
