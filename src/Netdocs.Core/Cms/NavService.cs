using System.Text.Json.Nodes;

namespace Netdocs.Core.Cms;

/// <summary>Reads and writes the <c>nav</c> array of <c>appsettings.json</c>, leaving the rest of
/// the file as it was.</summary>
public sealed class NavService(CmsWorkspace workspace)
{
    public JsonArray Read()
    {
        var json = File.ReadAllText(workspace.ConfigPath);
        return JsonSpanEditor.Get(json, "Netdocs", "nav") as JsonArray ?? [];
    }

    public void Write(JsonArray nav)
    {
        Validate(nav, "nav");
        var json = File.ReadAllText(workspace.ConfigPath);
        var updated = JsonSpanEditor.Set(json, nav.Count == 0 ? null : nav, "Netdocs", "nav");
        CmsWorkspace.WriteText(workspace.ConfigPath, updated);
        workspace.Reload();
    }

    /// <summary>Every page path listed in the nav.</summary>
    public HashSet<string> ListedPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(Read(), node =>
        {
            if (node["path"]?.GetValue<string>() is { } p) set.Add(p.Replace('\\', '/'));
        });
        return set;
    }

    /// <summary>Renames a page path everywhere it appears in the nav. Returns true when changed.</summary>
    public bool RenamePath(string from, string to)
    {
        var nav = Read();
        var changed = false;
        Walk(nav, node =>
        {
            if (node["path"]?.GetValue<string>() is { } p && p.Replace('\\', '/').Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                node["path"] = to;
                changed = true;
            }
        });
        if (changed) Write(nav);
        return changed;
    }

    /// <summary>Removes a page from the nav. Returns true when changed.</summary>
    public bool RemovePath(string path)
    {
        var nav = Read();
        var changed = RemoveFrom(nav, path);
        if (changed) Write(nav);
        return changed;
    }

    private static bool RemoveFrom(JsonArray array, string path)
    {
        var changed = false;
        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (array[i] is not JsonObject node) continue;
            if (node["path"]?.GetValue<string>() is { } p && p.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase) && node["children"] is null)
            {
                array.RemoveAt(i);
                changed = true;
                continue;
            }
            if (node["children"] is JsonArray children) changed |= RemoveFrom(children, path);
        }
        return changed;
    }

    private static void Walk(JsonArray array, Action<JsonObject> visit)
    {
        foreach (var item in array)
        {
            if (item is not JsonObject node) continue;
            visit(node);
            if (node["children"] is JsonArray children) Walk(children, visit);
        }
    }

    private static void Validate(JsonArray array, string where)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject node) throw new CmsException($"{where}[{i}] must be an object.");
            var hasPath = node["path"] is JsonValue;
            var hasChildren = node["children"] is JsonArray;
            if (!hasPath && !hasChildren)
                throw new CmsException($"Nav entry '{node["title"]?.ToString() ?? $"#{i + 1}"}' needs a page or child entries.");
            if (hasChildren) Validate((JsonArray)node["children"]!, $"{where}[{i}].children");
        }
    }
}
