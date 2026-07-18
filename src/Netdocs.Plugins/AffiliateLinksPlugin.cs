using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Netdocs.Abstractions;

namespace Netdocs.Plugins;

/// <summary>
/// Annotates outbound affiliate links (e.g. eBay Partner Network <c>ebay.us</c> links or
/// tagged Amazon links) with an automatic footnote reference. The footnote carries the
/// program's disclosure text, so:
/// <list type="bullet">
///   <item>hovering the link shows the disclosure as a tooltip (via
///   <c>content.footnote.tooltips</c>), and</item>
///   <item>the disclosure is emitted once at the bottom of the page (the footnote list),
///   satisfying affiliate-disclosure requirements automatically.</item>
/// </list>
/// The plugin is opt-in and fully data-driven: each affiliate <em>program</em> declares the
/// domains (and optional query marker) that identify its links plus the disclosure markdown.
/// It runs after snippets/table-reader/macros so links injected by those plugins are covered.
/// </summary>
public sealed class AffiliateLinksPlugin : IPlugin, IMarkdownPreprocessor
{
    private sealed record DomainRule(string Domain, string? QueryContains);
    private sealed record Program(string Id, DomainRule[] Domains, string Disclosure);

    private readonly List<Program> _programs = [];
    private ILogger? _log;

    public string Name => "affiliate-links";

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

        if (ctx.PluginOptions.TryGetValue("programs", out var raw) && raw is IEnumerable<object?> list)
        {
            foreach (var item in list)
            {
                if (item is not IReadOnlyDictionary<string, object?> map) continue;

                var id = map.TryGetValue("name", out var n) ? n?.ToString() : null;
                var disclosure = map.TryGetValue("disclosure", out var d) ? d?.ToString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(disclosure)) continue;

                // Program-level query marker is the default for any domain that doesn't override it.
                var defaultQuery = map.TryGetValue("query_contains", out var q) ? q?.ToString() : null;
                if (string.IsNullOrEmpty(defaultQuery)) defaultQuery = null;

                var domains = ReadDomainRules(map, "domains", defaultQuery);
                if (domains.Length == 0) continue;

                _programs.Add(new Program(id!, domains, disclosure!.Trim()));
            }
        }

        if (_programs.Count == 0)
            _log.LogWarning("affiliate-links: no affiliate programs configured; plugin is a no-op");
    }

    public Task<string> ProcessAsync(Page page, string markdown, SiteContext site, CancellationToken ct)
    {
        if (_programs.Count == 0 || markdown.Length == 0) return Task.FromResult(markdown);

        // Programs whose links got an inline footnote reference (definition will be rendered by the
        // footnote extension) vs. programs seen only in contexts where a reference can't be injected
        // (pipe-table cells), which need a standalone disclosure block appended instead.
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
            // one there breaks the table. Detect affiliate links there (to guarantee the footer
            // disclosure) but don't annotate the link.
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                foreach (Match m in LinkRegex.Matches(lines[i]))
                {
                    var prog = MatchProgram(m.Groups["url"].Value);
                    if (prog is not null && !m.Groups["existing"].Success) tableOnly.Add(prog.Id);
                }
                continue;
            }

            lines[i] = LinkRegex.Replace(lines[i], m => AnnotateLink(m, referenced, tableOnly));
        }

        // A program that got at least one inline reference doesn't also need a standalone block.
        tableOnly.ExceptWith(referenced);

        if (referenced.Count == 0 && tableOnly.Count == 0) return Task.FromResult(markdown);

        var sb = new StringBuilder(string.Join('\n', lines));
        sb.Append("\n\n");

        // Footnote definitions for referenced programs: Markdig renders these at the bottom of the
        // page (the footer disclosure) and links every reference to them (the hover tooltip).
        foreach (var program in _programs.Where(p => referenced.Contains(p.Id)))
            sb.Append("[^affiliate-").Append(program.Id).Append("]: ").Append(program.Disclosure).Append('\n');

        // Programs seen only inside tables get a standalone disclosure admonition so the footer
        // disclosure requirement is still met even though the individual links can't carry a tooltip.
        foreach (var program in _programs.Where(p => tableOnly.Contains(p.Id)))
        {
            sb.Append("\n!!! info \"Affiliate links\"\n    ");
            sb.Append(program.Disclosure.Replace("\n", "\n    ", StringComparison.Ordinal));
            sb.Append('\n');
        }

        return Task.FromResult(sb.ToString());
    }

    private string AnnotateLink(Match m, HashSet<string> referenced, HashSet<string> fallback)
    {
        var whole = m.Value;
        var program = MatchProgram(m.Groups["url"].Value);
        if (program is null) return whole;

        // A footnote reference already follows this link (e.g. a hand-authored `[^ebay]`); leave it
        // untouched so we don't produce duplicate references during content migration.
        if (m.Groups["existing"].Success) return whole;

        // Another link/reference is glued directly after this one (e.g. `[a](x)[b](y)`); a footnote
        // ref wedged between the `][` renders ambiguously, so skip it and rely on the fallback block.
        if (m.Groups["adjacent"].Success)
        {
            fallback.Add(program.Id);
            return whole;
        }

        referenced.Add(program.Id);
        return whole + $"[^affiliate-{program.Id}]";
    }

    private Program? MatchProgram(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.Host;
        foreach (var program in _programs)
        {
            foreach (var rule in program.Domains)
            {
                var domainHit = host.Equals(rule.Domain, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + rule.Domain, StringComparison.OrdinalIgnoreCase);
                if (!domainHit) continue;

                if (rule.QueryContains is not null &&
                    url.IndexOf(rule.QueryContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                return program;
            }
        }
        return null;
    }

    // Reads a program's `domains` list. Each entry may be a plain string (uses the program-level
    // query marker, if any) or an object `{ "domain": "...", "query_contains": "..." }` to require a
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
}
