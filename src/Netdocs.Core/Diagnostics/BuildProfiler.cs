using System.Diagnostics;
using System.Text;

namespace Netdocs.Core.Diagnostics;

/// <summary>
/// Lightweight hierarchical timer used by <c>netdocs profile</c>. Phases and plugins wrap their
/// work in <see cref="Measure"/> scopes; the collected tree is rendered as a console breakdown of
/// where build time is spent. Nesting relies on the ambient scope stack, so <see cref="Measure"/>
/// must only be called from the sequential parts of the build (wrap parallel loops as one scope).
/// </summary>
public sealed class BuildProfiler
{
    private readonly ProfileNode _root = new("build");
    private readonly Stack<ProfileNode> _stack = new();

    public BuildProfiler() => _stack.Push(_root);

    /// <summary>Starts a timing scope for <paramref name="name"/> nested under the active scope.
    /// Repeated scopes with the same name under the same parent accumulate their time and count.</summary>
    public IDisposable Measure(string name)
    {
        var parent = _stack.Peek();
        var node = parent.GetOrAddChild(name);
        _stack.Push(node);
        return new Scope(this, node);
    }

    private void Pop(ProfileNode node, long elapsedTicks)
    {
        node.Add(elapsedTicks);
        // Defensively unwind to the node we started, tolerating unbalanced disposal order.
        while (_stack.Count > 1 && _stack.Peek() != node) _stack.Pop();
        if (_stack.Count > 1) _stack.Pop();
    }

    /// <summary>Renders the collected timings as an indented tree, biggest consumers first.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        // The root is never itself measured; wall time is approximated by the sum of the
        // sequential top-level phases. Percentages are always relative to the parent scope.
        var totalMs = _root.Children.Sum(c => c.ElapsedMs);
        sb.AppendLine("Build profile (time by phase):");
        sb.AppendLine();
        foreach (var child in _root.Children.OrderByDescending(c => c.ElapsedTicks))
            RenderNode(sb, child, 1, totalMs);
        sb.AppendLine();
        sb.AppendLine($"Total measured: {totalMs:0.0} ms");
        return sb.ToString();
    }

    private static void RenderNode(StringBuilder sb, ProfileNode node, int depth, double parentMs)
    {
        var indent = new string(' ', depth * 2);
        var pct = parentMs > 0 ? node.ElapsedMs / parentMs * 100.0 : 0;
        var count = node.Count > 1 ? $" x{node.Count}" : "";
        sb.AppendLine($"{indent}{node.Name,-38} {node.ElapsedMs,8:0.0} ms  {pct,5:0.0}%{count}");
        foreach (var child in node.Children.OrderByDescending(c => c.ElapsedTicks))
            RenderNode(sb, child, depth + 1, node.ElapsedMs);
    }

    private sealed class Scope(BuildProfiler owner, ProfileNode node) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner.Pop(node, Stopwatch.GetTimestamp() - _start);
        }
    }

    private sealed class ProfileNode(string name)
    {
        private readonly Dictionary<string, ProfileNode> _index = new(StringComparer.Ordinal);
        private readonly List<ProfileNode> _children = [];
        public string Name { get; } = name;
        public long ElapsedTicks { get; private set; }
        public int Count { get; private set; }
        public IReadOnlyList<ProfileNode> Children => _children;
        public double ElapsedMs => ElapsedTicks * 1000.0 / Stopwatch.Frequency;

        public ProfileNode GetOrAddChild(string childName)
        {
            if (_index.TryGetValue(childName, out var existing)) return existing;
            var node = new ProfileNode(childName);
            _index[childName] = node;
            _children.Add(node);
            return node;
        }

        public void Add(long ticks) { ElapsedTicks += ticks; Count++; }
    }
}
