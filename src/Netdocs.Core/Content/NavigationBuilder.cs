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

    private static IReadOnlyList<NavNode> AutoNav(IReadOnlyList<Page> pages)
    {
        return pages
            .OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(p => new NavNode { Title = p.Title, Page = p })
            .ToList();
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
