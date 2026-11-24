using System.Text;

namespace Netdocs.Core.Cms;

/// <summary>One tag (or blog category) and the pages that use it.</summary>
public sealed record TermUsage(string Name, int Count, IReadOnlyList<string> Pages);

/// <summary>Terms that are probably the same thing spelled differently.</summary>
public sealed record SimilarTerms(IReadOnlyList<string> Names, string Reason);

/// <summary>
/// Site-wide tools for front matter lists such as <c>tags</c> and <c>categories</c>: usage counts,
/// rename, merge and delete across every page, and a report of near-duplicate and single-use
/// terms. Edits go through <see cref="FrontMatterEditor"/>, so only the list itself changes.
/// </summary>
public sealed class TaxonomyService(CmsWorkspace workspace, ContentStore content)
{
    public static readonly string[] Fields = ["tags", "categories"];

    public IReadOnlyList<TermUsage> List(string field)
    {
        RequireField(field);
        var usage = new Dictionary<string, (string Name, List<string> Pages)>(StringComparer.Ordinal);
        foreach (var page in content.AllPagePaths())
        {
            var meta = Content.FrontMatter.Split(File.ReadAllText(workspace.ResolveDocsPath(page))).Meta;
            foreach (var term in ContentStore.StringList(meta, field).Distinct(StringComparer.Ordinal))
            {
                if (!usage.TryGetValue(term, out var entry)) usage[term] = entry = (term, []);
                entry.Pages.Add(page);
            }
        }
        return [.. usage.Values
            .Select(u => new TermUsage(u.Name, u.Pages.Count, u.Pages))
            .OrderByDescending(u => u.Count).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Renames <paramref name="from"/> terms to <paramref name="to"/> on every page. Several
    /// sources merge them into one. Returns the number of pages changed.</summary>
    public int Rename(string field, IReadOnlyCollection<string> from, string to)
    {
        RequireField(field);
        to = (to ?? "").Trim();
        if (to.Length == 0) throw new CmsException("The new name cannot be empty.");
        return Update(field, terms =>
        {
            if (!terms.Any(from.Contains)) return null;
            var result = new List<string>();
            foreach (var t in terms)
            {
                var name = from.Contains(t) ? to : t;
                if (!result.Contains(name, StringComparer.Ordinal)) result.Add(name);
            }
            return result;
        });
    }

    /// <summary>Removes terms from every page. Returns the number of pages changed.</summary>
    public int Delete(string field, IReadOnlyCollection<string> names)
    {
        RequireField(field);
        return Update(field, terms => terms.Any(names.Contains) ? [.. terms.Where(t => !names.Contains(t))] : null);
    }

    /// <summary>Adds or removes terms on a set of pages at once.</summary>
    public int Assign(string field, IReadOnlyCollection<string> pages, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove)
    {
        RequireField(field);
        var set = new HashSet<string>(pages, StringComparer.OrdinalIgnoreCase);
        return Update(field, (page, terms) =>
        {
            if (!set.Contains(page)) return null;
            var result = terms.Where(t => !remove.Contains(t)).ToList();
            foreach (var a in add.Select(a => a.Trim()).Where(a => a.Length > 0))
                if (!result.Contains(a, StringComparer.Ordinal)) result.Add(a);
            return result.SequenceEqual(terms) ? null : result;
        });
    }

    /// <summary>Groups of terms that differ only by case, punctuation, a plural "s", or one or two
    /// typos, plus terms used on just one page.</summary>
    public (IReadOnlyList<SimilarTerms> Similar, IReadOnlyList<TermUsage> SingleUse) Report(string field)
    {
        var terms = List(field);
        var similar = new List<SimilarTerms>();
        var grouped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in terms.GroupBy(t => Normalize(t.Name)).Where(g => g.Count() > 1))
        {
            similar.Add(new SimilarTerms([.. group.Select(g => g.Name)], "Same name apart from case, spacing, punctuation or plural"));
            foreach (var g in group) grouped.Add(g.Name);
        }

        var rest = terms.Where(t => !grouped.Contains(t.Name)).ToList();
        for (var i = 0; i < rest.Count; i++)
        {
            for (var j = i + 1; j < rest.Count; j++)
            {
                var a = Normalize(rest[i].Name);
                var b = Normalize(rest[j].Name);
                if (Math.Min(a.Length, b.Length) < 5) continue;
                var distance = Levenshtein(a, b);
                if (distance is > 0 and <= 2)
                    similar.Add(new SimilarTerms([rest[i].Name, rest[j].Name], distance == 1 ? "One letter apart" : "Two letters apart"));
            }
        }

        return (similar, [.. terms.Where(t => t.Count == 1)]);
    }

    private int Update(string field, Func<List<string>, List<string>?> change) => Update(field, (_, terms) => change(terms));

    private int Update(string field, Func<string, List<string>, List<string>?> change)
    {
        var count = 0;
        foreach (var page in content.AllPagePaths())
        {
            var full = workspace.ResolveDocsPath(page);
            var raw = File.ReadAllText(full);
            var editor = FrontMatterEditor.Parse(raw);
            var values = editor.Values;
            var current = ContentStore.StringList(values, field).ToList();
            var updated = change(page, current);
            if (updated is null) continue;
            if (updated.Count == 0) editor.Remove(field);
            else editor.Set(field, updated);
            var text = editor.ToString();
            if (text != raw) { CmsWorkspace.WriteText(full, text); count++; }
        }
        return count;
    }

    private static void RequireField(string field)
    {
        if (!Fields.Contains(field, StringComparer.Ordinal))
            throw new CmsException($"Unknown field '{field}'. Use one of: {string.Join(", ", Fields)}.");
    }

    internal static string Normalize(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        var s = sb.ToString();
        if (s.EndsWith("ies", StringComparison.Ordinal) && s.Length > 4) s = s[..^3] + "y";
        else if (s.EndsWith("es", StringComparison.Ordinal) && s.Length > 4 && (s.EndsWith("ses", StringComparison.Ordinal) || s.EndsWith("xes", StringComparison.Ordinal))) s = s[..^2];
        else if (s.EndsWith('s') && !s.EndsWith("ss", StringComparison.Ordinal) && s.Length > 3) s = s[..^1];
        return s;
    }

    internal static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
