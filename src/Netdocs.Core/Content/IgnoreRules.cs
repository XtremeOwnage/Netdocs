using System.Text.RegularExpressions;

namespace Netdocs.Core.Content;

/// <summary>Minimal .mkdocsignore matcher (gitignore-style globs against docs-relative paths).</summary>
public sealed partial class IgnoreRules
{
    private readonly List<Regex> _patterns;

    private IgnoreRules(List<Regex> patterns) => _patterns = patterns;

    public static IgnoreRules Load(string projectRoot, string docsDir)
    {
        var patterns = new List<Regex>();
        foreach (var name in new[] { ".mkdocsignore" })
        {
            foreach (var path in new[] { Path.Combine(docsDir, name), Path.Combine(projectRoot, name) })
            {
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    patterns.Add(GlobToRegex(trimmed));
                }
            }
        }
        return new IgnoreRules(patterns);
    }

    public static IgnoreRules Load(string projectRoot, string docsDir, IEnumerable<string> extraPatterns)
    {
        var rules = Load(projectRoot, docsDir);
        foreach (var pattern in extraPatterns)
        {
            var trimmed = pattern.Trim();
            if (trimmed.Length > 0) rules._patterns.Add(GlobToRegex(trimmed));
        }
        return rules;
    }

    public bool IsIgnored(string relativePath)
    {
        foreach (var pattern in _patterns)
            if (pattern.IsMatch(relativePath))
                return true;
        return false;
    }

    private static Regex GlobToRegex(string glob)
    {
        var normalized = glob.TrimStart('/').Replace('\\', '/');
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < normalized.Length && normalized[i + 1] == '*') { sb.Append(".*"); i++; }
                    else sb.Append("[^/]*");
                    break;
                case '?': sb.Append("[^/]"); break;
                case '.': sb.Append("\\."); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        if (normalized.EndsWith('/')) sb.Append(".*");
        sb.Append("(/.*)?$");
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
