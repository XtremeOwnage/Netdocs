using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;
using Netdocs.Core.Configuration;
using Netdocs.Core.Content;

namespace Netdocs.Plugins;

/// <summary>
/// Adds a version selector to the header, driven either by an explicit list of versions or by the
/// repository's release tags. Emits a mike-compatible <c>versions.json</c> at the site root so a
/// multi-version deployment (each version served from its own subdirectory) can be navigated from
/// a dropdown. This is the netdocs equivalent of mkdocs-material's versioning/mike integration.
/// </summary>
public sealed partial class VersioningPlugin : IPlugin, IBuildHook
{
    private readonly List<VersionInfo> _configured = [];
    private string _provider = "static";
    private string? _current;
    private string _tagPattern = @"^v?\d+(\.\d+)*$";
    private string _urlTemplate = "../{version}/";
    private string _label = "Version";
    private string? _projectRoot;

    public string Name => "versioning";

    public void Configure(IPluginContext ctx)
    {
        var o = ctx.PluginOptions;
        _provider = o.Get("provider").AsString() ?? "static";
        _current = o.Get("current").AsString() ?? o.Get("default").AsString();
        if (o.Get("tag_pattern").AsString() is { Length: > 0 } tp) _tagPattern = tp;
        if (o.Get("url_template").AsString() is { Length: > 0 } ut) _urlTemplate = ut;
        if (o.Get("label").AsString() is { Length: > 0 } lb) _label = lb;
        _projectRoot = ctx.Config.ProjectRoot;

        foreach (var item in o.Get("versions").AsList())
        {
            var m = item.AsMap();
            var version = m.Get("version").AsString();
            if (string.IsNullOrWhiteSpace(version)) continue;
            _configured.Add(new VersionInfo(
                version,
                m.Get("title").AsString() ?? version,
                m.Get("url").AsString(),
                m.Get("aliases").AsList().Select(a => a.AsString()).Where(a => !string.IsNullOrEmpty(a)).Cast<string>().ToList()));
        }
    }

    public Task OnBuildStartAsync(SiteContext site, CancellationToken ct)
    {
        var versions = new List<VersionInfo>(_configured);
        if (string.Equals(_provider, "git-tags", StringComparison.OrdinalIgnoreCase))
            versions.AddRange(ReadGitTagVersions(site));

        // De-duplicate by version id, keeping the first (config wins over discovered tags).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        versions = versions.Where(v => seen.Add(v.Version)).ToList();
        if (versions.Count == 0) return Task.CompletedTask;

        var current = _current
            ?? versions.FirstOrDefault(v => v.Aliases.Contains("latest", StringComparer.OrdinalIgnoreCase))?.Version
            ?? versions[0].Version;

        var model = versions.Select(v => (object?)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["version"] = v.Version,
            ["title"] = v.Title,
            ["url"] = v.Url ?? _urlTemplate.Replace("{version}", v.Version),
            ["aliases"] = v.Aliases,
            ["current"] = string.Equals(v.Version, current, StringComparison.OrdinalIgnoreCase),
        }).ToList();

        site.State["versions"] = model;
        site.State["version_label"] = _label;
        site.State["current_version"] = model.OfType<Dictionary<string, object?>>()
            .FirstOrDefault(m => (bool)(m["current"] ?? false)) ?? model[0];
        return Task.CompletedTask;
    }

    public async Task OnBuildCompleteAsync(SiteContext site, CancellationToken ct)
    {
        if (site.State.GetValueOrDefault("versions") is not List<object?> versions || versions.Count == 0)
            return;

        // mike-compatible versions.json: [{ version, title, aliases }].
        var entries = versions.OfType<Dictionary<string, object?>>().Select(m => new
        {
            version = m["version"]?.ToString() ?? "",
            title = m["title"]?.ToString() ?? "",
            aliases = (m["aliases"] as IEnumerable<string>)?.ToArray() ?? [],
        });
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        await OutputWriter.WriteTextIfChangedAsync(site, Path.Combine(site.Config.AbsoluteSiteDir, "versions.json"), json, ct);
    }

    /// <summary>Reads release tags from the repository (newest version first) and maps each to a
    /// <see cref="VersionInfo"/>. Tags are filtered by <c>tag_pattern</c> and ordered by semantic
    /// version descending. Returns nothing if git is unavailable or the root is not a repo.</summary>
    private IEnumerable<VersionInfo> ReadGitTagVersions(SiteContext site)
    {
        var root = _projectRoot ?? site.Config.ProjectRoot;
        List<string> tags;
        try
        {
            tags = RunGit(root, "tag --list");
        }
        catch (Exception ex)
        {
            site.LoggerFactory.CreateLogger("versioning").LogWarning("Could not read git tags: {Message}", ex.Message);
            yield break;
        }

        var pattern = new Regex(_tagPattern, RegexOptions.CultureInvariant);
        var matched = tags.Where(t => pattern.IsMatch(t))
            .OrderByDescending(SortKey)
            .ToList();

        foreach (var tag in matched)
            yield return new VersionInfo(tag, tag, null, []);
    }

    /// <summary>Sort key that orders version-like tags (with an optional leading 'v') numerically,
    /// so 1.10.0 sorts after 1.9.0. Numeric segments are zero-padded; non-numeric segments fall
    /// back to ordinal order.</summary>
    private static string SortKey(string tag)
    {
        var body = tag.TrimStart('v', 'V');
        var sb = new StringBuilder();
        foreach (var part in body.Split('.', '-'))
        {
            sb.Append(int.TryParse(part, out var n) ? n.ToString("D8") : part.PadRight(8));
            sb.Append('.');
        }
        return sb.ToString();
    }

    private static List<string> RunGit(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("git not found");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(10_000);
        if (proc.ExitCode != 0) throw new InvalidOperationException(proc.StandardError.ReadToEnd().Trim());
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private sealed record VersionInfo(string Version, string Title, string? Url, List<string> Aliases);
}
