using System;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CorridorModelsTests
{
    [Fact]
    public void Usage_reports_remaining_and_exceeded_capacity()
    {
        var corridor = new RoutingCorridor(
            "H:100:160",
            CorridorOrientation.Horizontal,
            new Rect(0, 100, 400, 60),
            12,
            3);

        var available = new CorridorUsage(corridor, new[] { "a", "b" }, 2);
        var exceeded = new CorridorUsage(corridor, new[] { "a", "b", "c", "d" }, 4);

        Assert.Equal(1, available.RemainingCapacity);
        Assert.False(available.IsOverCapacity);
        Assert.Equal(-1, exceeded.RemainingCapacity);
        Assert.True(exceeded.IsOverCapacity);
    }

    [Fact]
    public void Junction_identifies_connected_corridors()
    {
        var junction = new CorridorJunction(
            "J:H:100:160:V:200:260",
            new Rect(200, 100, 60, 60),
            new[] { "H:100:160", "V:200:260" });

        Assert.Equal(2, junction.CorridorIds.Count);
    }
}
