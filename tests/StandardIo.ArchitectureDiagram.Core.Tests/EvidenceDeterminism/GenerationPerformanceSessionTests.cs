using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class GenerationPerformanceSessionTests
{
    [Fact]
    public void Snapshot_distinguishes_nested_inclusive_and_exclusive_work_and_invocations()
    {
        using var session = GenerationPerformanceSession.Start();
        using (session.Measure("outer", inputNodes: 3, layoutRevision: 2))
        {
            using (session.Measure("inner", inputRoutes: 4, routeRevision: 5))
            {
                session.Increment("operation", 7);
            }

            using (session.Measure("inner", inputRoutes: 4, routeRevision: 5))
            {
                session.Increment("operation", 2);
            }
        }

        var report = session.Snapshot();
        var outer = Assert.Single(report.Phases, phase => phase.Path == "outer");
        var inner = Assert.Single(report.Phases, phase => phase.Path == "outer > inner");

        Assert.Equal(1, outer.InvocationCount);
        Assert.Equal(2, inner.InvocationCount);
        Assert.Equal(3, outer.InputNodes);
        Assert.Equal(8, inner.InputRoutes);
        Assert.True(outer.InclusiveMicroseconds >= outer.ExclusiveMicroseconds);
        Assert.Equal(9, Assert.Single(report.Counters, counter => counter.Name == "operation").Value);
    }

    [Fact]
    public void Start_rejects_overlapping_sessions()
    {
        using var session = GenerationPerformanceSession.Start();

        Assert.Throws<InvalidOperationException>(() => GenerationPerformanceSession.Start());
    }
}
