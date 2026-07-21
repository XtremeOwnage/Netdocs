using System.Text.RegularExpressions;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Implements pymdownx.b64-style image embedding: rewrites local image references
/// (Markdown <c>![alt](path)</c> and inline <c>&lt;img src="path"&gt;</c>) into
/// self-contained <c>data:</c> URIs so a page carries its images inline.
/// </summary>
/// <remarks>
/// Toggle site-wide with the <c>default</c> plugin option, and override per page via
/// the <c>embed_images</c> front-matter key. Remote (http/https), protocol-relative,
/// and existing <c>data:</c> sources are left untouched.
/// </remarks>
public sealed partial class Base64EmbedPlugin : IPlugin, IMarkdownPreprocessor
{
    private readonly List<string> _basePaths = [];
    private string _docsDir = "";
    private bool _default;

    public string Name => "b64";

    // After snippets (10) and macros so included/generated image references embed too.
    public int Order => 60;

    public void Configure(IPluginContext ctx)
    {
        _docsDir = ctx.Config.AbsoluteDocsDir;

        _default = ctx.PluginOptions.TryGetValue("default", out var d) && ToBool(d) == true;

        var basePath = ctx.PluginOptions.TryGetValue("base_path", out var bp) ? bp : null;
        foreach (var p in AsStringList(basePath))
            _basePaths.Add(Path.GetFullPath(Path.Combine(ctx.Config.ProjectRoot, p)));
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (!ShouldEmbed(page)) return Task.FromResult(markdown);

        var sourceDir = string.IsNullOrEmpty(page.SourcePath)
            ? _docsDir
            : Path.GetDirectoryName(page.SourcePath) ?? _docsDir;

        var result = MarkdownImageRegex().Replace(markdown, m =>
        {
            var data = TryEmbed(m.Groups["src"].Value, sourceDir);
            return data is null
                ? m.Value
                : $"![{m.Groups["alt"].Value}]({data}{m.Groups["rest"].Value})";
        });

        result = HtmlImageRegex().Replace(result, m =>
        {
            var data = TryEmbed(m.Groups["src"].Value, sourceDir);
            return data is null ? m.Value : m.Value.Replace(m.Groups["src"].Value, data);
        });

        return Task.FromResult(result);
    }

    private bool ShouldEmbed(Page page)
    {
        if (page.FrontMatter.TryGetValue("embed_images", out var v) && ToBool(v) is { } b)
            return b;
        return _default;
    }

    private string? TryEmbed(string src, string sourceDir)
    {
        src = src.Trim();
        if (src.Length == 0) return null;
        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return null;
        if (src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        if (src.StartsWith("//")) return null;

        var mime = MimeFor(src);
        if (mime is null) return null; // not a recognized image extension

        var resolved = Resolve(src, sourceDir);
        if (resolved is null) return null;

        try
        {
            var bytes = File.ReadAllBytes(resolved);
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string? Resolve(string src, string sourceDir)
    {
        // Strip any query/fragment (e.g. logo.png?v=2).
        var clean = src;
        var cut = clean.IndexOfAny(['?', '#']);
        if (cut >= 0) clean = clean[..cut];
        clean = Uri.UnescapeDataString(clean);

        if (Path.IsPathRooted(clean) && File.Exists(clean)) return clean;

        // Docs-root-absolute reference ("/img/x.png").
        if (clean.StartsWith('/'))
        {
            var rooted = Path.GetFullPath(Path.Combine(_docsDir, clean.TrimStart('/')));
            if (File.Exists(rooted)) return rooted;
        }

        var candidates = new List<string> { Path.Combine(sourceDir, clean) };
        candidates.AddRange(_basePaths.Select(b => Path.Combine(b, clean)));
        candidates.Add(Path.Combine(_docsDir, clean));

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string? MimeFor(string src)
    {
        var path = src;
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            _ => null,
        };
    }

    private static bool? ToBool(object? node) => node switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        string s => s is "1" or "yes" or "on" ? true : s is "0" or "no" or "off" ? false : null,
        _ => null,
    };

    private static IEnumerable<string> AsStringList(object? node) => node switch
    {
        string s => [s],
        IEnumerable<object?> list => list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0),
        _ => [],
    };

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<src>[^)\s]+)(?<rest>(?:\s+""[^""]*"")?)\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*[""'](?<src>[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageRegex();
}
