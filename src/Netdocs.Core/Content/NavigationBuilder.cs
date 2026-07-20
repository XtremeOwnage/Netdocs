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
            return new NavNode { Title = item.Title ?? page.Title, Page = page };

        if (item.Children.Count > 0)
        {
            var children = item.Children.Select(c => Resolve(c, byRelative)).Where(n => n is not null).Select(n => n!).ToList();
            return new NavNode { Title = item.Title ?? "", Children = children };
        }

        return item.Path is null && item.Title is not null
            ? new NavNode { Title = item.Title }
            : null;
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
                if (node.Page is not null) result.Add(node.Page);
                if (node.Children.Count > 0) Walk(node.Children);
            }
        }
        Walk(nodes);
        return result;
    }
}
