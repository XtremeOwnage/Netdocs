using System.Text.RegularExpressions;

namespace Netdocs.Core.Content;

/// <summary>Minimal .mkdocsignore matcher (gitignore-style globs against docs-relative paths).</summary>
public sealed partial class IgnoreRules
{
    private readonly List<Regex> _patterns;

    private IgnoreRules(List<Regex> patterns) => _patterns = patterns;

    /// <param name="includeIgnoreFile">
    /// When true, patterns from the <paramref name="ignoreFileName"/> file (default
    /// <c>.mkdocsignore</c>) are loaded. Callers pass false to skip it — e.g. a development build
    /// where the file-filter is disabled — so dev-only sections listed there stay in the site.
    /// </param>
    /// <param name="ignoreFileName">Name of the ignore file to read (from docs dir, then project root).</param>
    public static IgnoreRules Load(
        string projectRoot,
        string docsDir,
        IEnumerable<string> extraPatterns,
        bool includeIgnoreFile = true,
        string ignoreFileName = ".mkdocsignore")
    {
        var patterns = new List<Regex>();

        if (includeIgnoreFile)
        {
            foreach (var path in new[] { Path.Combine(docsDir, ignoreFileName), Path.Combine(projectRoot, ignoreFileName) })
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

        // Explicit config `exclude` globs are always honoured, independent of the ignore file.
        foreach (var pattern in extraPatterns)
        {
            var trimmed = pattern.Trim();
            if (trimmed.Length > 0) patterns.Add(GlobToRegex(trimmed));
        }

        return new IgnoreRules(patterns);
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
