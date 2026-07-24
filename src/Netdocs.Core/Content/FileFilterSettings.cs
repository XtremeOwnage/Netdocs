using Netdocs.Core.Configuration;

namespace Netdocs.Core.Content;

/// <summary>
/// Reads <c>.file-filter.yml</c> (mkdocs-file-filter compatible) from the project root and
/// resolves whether content filtering is active for the current build mode.
///
/// The whole filter — front-matter label pruning AND <c>.mkdocsignore</c> path exclusion — is
/// gated behind <c>enabled</c> / <c>enabled_on_serve</c> (both support <c>!ENV</c> lookups, which
/// <see cref="YamlTree"/> resolves at parse time). This lets a development or <c>serve</c> build
/// keep draft / dev-only content that a production build hides, matching the upstream plugin.
///
/// When no <c>.file-filter.yml</c> exists, a <c>.mkdocsignore</c> is treated as an always-on
/// ignore file (like <c>.gitignore</c>) so simple projects that never opted into the filter keep
/// their previous behaviour.
/// </summary>
public sealed class FileFilterSettings
{
    public bool Exists { get; private init; }
    public bool Enabled { get; private init; } = true;
    public bool EnabledOnServe { get; private init; } = true;
    public bool Mkdocsignore { get; private init; } = true;
    public string MkdocsignoreFile { get; private init; } = ".mkdocsignore";
    public string MetadataProperty { get; private init; } = "labels";
    public IReadOnlySet<string> ExcludeTags { get; private init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> IncludeTags { get; private init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static FileFilterSettings Load(string projectRoot)
    {
        var path = Path.Combine(projectRoot, ".file-filter.yml");
        if (!File.Exists(path))
            return new FileFilterSettings { Exists = false };

        var root = YamlTree.Parse(File.ReadAllText(path)).AsMap();

        var excludeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in root.Get("exclude_tag").AsList())
            if (t.AsString() is { Length: > 0 } s) excludeTags.Add(s);

        var includeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in root.Get("include_tag").AsList())
            if (t.AsString() is { Length: > 0 } s) includeTags.Add(s);

        var file = root.Get("mkdocsignore_file").AsString();
        var prop = root.Get("metadata_property").AsString();

        return new FileFilterSettings
        {
            Exists = true,
            Enabled = root.Get("enabled").AsBool(true),
            EnabledOnServe = root.TryGetValue("enabled_on_serve", out var eos) ? eos.AsBool(true) : true,
            Mkdocsignore = root.Get("mkdocsignore").AsBool(true),
            MkdocsignoreFile = string.IsNullOrWhiteSpace(file) ? ".mkdocsignore" : file!,
            MetadataProperty = prop is { Length: > 0 } ? prop : "labels",
            ExcludeTags = excludeTags,
            IncludeTags = includeTags,
        };
    }

    /// <summary>Whether the filter runs at all for this build (the <c>enabled</c> gate).</summary>
    public bool IsActive(bool isServe) =>
        Exists && (isServe ? Enabled && EnabledOnServe : Enabled);

    /// <summary>Whether label-based pruning runs (the gate AND at least one <c>exclude_tag</c>).</summary>
    public bool AppliesLabelFilter(bool isServe) => IsActive(isServe) && ExcludeTags.Count > 0;

    /// <summary>
    /// Whether <c>.mkdocsignore</c> path rules should be applied for this build. With no filter
    /// config the ignore file is always honoured; otherwise it follows the <c>enabled</c> gate so
    /// dev-only sections listed there reappear on non-production builds.
    /// </summary>
    public bool AppliesMkdocsIgnore(bool isServe) => !Exists || (Mkdocsignore && IsActive(isServe));
}
