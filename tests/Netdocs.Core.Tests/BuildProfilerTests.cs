using Netdocs.Core.Diagnostics;
using Xunit;

namespace Netdocs.Core.Tests;

public class BuildProfilerTests
{
    [Fact]
    public void Measure_NestsChildScopes_UnderTheActiveScope()
    {
        var p = new BuildProfiler();
        using (p.Measure("phase"))
        {
            using (p.Measure("plugin-a")) { }
            using (p.Measure("plugin-b")) { }
        }

        var report = p.Render();
        Assert.Contains("phase", report);
        Assert.Contains("plugin-a", report);
        Assert.Contains("plugin-b", report);
        // Children are indented deeper than their parent phase.
        var phaseIndent = report.IndexOf("phase", System.StringComparison.Ordinal);
        var childIndent = report.IndexOf("plugin-a", System.StringComparison.Ordinal);
        Assert.True(LineIndent(report, "plugin-a") > LineIndent(report, "phase"));
        Assert.True(childIndent > phaseIndent);
    }

    [Fact]
    public void Measure_SameNameUnderSameParent_AccumulatesCount()
    {
        var p = new BuildProfiler();
        using (p.Measure("phase"))
        {
            using (p.Measure("plugin")) { }
            using (p.Measure("plugin")) { }
            using (p.Measure("plugin")) { }
        }

        Assert.Contains("x3", p.Render());
    }

    [Fact]
    public void Render_EmptyProfiler_DoesNotThrow()
    {
        var report = new BuildProfiler().Render();
        Assert.Contains("Build profile", report);
    }

    private static int LineIndent(string report, string token)
    {
        foreach (var line in report.Split('\n'))
            if (line.Contains(token))
                return line.Length - line.TrimStart().Length;
        return -1;
    }
}
