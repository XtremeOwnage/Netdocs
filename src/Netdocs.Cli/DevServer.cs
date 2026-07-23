using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core;
using Netdocs.Core.Configuration;
using Netdocs.Core.Plugins;

namespace Netdocs.Cli;

/// <summary>Kestrel static host with FileSystemWatcher-driven rebuilds and WebSocket live reload.</summary>
public sealed class DevServer(
    string configPath,
    SiteConfig config,
    BuildOptions options,
    PluginRegistry registry,
    ILoggerFactory loggerFactory,
    int port)
{
    private readonly List<WebSocket> _clients = [];
    private readonly Lock _clientsLock = new();
    private readonly ILogger _log = loggerFactory.CreateLogger("serve");

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

        using var watcher = CreateWatcher();
        _log.LogInformation("Serving {Site} at http://localhost:{Port} (Ctrl+C to stop)", config.SiteName, listenPort);
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
        if (path.StartsWith("/__livereload", StringComparison.Ordinal)) { await next(context); return; }
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
                await context.Response.WriteAsync(InjectReloadScript(await File.ReadAllTextAsync(notFound)));
                return;
            }
            await next(context);
            return;
        }

        if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(InjectReloadScript(await File.ReadAllTextAsync(file)));
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
        try
        {
            var freshConfig = JsonConfigLoader.Load(configPath);
            var engine = new BuildEngine(freshConfig, options, registry, loggerFactory);
            await engine.BuildAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rebuild failed");
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

    private static string InjectReloadScript(string html)
    {
        const string script = """
            <script>
            (function(){var s=new WebSocket("ws://"+location.host+"/__livereload");
            s.onmessage=function(e){if(e.data==="reload")location.reload();};
            s.onclose=function(){setTimeout(function(){location.reload();},1000);};})();
            </script>
            """;
        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? html.Insert(idx, script) : html + script;
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
