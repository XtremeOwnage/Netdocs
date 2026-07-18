using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>Emits client-side redirect pages from a <c>redirect_maps</c> option (source path -&gt; target URL).</summary>
public sealed class RedirectsPlugin : IPlugin, IBuildHook
{
    private IReadOnlyDictionary<string, object?> _maps = new Dictionary<string, object?>();

    public string Name => "redirects";

    public void Configure(IPluginContext ctx)
    {
        if (ctx.PluginOptions.TryGetValue("redirect_maps", out var m) && m is IReadOnlyDictionary<string, object?> map)
            _maps = map;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        foreach (var (source, targetObj) in _maps)
        {
            var target = targetObj?.ToString();
            if (string.IsNullOrWhiteSpace(target)) continue;

            var relative = source.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? source[..^3] + "/index.html"
                : source.TrimEnd('/') + "/index.html";
            var dest = Path.Combine(site.Config.AbsoluteSiteDir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            var escaped = System.Net.WebUtility.HtmlEncode(target);
            var html = $"""
                <!doctype html>
                <html lang="en">
                <head>
                <meta charset="utf-8">
                <title>Redirecting…</title>
                <link rel="canonical" href="{escaped}">
                <meta http-equiv="refresh" content="0; url={escaped}">
                </head>
                <body>Redirecting to <a href="{escaped}">{escaped}</a>…</body>
                </html>
                """;
            await File.WriteAllTextAsync(dest, html, ct);
        }
    }
}
