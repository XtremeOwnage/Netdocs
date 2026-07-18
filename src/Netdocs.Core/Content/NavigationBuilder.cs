using Netdocs.Abstractions;

namespace Netdocs.Core.Content;

/// <summary>Resolves the authored nav (or auto-nav) into a tree of <see cref="NavNode"/>.</summary>
public static class NavigationBuilder
{
    public static IReadOnlyList<NavNode> Build(SiteConfig config, IReadOnlyList<Page> pages)
    {
        var byRelative = pages
            .GroupBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (config.Nav.Count == 0)
            return AutoNav(pages);

        return config.Nav.Select(item => Resolve(item, byRelative)).Where(n => n is not null).Select(n => n!).ToList();
    }

    private static NavNode? Resolve(NavItem item, IReadOnlyDictionary<string, Page> byRelative)
    {
        if (item.Path is not null && byRelative.TryGetValue(item.Path, out var page))
            return new NavNode { Title = item.Title ?? page.Title, Page = page, Icon = item.Icon ?? PageIcon(page) };

        if (item.Children.Count > 0)
        {
            var children = item.Children.Select(c => Resolve(c, byRelative)).Where(n => n is not null).Select(n => n!).ToList();

            // navigation.indexes: if the first child is an index/README page, promote it to the
            // section's landing page (its title links there) and drop it from the child list.
            Page? sectionIndex = null;
            if (children.Count > 0 && children[0].Page is { } first && IsIndexPage(first.RelativePath))
            {
                sectionIndex = first;
                children.RemoveAt(0);
            }

            return new NavNode { Title = item.Title ?? "", Children = children, SectionIndex = sectionIndex, Icon = item.Icon ?? (sectionIndex is not null ? PageIcon(sectionIndex) : null) };
        }

        return item.Path is null && item.Title is not null
            ? new NavNode { Title = item.Title, Icon = item.Icon }
            : null;
    }

    /// <summary>A page can declare a nav icon via <c>icon:</c> front matter (mkdocs-material style).</summary>
    private static string? PageIcon(Page page) =>
        page.FrontMatter.TryGetValue("icon", out var v) && v is string s && s.Length > 0 ? s : null;

    private static bool IsIndexPage(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        return name.Equals("index", StringComparison.OrdinalIgnoreCase)
            || name.Equals("README", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a hierarchical nav from the pages' directory structure when no nav is
    /// authored — mirroring MkDocs' default behaviour. Each folder becomes a section
    /// (titleised from its name), an <c>index.md</c>/<c>README.md</c> becomes that
    /// section's landing page, and entries are ordered alphabetically with index pages
    /// first. A flat list (the previous behaviour) dumped every page into one level,
    /// which for large sites overflowed the header nav.
    /// </summary>
    private static IReadOnlyList<NavNode> AutoNav(IReadOnlyList<Page> pages)
    {
        var root = new Dir("");
        foreach (var page in pages)
        {
            var parts = page.RelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dir = root;
            for (var i = 0; i < parts.Length - 1; i++)
                dir = dir.Sub(parts[i]);
            dir.Pages.Add(page);
        }

        var nodes = BuildLevel(root).ToList();

        // A top-level index/README page becomes a plain landing link (e.g. "Home"),
        // not a section, so it isn't swallowed by BuildLevel's index handling.
        var home = root.Pages.FirstOrDefault(p => IsIndexPage(p.RelativePath));
        if (home is not null)
            nodes.Insert(0, new NavNode { Title = home.Title.Length > 0 ? home.Title : "Home", Page = home, Icon = PageIcon(home) });

        return nodes;
    }

    /// <summary>Convert a directory's sub-folders and (non-index) pages into nav nodes,
    /// interleaved and ordered alphabetically by name.</summary>
    private static List<NavNode> BuildLevel(Dir dir)
    {
        var entries = new List<(string Key, NavNode Node)>();

        foreach (var sub in dir.Subs.Values)
        {
            var children = BuildLevel(sub);
            var index = sub.Pages.FirstOrDefault(p => IsIndexPage(p.RelativePath));
            var section = new NavNode
            {
                Title = Titleize(sub.Name),
                Children = children,
                SectionIndex = index,
                Icon = index is not null ? PageIcon(index) : null,
            };
            entries.Add((sub.Name, section));
        }

        foreach (var page in dir.Pages.Where(p => !IsIndexPage(p.RelativePath)))
        {
            var key = Path.GetFileNameWithoutExtension(page.RelativePath);
            entries.Add((key, new NavNode { Title = page.Title, Page = page, Icon = PageIcon(page) }));
        }

        return entries
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Node)
            .ToList();
    }

    /// <summary>Turn a folder/file slug into a human-readable section title
    /// (e.g. "account-management" -> "Account Management").</summary>
    private static string Titleize(string slug)
    {
        var words = slug.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        return words.Length > 0 ? string.Join(' ', words) : slug;
    }

    /// <summary>A node in the directory tree used to build the auto-nav.</summary>
    private sealed class Dir(string name)
    {
        public string Name { get; } = name;
        public SortedDictionary<string, Dir> Subs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Page> Pages { get; } = [];

        public Dir Sub(string childName)
        {
            if (!Subs.TryGetValue(childName, out var d))
                Subs[childName] = d = new Dir(childName);
            return d;
        }
    }

    /// <summary>Depth-first flatten of the nav tree to the linear order of real pages.</summary>
    public static List<Page> Flatten(IReadOnlyList<NavNode> nodes)
    {
        var result = new List<Page>();
        void Walk(IReadOnlyList<NavNode> ns)
        {
            foreach (var node in ns)
            {
                if (node.SectionIndex is not null) result.Add(node.SectionIndex);
                if (node.Page is not null) result.Add(node.Page);
                if (node.Children.Count > 0) Walk(node.Children);
            }
        }
        Walk(nodes);
        return result;
    }
}
