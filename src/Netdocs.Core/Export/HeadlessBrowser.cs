using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Netdocs.Core.Export;

/// <summary>
/// Minimal driver for a locally installed Chrome, Chromium or Edge running headless, spoken to
/// over the Chrome DevTools Protocol. It loads a page and prints it to PDF or captures a
/// full-page PNG/WebP screenshot, so exports render with the same engine the site targets
/// without bundling a browser into Netdocs.
/// </summary>
public sealed class HeadlessBrowser : IAsyncDisposable
{
    /// <summary>Largest image edge Chrome and the WebP format can encode.</summary>
    internal const int MaxImageDimension = 16383;

    private readonly Process _process;
    private readonly string _profileDir;
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _receiveCts = new();
    private readonly Task _receiveLoop;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private string _sessionId = "";
    private int _nextId;

    private HeadlessBrowser(Process process, string profileDir, ClientWebSocket socket)
    {
        _process = process;
        _profileDir = profileDir;
        _socket = socket;
        _receiveLoop = ReceiveLoopAsync();
    }

    /// <summary>Environment variable that names the browser executable to use.</summary>
    public const string BrowserEnvironmentVariable = "NETDOCS_BROWSER";

    /// <summary>Finds a Chromium-family browser: an explicit path, then <c>NETDOCS_BROWSER</c>,
    /// then the usual install locations and PATH entries for each platform.</summary>
    public static string? Find(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : ResolveOnPath(explicitPath);

        if (Environment.GetEnvironmentVariable(BrowserEnvironmentVariable) is { Length: > 0 } fromEnv)
            return File.Exists(fromEnv) ? Path.GetFullPath(fromEnv) : ResolveOnPath(fromEnv);

        foreach (var candidate in Candidates())
        {
            var found = Path.IsPathRooted(candidate) ? (File.Exists(candidate) ? candidate : null) : ResolveOnPath(candidate);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            };
            foreach (var root in roots.Where(r => r.Length > 0))
            {
                yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(root, "Chromium", "Application", "chrome.exe");
                yield return Path.Combine(root, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
            }
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
            yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
            yield return "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser";
        }

        foreach (var name in new[] { "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge", "microsoft-edge-stable", "brave-browser" })
            yield return name;

        // Playwright-managed Chromium, when present (common on CI images and dev containers).
        if (Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") is { Length: > 0 } pw && Directory.Exists(pw))
        {
            foreach (var dir in Directory.EnumerateDirectories(pw, "chromium-*").OrderDescending())
            {
                yield return Path.Combine(dir, "chrome-linux", "chrome");
                yield return Path.Combine(dir, "chrome-linux64", "chrome");
            }
        }
    }

    private static string? ResolveOnPath(string name)
    {
        if (Path.IsPathRooted(name)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows() ? new[] { "", ".exe" } : [""];
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in extensions)
            {
                var full = Path.Combine(dir, name + ext);
                if (File.Exists(full)) return full;
            }
        return null;
    }

    /// <summary>Starts the browser headless and attaches to a fresh page.</summary>
    public static async Task<HeadlessBrowser> LaunchAsync(string executable, TimeSpan timeout, CancellationToken ct = default)
    {
        var profileDir = Path.Combine(Path.GetTempPath(), "netdocs-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDir);

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[]
        {
            "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
            "--disable-extensions", "--disable-background-networking", "--disable-sync",
            "--hide-scrollbars", "--mute-audio", "--allow-file-access-from-files",
            "--remote-debugging-port=0", $"--user-data-dir={profileDir}", "about:blank",
        })
            psi.ArgumentList.Add(arg);
        // Chrome refuses to start its sandbox as root (typical in containers and CI runners).
        if (OperatingSystem.IsLinux() && Environment.UserName == "root")
            psi.ArgumentList.Add("--no-sandbox");

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {executable}.");
        // Drain the pipes so a chatty browser never blocks on a full buffer.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            // Chrome writes "<port>\n<browser ws path>" here once DevTools is listening.
            var portFile = Path.Combine(profileDir, "DevToolsActivePort");
            string[] lines;
            while (true)
            {
                if (process.HasExited)
                    throw new InvalidOperationException($"{Path.GetFileName(executable)} exited during startup (code {process.ExitCode}).");
                if (File.Exists(portFile))
                {
                    lines = (await ReadSharedAsync(portFile, timeoutCts.Token)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (lines.Length >= 2) break;
                }
                await Task.Delay(50, timeoutCts.Token);
            }

            var socket = new ClientWebSocket();
            socket.Options.Proxy = null;
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{lines[0]}{lines[1]}"), timeoutCts.Token);

            var browser = new HeadlessBrowser(process, profileDir, socket);
            try
            {
                var target = await browser.SendAsync("Target.createTarget", new JsonObject { ["url"] = "about:blank" }, session: false, timeoutCts.Token);
                var attached = await browser.SendAsync("Target.attachToTarget",
                    new JsonObject { ["targetId"] = target["targetId"]!.GetValue<string>(), ["flatten"] = true }, session: false, timeoutCts.Token);
                browser._sessionId = attached["sessionId"]!.GetValue<string>();
                return browser;
            }
            catch
            {
                await browser.DisposeAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            Kill(process);
            TryDeleteDirectory(profileDir);
            if (ex is OperationCanceledException && !ct.IsCancellationRequested)
                throw new TimeoutException($"{Path.GetFileName(executable)} did not start within {timeout.TotalSeconds:0} seconds.", ex);
            throw;
        }
    }

    private static async Task<string> ReadSharedAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (IOException)
        {
            return "";
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open && !_receiveCts.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, _receiveCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                var node = JsonNode.Parse(message.GetBuffer().AsSpan(0, (int)message.Length)) as JsonObject;
                message.SetLength(0);
                if (node?["id"]?.GetValue<int>() is not { } id || !_pending.TryRemove(id, out var tcs)) continue;

                if (node["error"] is JsonObject error)
                    tcs.TrySetException(new InvalidOperationException($"Browser error: {error["message"]?.GetValue<string>()}"));
                else
                    tcs.TrySetResult(node["result"] as JsonObject ?? []);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            foreach (var (_, tcs) in _pending)
                tcs.TrySetException(new InvalidOperationException("The browser connection closed."));
        }
    }

    private async Task<JsonObject> SendAsync(string method, JsonObject? parameters, bool session, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var command = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? [] };
        if (session) command["sessionId"] = _sessionId;
        var bytes = Encoding.UTF8.GetBytes(command.ToJsonString());

        await _sendLock.WaitAsync(ct);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }

        await using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    private Task<JsonObject> SendAsync(string method, JsonObject? parameters, CancellationToken ct) =>
        SendAsync(method, parameters, session: true, ct);

    /// <summary>Opens <paramref name="url"/> at the given viewport width and waits until the
    /// page, its fonts and images, and any client-side diagrams or math have finished.</summary>
    public async Task LoadAsync(Uri url, int width, double scale, TimeSpan timeout, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        await SendAsync("Page.enable", null, token);
        await SendAsync("Runtime.enable", null, token);
        await SetViewportAsync(width, 800, scale, token);
        // Render with screen styles for every format, so a PDF looks like the page in a browser
        // (Material's print stylesheet would otherwise drop the dark scheme and restyle code).
        await SendAsync("Emulation.setEmulatedMedia", new JsonObject { ["media"] = "screen" }, token);

        var nav = await SendAsync("Page.navigate", new JsonObject { ["url"] = url.AbsoluteUri }, token);
        if (nav["errorText"]?.GetValue<string>() is { Length: > 0 } navError)
            throw new InvalidOperationException($"Could not load {url}: {navError}");

        // Wait for the load event (stylesheets, sync scripts, images), then for fonts, lazy
        // images, mermaid diagrams and MathJax. Each wait is bounded, so a remote resource that
        // never arrives delays the export instead of failing it.
        const string readyScript = """
            (async () => {
              const loaded = Date.now() + 30000;
              while (document.readyState !== "complete" && Date.now() < loaded) await new Promise(r => setTimeout(r, 50));
              await document.fonts.ready;
              const deadline = Date.now() + 15000;
              while (Date.now() < deadline) {
                const diagrams = document.querySelectorAll(".mermaid:not([data-processed])").length;
                const images = [...document.images].filter(i => !i.complete).length;
                if (!diagrams && !images) break;
                await new Promise(r => setTimeout(r, 100));
              }
              if (window.MathJax && MathJax.startup && MathJax.startup.promise) {
                await Promise.race([MathJax.startup.promise, new Promise(r => setTimeout(r, 10000))]);
              }
              await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
              return true;
            })()
            """;
        try
        {
            await EvaluateAsync(readyScript, token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The page did not finish loading within {timeout.TotalSeconds:0} seconds.", ex);
        }
    }

    private async Task<JsonNode?> EvaluateAsync(string expression, CancellationToken ct)
    {
        var result = await SendAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["awaitPromise"] = true,
            ["returnByValue"] = true,
        }, ct);
        if (result["exceptionDetails"] is JsonObject details)
            throw new InvalidOperationException($"Page script failed: {details["text"]?.GetValue<string>()}");
        return result["result"]?["value"];
    }

    private Task SetViewportAsync(int width, int height, double scale, CancellationToken ct) =>
        SendAsync("Emulation.setDeviceMetricsOverride", new JsonObject
        {
            ["width"] = width,
            ["height"] = height,
            ["deviceScaleFactor"] = scale,
            ["mobile"] = false,
        }, ct);

    /// <summary>Prints the loaded page to PDF with backgrounds (so the dark scheme survives).
    /// The page has no paper margins: the export template turns its own per-page padding on via
    /// the <c>nd-export--pdf</c> class, so the scheme background covers the whole sheet.</summary>
    public async Task<byte[]> PrintToPdfAsync(double paperWidthInches, double paperHeightInches, CancellationToken ct = default)
    {
        await EvaluateAsync("document.documentElement.classList.add('nd-export--pdf')", ct);
        const double margin = 0;
        var result = await SendAsync("Page.printToPDF", new JsonObject
        {
            ["printBackground"] = true,
            ["paperWidth"] = paperWidthInches,
            ["paperHeight"] = paperHeightInches,
            ["marginTop"] = margin,
            ["marginBottom"] = margin,
            ["marginLeft"] = margin,
            ["marginRight"] = margin,
            ["preferCSSPageSize"] = true,
        }, ct);
        await EvaluateAsync("document.documentElement.classList.remove('nd-export--pdf')", ct);
        return Convert.FromBase64String(result["data"]!.GetValue<string>());
    }

    /// <summary>Captures the whole page (not just the viewport) as PNG or WebP. Pages taller
    /// than the format allows are scaled down to fit; <paramref name="downscaled"/> reports it.</summary>
    public async Task<(byte[] Data, bool Downscaled)> ScreenshotAsync(ExportFormat format, int width, double scale, CancellationToken ct = default)
    {
        var metrics = await SendAsync("Page.getLayoutMetrics", null, ct);
        var size = metrics["cssContentSize"] as JsonObject ?? metrics["contentSize"] as JsonObject;
        var height = Math.Max(1, (int)Math.Ceiling(size?["height"]?.GetValue<double>() ?? 800));

        // Size the viewport to the full document so nothing outside it is clipped or lazily skipped.
        await SetViewportAsync(width, height, scale, ct);

        var clipScale = Math.Min(1.0, Math.Min(MaxImageDimension / (height * scale), MaxImageDimension / (width * scale)));
        var parameters = new JsonObject
        {
            ["format"] = format == ExportFormat.Webp ? "webp" : "png",
            ["captureBeyondViewport"] = true,
            ["clip"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = width, ["height"] = height, ["scale"] = clipScale },
        };
        if (format == ExportFormat.Webp) parameters["quality"] = 90;

        var result = await SendAsync("Page.captureScreenshot", parameters, ct);
        return (Convert.FromBase64String(result["data"]!.GetValue<string>()), clipScale < 1.0);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await SendAsync("Browser.close", null, session: false, cts.Token);
        }
        catch
        {
            // Best effort: the process is killed below regardless.
        }

        await _receiveCts.CancelAsync();
        try { await _receiveLoop; } catch { /* already reported to pending callers */ }
        _socket.Dispose();
        _receiveCts.Dispose();
        _sendLock.Dispose();

        if (!_process.WaitForExit(5000)) Kill(_process);
        _process.Dispose();
        TryDeleteDirectory(_profileDir);
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    private static void TryDeleteDirectory(string dir)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}
