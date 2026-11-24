using System.Security.Cryptography;
using System.Text;
using Netdocs.Abstractions;

namespace Netdocs.Core.Cms;

/// <summary>
/// The folders the CMS is allowed to read and write: the project root (where
/// <c>appsettings.json</c> lives) and the docs folder inside it. Every path that arrives from the
/// browser goes through <see cref="ResolveDocsPath"/> or <see cref="ResolveProjectPath"/>, which
/// refuse anything that would land outside those folders.
/// </summary>
public sealed class CmsWorkspace
{
    public CmsWorkspace(string configPath, SiteConfig config)
    {
        ConfigPath = Path.GetFullPath(configPath);
        Config = config;
        ProjectRoot = Path.GetFullPath(config.ProjectRoot);
        DocsDir = Path.GetFullPath(config.AbsoluteDocsDir);
    }

    /// <summary>Absolute path of the site's <c>appsettings.json</c>.</summary>
    public string ConfigPath { get; }

    /// <summary>The configuration as last loaded. Replaced by <see cref="Reload"/> after the CMS
    /// writes to <c>appsettings.json</c>.</summary>
    public SiteConfig Config { get; private set; }

    public string ProjectRoot { get; }
    public string DocsDir { get; }

    /// <summary>Re-reads <c>appsettings.json</c> so later CMS calls see the change.</summary>
    public void Reload() => Config = Configuration.JsonConfigLoader.Load(ConfigPath);

    public static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolves a docs-relative path (forward slashes) to an absolute path inside the docs folder.</summary>
    public string ResolveDocsPath(string relative) => Resolve(DocsDir, relative);

    /// <summary>Resolves a project-relative path to an absolute path inside the project folder.</summary>
    public string ResolveProjectPath(string relative) => Resolve(ProjectRoot, relative);

    /// <summary>Docs-relative path (forward slashes) of an absolute path.</summary>
    public string ToDocsRelative(string absolute) =>
        Path.GetRelativePath(DocsDir, absolute).Replace('\\', '/');

    public string ToProjectRelative(string absolute) =>
        Path.GetRelativePath(ProjectRoot, absolute).Replace('\\', '/');

    public static bool IsUnder(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return full.Equals(normalizedRoot, PathComparison)
            || full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new CmsException("A path is required.");
        if (relative.Contains('\0'))
            throw new CmsException("Invalid path.");
        var trimmed = relative.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnder(full, root))
            throw new CmsException($"'{relative}' is outside the site folder.");
        return full;
    }

    /// <summary>A short content hash the editor sends back on save, so a save that would overwrite
    /// a change made elsewhere since the page was opened is refused instead.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    /// <summary>Writes text without a BOM, creating the folder when needed.</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

/// <summary>A request the CMS refuses: bad input, a missing file, or a conflicting change.
/// The message is safe to show to the user.</summary>
public class CmsException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class CmsNotFoundException(string message) : CmsException(message, 404);

public sealed class CmsConflictException(string message) : CmsException(message, 409);
