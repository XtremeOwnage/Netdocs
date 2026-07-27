using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Annotates outbound links that match configured rules, so a note (arbitrary markdown) is
/// attached to them automatically. Two render modes are supported per rule:
/// <list type="bullet">
///   <item><b>Footnote mode</b> (default): the link gets a footnote reference whose definition is
///   emitted once at the bottom of the page. With <c>content.footnote.tooltips</c> the note also
///   shows on hover.</item>
///   <item><b>Tooltip mode</b> (<c>link_snippet</c> set): each matching link is replaced inline
///   with the snippet rendered as a template — the matched URL and link text are substituted for
///   <c>${url}</c>/<c>${text}</c> — producing a hover popup, and the rule's disclosure box is
///   emitted once per page instead of a footnote. Works inside pipe-table cells too.</item>
/// </list>
/// The plugin is generic and data-driven: each <em>rule</em> declares the domains
/// (with an optional query marker) and/or regular expressions that identify its links,
/// plus the note markdown and/or link snippet. A common use-case is attaching an
/// affiliate-disclosure to eBay Partner Network / tagged Amazon links (which also satisfies the
/// once-per-page disclosure requirement automatically), but any note works.
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
    private sealed record Rule(string Id, DomainRule[] Domains, Regex[] Patterns, string Note, string Label, string Kind, string? LinkSnippet);

    private readonly List<Rule> _rules = [];
    private readonly List<string> _snippetBasePaths = [];
    private readonly List<string> _configErrors = [];
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
                    var content = ReadSnippet(snippetPath!);
                    if (content is null)
                    {
                        // An explicitly referenced snippet that cannot be found is a configuration
                        // mistake (typically a typo in the path). Silently dropping the rule would
                        // omit an affiliate disclosure without any signal, which is worse than a
                        // failed build — so record it as a fatal error. It is thrown from
                        // ProcessAsync (below) rather than here so it aborts the build even outside
                        // `--strict` (plugin Configure exceptions are otherwise swallowed).
                        _configErrors.Add(
                            $"rule '{id}' references note_snippet '{snippetPath}' which was not found " +
                            $"(searched: {string.Join(", ", _snippetBasePaths)})");
                        continue;
                    }
                    (kind, snippetTitle, note) = ExtractNote(content);
                }

                // `link_snippet` opts the rule into *tooltip mode*: instead of appending a footnote,
                // every matching link is replaced inline with this snippet rendered as a template
                // (the matched URL and link text are substituted for `${url}`/`${text}`), producing a
                // hover popup. This is the pretty per-link affiliate popup; a referenced-but-missing
                // snippet fails the build for the same reason `note_snippet` does.
                var linkSnippetPath = map.TryGetValue("link_snippet", out var lsp) ? lsp?.ToString() : null;
                string? linkSnippet = null;
                if (!string.IsNullOrWhiteSpace(linkSnippetPath))
                {
                    linkSnippet = ReadSnippet(linkSnippetPath!);
                    if (linkSnippet is null)
                    {
                        _configErrors.Add(
                            $"rule '{id}' references link_snippet '{linkSnippetPath}' which was not found " +
                            $"(searched: {string.Join(", ", _snippetBasePaths)})");
                        continue;
                    }
                    linkSnippet = linkSnippet.Replace("\r\n", "\n").Trim('\n');
                }

                // A rule needs *something* to attach: either a note (footnote / page box) or a
                // link_snippet (per-link popup). A rule with neither is a no-op.
                if (string.IsNullOrWhiteSpace(note) && linkSnippet is null)
                {
                    _log.LogWarning("link-notes: rule '{Id}' has no note (inline or snippet) or link_snippet; skipping", id);
                    continue;
                }

                var label = explicitLabel ?? snippetTitle ?? "Links";
                _rules.Add(new Rule(id!, domains, patterns, note?.Trim('\n') ?? "", label, kind, linkSnippet));
            }
        }

        if (_rules.Count == 0)
            _log.LogWarning("link-notes: no link rules configured; plugin is a no-op");
    }

    // Resolves a snippet path against the configured base directories and returns its text, or null
    // when it cannot be found (the caller turns an unresolved explicit reference into a build error).
    private string? ReadSnippet(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path)) return File.ReadAllText(path);
        foreach (var basePath in _snippetBasePaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(basePath, path));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
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
        // A referenced-but-missing note_snippet is a fatal configuration error. Throwing here (from
        // the build's preprocess loop, outside PluginHost.Configure's try/catch) aborts the build
        // with a non-zero exit even without `--strict`, so a mistyped snippet path can never
        // silently omit an affiliate disclosure.
        if (_configErrors.Count > 0)
            throw new FileNotFoundException(
                "link-notes: " + string.Join("; ", _configErrors));

        if (_rules.Count == 0 || markdown.Length == 0) return Task.FromResult(markdown);

        // Rules whose links got an inline footnote reference (definition will be rendered by the
        // footnote extension) vs. rules seen only in contexts where a reference can't be injected
        // (pipe-table cells), which need a standalone note block appended instead.
        var referenced = new HashSet<string>();
        var tableOnly = new HashSet<string>();
        // Tooltip-mode rules (with a link_snippet) that matched at least one link on this page and so
        // need their disclosure box emitted once at the bottom.
        var boxed = new HashSet<string>();

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

            // Inside a pipe-table cell a footnote reference breaks the table (Markdig can't parse it
            // there), but a tooltip-mode rule replaces the link with inline HTML, which is safe in a
            // cell — so table rows are still processed; AnnotateLink is told it is in a table so a
            // footnote-mode rule falls back to the standalone box instead of injecting a reference.
            var inTable = trimmed.StartsWith("|", StringComparison.Ordinal);
            lines[i] = LinkRegex.Replace(lines[i], m => AnnotateLink(m, referenced, tableOnly, boxed, inTable));
        }

        // A rule that got at least one inline reference doesn't also need a standalone block.
        tableOnly.ExceptWith(referenced);

        if (referenced.Count == 0 && tableOnly.Count == 0 && boxed.Count == 0)
            return Task.FromResult(markdown);

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
            AppendBox(sb, rule);

        // Tooltip-mode rules emit their disclosure admonition once per page (the per-link popups
        // carry the hover text; this keeps a single always-visible disclosure without any footnotes).
        foreach (var rule in _rules.Where(r => boxed.Contains(r.Id) && r.Note.Length > 0))
            AppendBox(sb, rule);

        return Task.FromResult(sb.ToString());
    }

    // Appends a standalone `!!! kind "label"` admonition carrying a rule's note (indented body).
    private static void AppendBox(StringBuilder sb, Rule rule)
    {
        sb.Append("\n!!! ").Append(rule.Kind).Append(" \"").Append(rule.Label).Append("\"\n    ");
        sb.Append(rule.Note.Replace("\n", "\n    ", StringComparison.Ordinal));
        sb.Append('\n');
    }

    private string AnnotateLink(Match m, HashSet<string> referenced, HashSet<string> fallback, HashSet<string> boxed, bool inTable)
    {
        var whole = m.Value;
        var rule = MatchRule(m.Groups["url"].Value);
        if (rule is null) return whole;

        // A footnote reference already follows this link (e.g. a hand-authored `[^ebay]`); leave it
        // untouched so we don't produce duplicate references / double-wrap during content migration.
        if (m.Groups["existing"].Success) return whole;

        // Tooltip mode: replace the whole link with the link_snippet rendered as a template, passing
        // the matched URL and link text. This works in table cells and next to adjacent links (it is
        // inline HTML, not a footnote), and the disclosure box is emitted once per page.
        if (rule.LinkSnippet is not null)
        {
            boxed.Add(rule.Id);
            var text = ExtractLinkText(m.Groups["link"].Value);
            var popup = RenderLinkSnippet(rule.LinkSnippet, m.Groups["url"].Value, text);
            // Preserve any trailing attr-list the author added (rare, but keep it after the popup).
            return popup + m.Groups["attr"].Value;
        }

        // Footnote mode inside a pipe-table cell: a reference would break the table, so record the
        // rule for a standalone box instead of annotating the link.
        if (inTable)
        {
            fallback.Add(rule.Id);
            return whole;
        }

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

    // Extracts the display text of a markdown link `[text](url ...)` — everything between the first
    // `[` and its matching `]`. Unescapes `\]` back to `]`.
    private static string ExtractLinkText(string link)
    {
        var open = link.IndexOf('[');
        if (open < 0) return "";
        var i = open + 1;
        var sb = new StringBuilder();
        while (i < link.Length && link[i] != ']')
        {
            if (link[i] == '\\' && i + 1 < link.Length && link[i + 1] == ']') { sb.Append(']'); i += 2; continue; }
            sb.Append(link[i]);
            i++;
        }
        return sb.ToString();
    }

    // Renders a link_snippet template by substituting the matched URL / link text (HTML-escaped) for
    // the `${url}`, `${text}` and `${domain}` placeholders — the same `${key}` convention the
    // snippets plugin uses for parameterized includes.
    private static string RenderLinkSnippet(string template, string url, string text)
    {
        var domain = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        return template
            .Replace("${url}", HtmlEscape(url), StringComparison.Ordinal)
            .Replace("${text}", HtmlEscape(text), StringComparison.Ordinal)
            .Replace("${domain}", HtmlEscape(domain), StringComparison.Ordinal);
    }

    private static string HtmlEscape(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

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
