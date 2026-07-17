using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CorridorLaneAllocatorTests
{
    [Fact]
    public void Allocate_assigns_stable_separated_lanes()
    {
        var observation = Observation(capacity: 3, "edge_c", "edge_a", "edge_b");

        var allocation = CorridorLaneAllocator.Allocate(observation);

        Assert.True(allocation.IsSuccessful);
        var lanes = allocation.Corridors["H:20:80"].Values
            .OrderBy(lane => lane.LaneIndex)
            .ToArray();
        Assert.Equal(new[] { "edge_c", "edge_a", "edge_b" }, lanes.Select(lane => lane.EdgeId));
        Assert.Equal(new[] { 38, 50, 62 }, lanes.Select(lane => lane.Coordinate));
    }

    [Fact]
    public void Allocate_diagnoses_capacity_failure_without_reusing_lanes()
    {
        var observation = Observation(capacity: 2, "edge_a", "edge_b", "edge_c");

        var allocation = CorridorLaneAllocator.Allocate(observation);

        Assert.False(allocation.IsSuccessful);
        Assert.Equal(new[] { "H:20:80" }, allocation.FailedCorridorIds);
        Assert.False(allocation.Corridors.ContainsKey("H:20:80"));
    }

    private static CorridorObservation Observation(int capacity, params string[] edges)
    {
        var corridor = new RoutingCorridor(
            "H:20:80",
            CorridorOrientation.Horizontal,
            new Rect(0, 20, 200, 60),
            12,
            capacity);
        var usage = new CorridorUsage(corridor, edges, edges.Length);
        return new CorridorObservation(
            new Dictionary<string, RoutingCorridor> { [corridor.Id] = corridor },
            new Dictionary<string, CorridorJunction>(),
            Array.Empty<CorridorSegmentMapping>(),
            new Dictionary<string, CorridorUsage> { [corridor.Id] = usage });
    }
}
