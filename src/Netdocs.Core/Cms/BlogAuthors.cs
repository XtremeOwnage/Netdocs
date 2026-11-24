using Netdocs.Core.Configuration;

namespace Netdocs.Core.Cms;

/// <summary>A blog author from a <c>.authors.yml</c> file.</summary>
public sealed record BlogAuthor(string Id, string Name, string Description, string Avatar);

/// <summary>Reads the blog authors the site defines, for the editor's author picker.</summary>
public static class BlogAuthors
{
    public static IReadOnlyList<BlogAuthor> List(CmsWorkspace workspace)
    {
        var result = new List<BlogAuthor>();
        if (!Directory.Exists(workspace.DocsDir)) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(workspace.DocsDir, ".authors.yml", SearchOption.AllDirectories))
        {
            try
            {
                var root = YamlTree.Parse(File.ReadAllText(file)).AsMap();
                foreach (var (id, value) in root.Get("authors").AsMap())
                {
                    if (!seen.Add(id)) continue;
                    var a = value.AsMap();
                    result.Add(new BlogAuthor(id, a.Get("name").AsString() ?? id, a.Get("description").AsString() ?? "", a.Get("avatar").AsString() ?? ""));
                }
            }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException)
            {
                // A broken authors file shows no suggestions rather than failing the editor.
            }
        }
        return result;
    }
}
