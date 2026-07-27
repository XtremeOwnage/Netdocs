using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Annotates outbound links that match configured rules with an automatic footnote
/// reference. The footnote carries a note (arbitrary markdown), so:
/// <list type="bullet">
///   <item>hovering the link shows the note as a tooltip (via
///   <c>content.footnote.tooltips</c>), and</item>
///   <item>the note is emitted once at the bottom of the page (the footnote list).</item>
/// </list>
/// The plugin is generic and data-driven: each <em>rule</em> declares the domains
/// (with an optional query marker) and/or regular expressions that identify its links,
/// plus the note markdown. A common use-case is attaching affiliate-disclosure text to
/// eBay Partner Network / tagged Amazon links (which also satisfies the once-per-page
/// disclosure requirement automatically), but any note works.
/// <para>
/// It runs after snippets/table-reader/macros so links injected by those plugins are
/// covered. Registered as both <c>link-notes</c> and the legacy alias
/// <c>affiliate-links</c>; the legacy <c>programs</c>/<c>disclosure</c> config keys are
/// still accepted.
/// </para>
/// </summary>
public sealed partial class LinkNotesPlugin : IPlugin, IMarkdownPreprocessor
{
    private sealed record DomainRule(string Domain, string? QueryContains);
    private sealed record Rule(string Id, DomainRule[] Domains, Regex[] Patterns, string Note, string Label, string Kind);

    private readonly List<Rule> _rules = [];
    private readonly List<string> _snippetBasePaths = [];
    private ILogger? _log;

    public string Name => "link-notes";

    // After snippets (10), table-reader (20) and macros (25) so their generated links are seen.
    public int Order => 30;

    // Matches a markdown inline link `[text](url "title")` plus an optional attr-list `{...}`, any
    // footnote reference already following it (so we don't double-annotate), and looks ahead for an
    // immediately-adjacent `[` (another link/ref) which would make an injected footnote ambiguous.
    private static readonly Regex LinkRegex = new(
        """(?<link>\[(?:[^\]]|\\\])*\]\(\s*<?(?<url>[^)\s>]+)>?(?:\s+"[^"]*")?\s*\))(?<attr>\{[^}]*\})?(?<existing>\[\^[^\]]+\])?(?=(?<adjacent>\[)?)""",
        RegexOptions.Compiled);

    public void Configure(IPluginContext ctx)
    {
        _log = ctx.Logger;

        // Snippet search roots (mirrors SnippetsPlugin): the project root and docs dir, each with a
        // conventional `snippets` subdirectory. A `note_snippet` path is resolved against these so a
        // rule can reuse the same admonition snippet included elsewhere on the site.
        var root = ctx.Config.ProjectRoot ?? "";
        void AddBase(string p) { if (p.Length > 0 && !_snippetBasePaths.Contains(p)) _snippetBasePaths.Add(p); }
        AddBase(Path.GetFullPath(root.Length == 0 ? "." : root));
        AddBase(Path.GetFullPath(Path.Combine(root, "snippets")));
        AddBase(Path.GetFullPath(ctx.Config.AbsoluteDocsDir));
        AddBase(Path.GetFullPath(Path.Combine(ctx.Config.AbsoluteDocsDir, "snippets")));

        // Accept the new `rules` key; fall back to the legacy `programs` key (affiliate-links).
        if (!ctx.PluginOptions.TryGetValue("rules", out var raw) || raw is not IEnumerable<object?>)
            ctx.PluginOptions.TryGetValue("programs", out raw);

        if (raw is IEnumerable<object?> list)
        {
            foreach (var item in list)
            {
                if (item is not IReadOnlyDictionary<string, object?> map) continue;

                var id = map.TryGetValue("name", out var n) ? n?.ToString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;

                // `note` (new) or `disclosure` (legacy alias) supply the note markdown inline.
                var note = (map.TryGetValue("note", out var nt) ? nt?.ToString() : null)
                         ?? (map.TryGetValue("disclosure", out var d) ? d?.ToString() : null);

                // Rule-level query marker is the default for any domain that doesn't override it.
                var defaultQuery = map.TryGetValue("query_contains", out var q) ? q?.ToString() : null;
                if (string.IsNullOrEmpty(defaultQuery)) defaultQuery = null;

                var domains = ReadDomainRules(map, "domains", defaultQuery);
                var patterns = ReadPatterns(map, "patterns");
                if (domains.Length == 0 && patterns.Length == 0)
                {
                    _log.LogWarning("link-notes: rule '{Id}' has no domains or patterns; skipping", id);
                    continue;
                }

                // Explicit `label` overrides everything; otherwise a snippet's admonition title is used.
                var explicitLabel = map.TryGetValue("label", out var lv) && !string.IsNullOrWhiteSpace(lv?.ToString())
                    ? lv!.ToString()!.Trim()
                    : null;

                // `note_snippet` (or legacy `disclosure_snippet`) points at a markdown snippet whose
                // content becomes the note. When the snippet is a single admonition, its title/kind
                // drive the standalone fallback box and its body becomes the tooltip/footnote text.
                var snippetPath = (map.TryGetValue("note_snippet", out var ns) ? ns?.ToString() : null)
                                ?? (map.TryGetValue("disclosure_snippet", out var ds) ? ds?.ToString() : null);

                var kind = "info";
                string? snippetTitle = null;
                if (!string.IsNullOrWhiteSpace(snippetPath))
                {
                    var content = ReadSnippet(snippetPath!, id!);
                    if (content is not null)
                    {
                        (kind, snippetTitle, note) = ExtractNote(content);
                    }
                }

                if (string.IsNullOrWhiteSpace(note))
                {
                    _log.LogWarning("link-notes: rule '{Id}' has no note (inline or snippet); skipping", id);
                    continue;
                }

                var label = explicitLabel ?? snippetTitle ?? "Links";
                _rules.Add(new Rule(id!, domains, patterns, note!.Trim('\n'), label, kind));
            }
        }

        if (_rules.Count == 0)
            _log.LogWarning("link-notes: no link rules configured; plugin is a no-op");
    }

    // Resolves a snippet path against the configured base directories and returns its text, or null.
    private string? ReadSnippet(string path, string ruleId)
    {
        if (Path.IsPathRooted(path) && File.Exists(path)) return File.ReadAllText(path);
        foreach (var basePath in _snippetBasePaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(basePath, path));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        _log?.LogWarning("link-notes: rule '{Id}' snippet '{Path}' not found; falling back to inline note", ruleId, path);
        return null;
    }

    // Splits snippet content into (kind, title, body). If the snippet is a single admonition
    // (`!!! kind "Title"` with a 4-space indented body), the title/kind are returned and the body is
    // de-indented so it renders as plain paragraphs inside a footnote tooltip (an admonition cannot
    // render inside a footnote) while still driving the standalone fallback box. Otherwise the whole
    // trimmed content is treated as the body with kind "info" and no title.
    private static (string Kind, string? Title, string Body) ExtractNote(string content)
    {
        var text = content.Replace("\r\n", "\n").Trim('\n');
        var lines = text.Split('\n');
        var first = lines.Length > 0 ? lines[0] : "";
        var m = AdmonitionHeader().Match(first);
        if (!m.Success)
            return ("info", null, text.Trim());

        var kind = m.Groups["kind"].Value.Trim();
        if (kind.Length == 0) kind = "info";
        var title = m.Groups["title"].Success ? m.Groups["title"].Value : null;

        // De-indent the admonition body (strip one 4-space / tab indent from each line).
        var body = new StringBuilder();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("    ", StringComparison.Ordinal)) line = line[4..];
            else if (line.StartsWith("\t", StringComparison.Ordinal)) line = line[1..];
            body.Append(line).Append('\n');
        }
        return (kind, string.IsNullOrWhiteSpace(title) ? null : title.Trim(), body.ToString().Trim('\n'));
    }

    [GeneratedRegex("""^(?:!!!|\?\?\?\+?)\s+(?<kind>[^"\n]*?)\s*(?:"(?<title>[^"]*)")?\s*$""")]
    private static partial Regex AdmonitionHeader();

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (_rules.Count == 0 || markdown.Length == 0) return Task.FromResult(markdown);

        // Rules whose links got an inline footnote reference (definition will be rendered by the
        // footnote extension) vs. rules seen only in contexts where a reference can't be injected
        // (pipe-table cells), which need a standalone note block appended instead.
        var referenced = new HashSet<string>();
        var tableOnly = new HashSet<string>();

        var lines = markdown.Split('\n');
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            // Markdig does not reliably parse footnote references inside pipe-table cells, so injecting
            // one there breaks the table. Detect matching links there (to guarantee the footer
            // note) but don't annotate the link.
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                foreach (Match m in LinkRegex.Matches(lines[i]))
                {
                    var rule = MatchRule(m.Groups["url"].Value);
                    if (rule is not null && !m.Groups["existing"].Success) tableOnly.Add(rule.Id);
                }
                continue;
            }

            lines[i] = LinkRegex.Replace(lines[i], m => AnnotateLink(m, referenced, tableOnly));
        }

        // A rule that got at least one inline reference doesn't also need a standalone block.
        tableOnly.ExceptWith(referenced);

        if (referenced.Count == 0 && tableOnly.Count == 0) return Task.FromResult(markdown);

        var sb = new StringBuilder(string.Join('\n', lines));
        sb.Append("\n\n");

        // Footnote definitions for referenced rules: Markdig renders these at the bottom of the
        // page (the footer note) and links every reference to them (the hover tooltip). Continuation
        // lines are indented 4 spaces so multi-paragraph notes stay within the footnote definition.
        foreach (var rule in _rules.Where(r => referenced.Contains(r.Id)))
            sb.Append("[^linknote-").Append(rule.Id).Append("]: ")
              .Append(rule.Note.Replace("\n", "\n    ", StringComparison.Ordinal)).Append('\n');

        // Rules seen only inside tables get a standalone note admonition so the footer note
        // requirement is still met even though the individual links can't carry a tooltip.
        foreach (var rule in _rules.Where(r => tableOnly.Contains(r.Id)))
        {
            sb.Append("\n!!! ").Append(rule.Kind).Append(" \"").Append(rule.Label).Append("\"\n    ");
            sb.Append(rule.Note.Replace("\n", "\n    ", StringComparison.Ordinal));
            sb.Append('\n');
        }

        return Task.FromResult(sb.ToString());
    }

    private string AnnotateLink(Match m, HashSet<string> referenced, HashSet<string> fallback)
    {
        var whole = m.Value;
        var rule = MatchRule(m.Groups["url"].Value);
        if (rule is null) return whole;

        // A footnote reference already follows this link (e.g. a hand-authored `[^ebay]`); leave it
        // untouched so we don't produce duplicate references during content migration.
        if (m.Groups["existing"].Success) return whole;

        // Another link/reference is glued directly after this one (e.g. `[a](x)[b](y)`); a footnote
        // ref wedged between the `][` renders ambiguously, so skip it and rely on the fallback block.
        if (m.Groups["adjacent"].Success)
        {
            fallback.Add(rule.Id);
            return whole;
        }

        referenced.Add(rule.Id);
        return whole + $"[^linknote-{rule.Id}]";
    }

    private Rule? MatchRule(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.Host;
        foreach (var rule in _rules)
        {
            foreach (var dr in rule.Domains)
            {
                var domainHit = host.Equals(dr.Domain, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + dr.Domain, StringComparison.OrdinalIgnoreCase);
                if (!domainHit) continue;

                if (dr.QueryContains is not null &&
                    url.IndexOf(dr.QueryContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                return rule;
            }

            // Regex fallback: any configured pattern that matches the full URL selects the rule.
            foreach (var rx in rule.Patterns)
                if (rx.IsMatch(url)) return rule;
        }
        return null;
    }

    // Reads a rule's `domains` list. Each entry may be a plain string (uses the rule-level query
    // marker, if any) or an object `{ "domain": "...", "query_contains": "..." }` to require a
    // specific marker only for that domain (e.g. amazon.com needs `tag=` but amzn.to never does).
    private static DomainRule[] ReadDomainRules(IReadOnlyDictionary<string, object?> map, string key, string? defaultQuery)
    {
        if (!map.TryGetValue(key, out var v) || v is not IEnumerable<object?> list) return [];

        var rules = new List<DomainRule>();
        foreach (var entry in list)
        {
            switch (entry)
            {
                case string s when s.Length > 0:
                    rules.Add(new DomainRule(s, defaultQuery));
                    break;
                case IReadOnlyDictionary<string, object?> obj:
                    var dom = obj.TryGetValue("domain", out var dv) ? dv?.ToString() : null;
                    if (string.IsNullOrWhiteSpace(dom)) break;
                    var q = obj.TryGetValue("query_contains", out var qv) ? qv?.ToString() : null;
                    rules.Add(new DomainRule(dom!, string.IsNullOrEmpty(q) ? defaultQuery : q));
                    break;
            }
        }
        return rules.ToArray();
    }

    // Reads a rule's optional `patterns` list — regular expressions matched (case-insensitively)
    // against the full link URL. Invalid patterns are logged and skipped rather than aborting.
    private Regex[] ReadPatterns(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var v) || v is not IEnumerable<object?> list) return [];

        var patterns = new List<Regex>();
        foreach (var entry in list)
        {
            if (entry?.ToString() is not { Length: > 0 } pat) continue;
            try
            {
                patterns.Add(new Regex(pat, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
            catch (ArgumentException ex)
            {
                _log?.LogWarning("link-notes: invalid pattern '{Pattern}': {Message}", pat, ex.Message);
            }
        }
        return patterns.ToArray();
    }
}
