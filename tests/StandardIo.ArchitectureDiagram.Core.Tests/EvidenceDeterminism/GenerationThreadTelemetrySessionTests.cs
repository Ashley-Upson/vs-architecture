using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class GenerationThreadTelemetrySessionTests
{
    [Fact]
    public void Performance_phase_records_thread_identity_and_main_thread_state()
    {
        var current = Environment.CurrentManagedThreadId;
        using var session = GenerationThreadTelemetrySession.Start(() => true);

        using (PerformanceAudit.Measure("fixture"))
        {
            session.Mark("point");
        }

        var metrics = session.Snapshot();
        Assert.Equal(2, metrics.Count);
        var point = metrics.Single(metric => metric.Stage == "point");
        var phase = metrics.Single(metric => metric.Stage == "fixture");
        Assert.Equal("point", point.Stage);
        Assert.Equal(current, point.StartManagedThreadId);
        Assert.True(point.StartIsMainThread);
        Assert.Equal(current, phase.EndManagedThreadId);
    }
}
