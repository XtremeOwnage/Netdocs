using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Netdocs.Core.Configuration;
using Netdocs.Core.Content;

namespace Netdocs.Core.Cms;

/// <summary>A plugin entry in <c>appsettings.json</c>.</summary>
public sealed record PluginEntry(string Name, bool Enabled, JsonObject Options, int Index);

/// <summary>One abbreviation: a term and what it stands for.</summary>
public sealed record Abbreviation(string Term, string Definition);

/// <summary>An abbreviations file and the terms it defines.</summary>
public sealed record AbbreviationFile(string File, IReadOnlyList<string> Files, bool Exists, IReadOnlyList<Abbreviation> Entries);

/// <summary>The file-filter plugin's settings files.</summary>
public sealed record FileFilterConfig(bool Exists, string Yaml, JsonObject Fields, string IgnoreFile, string IgnoreText);

/// <summary>
/// Plugin management for the CMS. Plugins are entries in the <c>plugins</c> list of
/// <c>appsettings.json</c>; a disabled plugin keeps its entry with <c>"enabled": false</c> so its
/// options survive. Each entry is edited on its own, so the rest of the file keeps its layout.
/// Some plugins keep settings in their own files (abbreviations, file-filter, blog authors),
/// which are edited here too.
/// </summary>
public sealed partial class PluginService(CmsWorkspace workspace)
{
    private static readonly string[] PluginsPath = ["Netdocs", "plugins"];
    private static readonly JsonSerializerOptions YamlString = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public IReadOnlyList<PluginEntry> List() => Entries(Config());

    /// <summary>Turns a plugin on or off and replaces its options. A plugin that is not listed is added.</summary>
    public IReadOnlyList<PluginEntry> Save(string name, bool enabled, JsonObject? options)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new CmsException("A plugin name is required.");
        var json = Config();
        var existing = Entries(json).FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var current = existing is null ? null : RawEntry(json, existing.Index);

        var entry = new JsonObject { ["name"] = existing?.Name ?? name.Trim() };
        if (current is not null)
            foreach (var (key, value) in current)
                if (!key.Equals("name", StringComparison.OrdinalIgnoreCase) && !key.Equals("enabled", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("options", StringComparison.OrdinalIgnoreCase))
                    entry[key] = value?.DeepClone();
        if (!enabled) entry["enabled"] = false;
        if (options is { Count: > 0 }) entry["options"] = options.DeepClone();

        if (current is not null && JsonNode.DeepEquals(current, entry)) return Entries(json);
        json = JsonSpanEditor.SetArrayItem(json, existing?.Index ?? Entries(json).Count, entry, PluginsPath);
        return Write(json);
    }

    /// <summary>Removes a plugin's entry, options and all.</summary>
    public IReadOnlyList<PluginEntry> Remove(string name)
    {
        var json = Config();
        var existing = Entries(json).FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new CmsNotFoundException($"Plugin '{name}' is not in the config.");
        return Write(JsonSpanEditor.SetArrayItem(json, existing.Index, null, PluginsPath));
    }

    // ---- abbreviations ------------------------------------------------------------------

    public IReadOnlyList<string> AbbreviationFiles()
    {
        var options = Options("abbreviations");
        var files = options?["files"] is JsonArray list
            ? list.Select(x => x?.ToString() ?? "").Where(s => s.Trim().Length > 0).ToList()
            : [];
        if (files.Count == 0) files.Add("_include/abbv.md");
        return files;
    }

    public AbbreviationFile ReadAbbreviations(string? file)
    {
        var files = AbbreviationFiles();
        var name = string.IsNullOrWhiteSpace(file) ? files[0] : file;
        var path = workspace.ResolveDocsPath(name);
        var exists = File.Exists(path);
        var entries = new List<Abbreviation>();
        if (exists)
            foreach (var line in File.ReadAllLines(path))
                if (AbbreviationLine().Match(line) is { Success: true } m)
                    entries.Add(new Abbreviation(m.Groups[1].Value, m.Groups[2].Value.Trim()));
        return new AbbreviationFile(name, files, exists, entries);
    }

    /// <summary>Rewrites the abbreviation lines of a file. Other lines (comments, headings) stay
    /// where they are; the definitions go where the first one was.</summary>
    public AbbreviationFile SaveAbbreviations(string? file, IReadOnlyList<Abbreviation> entries)
    {
        var name = string.IsNullOrWhiteSpace(file) ? AbbreviationFiles()[0] : file;
        var path = workspace.ResolveDocsPath(name);
        var clean = new List<Abbreviation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            var term = (e.Term ?? "").Trim();
            var definition = (e.Definition ?? "").Trim().ReplaceLineEndings(" ");
            if (term.Length == 0) continue;
            if (term.Contains(']', StringComparison.Ordinal)) throw new CmsException($"The term '{term}' cannot contain ']'.");
            if (!seen.Add(term)) throw new CmsException($"The term '{term}' is listed twice.");
            clean.Add(new Abbreviation(term, definition));
        }

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var newline = File.Exists(path) && File.ReadAllText(path).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var output = new List<string>();
        var placed = false;
        foreach (var line in lines)
        {
            if (!AbbreviationLine().IsMatch(line)) { output.Add(line); continue; }
            if (placed) continue;
            output.AddRange(clean.Select(Format));
            placed = true;
        }
        if (!placed) output.AddRange(clean.Select(Format));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CmsWorkspace.WriteText(path, string.Join(newline, output) + newline);
        return ReadAbbreviations(name);

        static string Format(Abbreviation a) => $"*[{a.Term}]: {a.Definition}";
    }

    [GeneratedRegex(@"^\*\[([^\]]+)\]:\s?(.*)$")]
    private static partial Regex AbbreviationLine();

    // ---- file-filter ----------------------------------------------------------------------

    public FileFilterConfig ReadFileFilter()
    {
        var path = Path.Combine(workspace.ProjectRoot, ".file-filter.yml");
        var exists = File.Exists(path);
        var settings = FileFilterSettings.Load(workspace.ProjectRoot);
        var fields = new JsonObject
        {
            ["enabled"] = settings.Enabled,
            ["enabled_on_serve"] = settings.EnabledOnServe,
            ["mkdocsignore"] = settings.Mkdocsignore,
            ["mkdocsignore_file"] = settings.MkdocsignoreFile,
            ["metadata_property"] = settings.MetadataProperty,
            ["exclude_tag"] = new JsonArray([.. settings.ExcludeTags.Select(t => (JsonNode)t)]),
            ["include_tag"] = new JsonArray([.. settings.IncludeTags.Select(t => (JsonNode)t)]),
        };
        var ignorePath = workspace.ResolveProjectPath(settings.MkdocsignoreFile);
        return new FileFilterConfig(exists, exists ? File.ReadAllText(path) : "", fields, settings.MkdocsignoreFile,
            File.Exists(ignorePath) ? File.ReadAllText(ignorePath) : "");
    }

    /// <summary>Saves <c>.file-filter.yml</c>, from raw text or from form fields, and the ignore file.</summary>
    public FileFilterConfig SaveFileFilter(string? yaml, JsonObject? fields, string? ignoreText)
    {
        var path = Path.Combine(workspace.ProjectRoot, ".file-filter.yml");
        if (yaml is null && fields is not null) yaml = FileFilterYaml(fields);
        if (yaml is not null)
        {
            try { YamlTree.Parse(yaml); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                throw new CmsException("The file-filter settings are not valid YAML: " + ex.Message);
            }
            CmsWorkspace.WriteText(path, yaml);
        }
        if (ignoreText is not null)
        {
            var ignorePath = workspace.ResolveProjectPath(FileFilterSettings.Load(workspace.ProjectRoot).MkdocsignoreFile);
            // An empty ignore file is only written when one already exists.
            if (ignoreText.Trim().Length > 0 || File.Exists(ignorePath)) CmsWorkspace.WriteText(ignorePath, ignoreText);
        }
        return ReadFileFilter();
    }

    private static string FileFilterYaml(JsonObject f)
    {
        var sb = new StringBuilder();
        sb.Append("enabled: ").Append(Bool(f["enabled"], true)).Append('\n');
        sb.Append("enabled_on_serve: ").Append(Bool(f["enabled_on_serve"], true)).Append('\n');
        sb.Append("mkdocsignore: ").Append(Bool(f["mkdocsignore"], true)).Append('\n');
        if (Text(f["mkdocsignore_file"]) is { } file && file != ".mkdocsignore") sb.Append("mkdocsignore_file: ").Append(Quote(file)).Append('\n');
        if (Text(f["metadata_property"]) is { } prop && prop != "labels") sb.Append("metadata_property: ").Append(Quote(prop)).Append('\n');
        foreach (var key in new[] { "exclude_tag", "include_tag" })
        {
            var tags = f[key] is JsonArray a ? a.Select(x => x?.ToString().Trim() ?? "").Where(s => s.Length > 0).ToList() : [];
            if (tags.Count == 0) continue;
            sb.Append(key).Append(":\n");
            foreach (var t in tags) sb.Append("  - ").Append(Quote(t)).Append('\n');
        }
        return sb.ToString();

        static string Bool(JsonNode? n, bool fallback) =>
            (n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback) ? "true" : "false";
    }

    // ---- blog authors ---------------------------------------------------------------------

    /// <summary>The blog's <c>.authors.yml</c>, relative to the docs folder.</summary>
    public string AuthorsFile()
    {
        var dir = Text(Options("blog")?["blog_dir"]) ?? "blog/";
        dir = dir.Trim('/');
        return (dir.Length == 0 ? "" : dir + "/") + ".authors.yml";
    }

    public IReadOnlyList<BlogAuthor> ReadAuthors()
    {
        var path = workspace.ResolveDocsPath(AuthorsFile());
        if (!File.Exists(path)) return [];
        var result = new List<BlogAuthor>();
        foreach (var (id, value) in YamlTree.Parse(File.ReadAllText(path)).AsMap().Get("authors").AsMap())
        {
            var a = value.AsMap();
            result.Add(new BlogAuthor(id, a.Get("name").AsString() ?? id, a.Get("description").AsString() ?? "", a.Get("avatar").AsString() ?? ""));
        }
        return result;
    }

    public IReadOnlyList<BlogAuthor> SaveAuthors(IReadOnlyList<BlogAuthor> authors)
    {
        var sb = new StringBuilder("authors:\n");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in authors)
        {
            var id = (a.Id ?? "").Trim();
            if (id.Length == 0) continue;
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_.-]+$")) throw new CmsException($"Author id '{id}' may only use letters, digits, '-', '_' and '.'.");
            if (!seen.Add(id)) throw new CmsException($"Author id '{id}' is listed twice.");
            sb.Append("  ").Append(id).Append(":\n");
            sb.Append("    name: ").Append(Quote(string.IsNullOrWhiteSpace(a.Name) ? id : a.Name.Trim())).Append('\n');
            if (!string.IsNullOrWhiteSpace(a.Description)) sb.Append("    description: ").Append(Quote(a.Description.Trim())).Append('\n');
            if (!string.IsNullOrWhiteSpace(a.Avatar)) sb.Append("    avatar: ").Append(Quote(a.Avatar.Trim())).Append('\n');
        }
        var path = workspace.ResolveDocsPath(AuthorsFile());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CmsWorkspace.WriteText(path, sb.ToString());
        return ReadAuthors();
    }

    // ---- footer social links ----------------------------------------------------------------

    public JsonArray ReadSocialLinks() =>
        JsonSpanEditor.Get(Config(), "Netdocs", "extra", "social") as JsonArray ?? [];

    public JsonArray SaveSocialLinks(JsonArray links)
    {
        var clean = new JsonArray();
        foreach (var item in links)
        {
            if (item is not JsonObject o) continue;
            var link = new JsonObject();
            foreach (var (key, value) in o)
                if (value is not JsonValue v || !v.TryGetValue<string>(out var s) || s.Trim().Length > 0)
                    link[key] = value?.DeepClone();
            if (link.Count > 0) clean.Add(link);
        }
        var json = Config();
        if (JsonNode.DeepEquals(clean, ReadSocialLinks())) return clean;
        json = JsonSpanEditor.Set(json, clean.Count == 0 ? null : clean, "Netdocs", "extra", "social");
        TemplateService.ValidateConfig(workspace, json);
        CmsWorkspace.WriteText(workspace.ConfigPath, json);
        workspace.Reload();
        return ReadSocialLinks();
    }

    // ---- helpers ----------------------------------------------------------------------------

    private string Config() => File.ReadAllText(workspace.ConfigPath);

    private JsonObject? Options(string plugin)
    {
        var json = Config();
        var entry = Entries(json).FirstOrDefault(e => e.Name.Equals(plugin, StringComparison.OrdinalIgnoreCase));
        return entry?.Options;
    }

    private static JsonObject? RawEntry(string json, int index) =>
        JsonSpanEditor.Get(json, PluginsPath) is JsonArray a && index < a.Count ? a[index] as JsonObject : null;

    private static List<PluginEntry> Entries(string json)
    {
        var result = new List<PluginEntry>();
        if (JsonSpanEditor.Get(json, PluginsPath) is not JsonArray list) return result;
        for (var i = 0; i < list.Count; i++)
        {
            switch (list[i])
            {
                case JsonObject o:
                    var name = Field(o, "name")?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var enabled = Field(o, "enabled") is not JsonValue ev || !ev.TryGetValue<bool>(out var b) || b;
                    result.Add(new PluginEntry(name, enabled, Field(o, "options")?.DeepClone() as JsonObject ?? [], i));
                    break;
                case JsonValue v when v.TryGetValue<string>(out var s):
                    result.Add(new PluginEntry(s, true, [], i));
                    break;
            }
        }
        return result;
    }

    private static JsonNode? Field(JsonObject o, string key) =>
        o.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private IReadOnlyList<PluginEntry> Write(string json)
    {
        TemplateService.ValidateConfig(workspace, json);
        CmsWorkspace.WriteText(workspace.ConfigPath, json);
        workspace.Reload();
        return Entries(json);
    }

    private static string? Text(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : null;

    private static string Quote(string s) => JsonSerializer.Serialize(s, YamlString);
}
