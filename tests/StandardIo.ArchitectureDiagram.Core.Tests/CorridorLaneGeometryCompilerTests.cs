using System;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CorridorLaneGeometryCompilerTests
{
    [Fact]
    public void Compile_moves_complete_internal_segment_to_allocated_lane()
    {
        var link = new LinkLayout(
            new RenderLink("edge", "source", "target", "internal", 0),
            new Point(20, 0),
            new Point(100, 100),
            new[] { new Point(20, 20), new Point(100, 20) },
            0.5,
            0.5);
        var corridor = new RoutingCorridor(
            "H:20:80",
            CorridorOrientation.Horizontal,
            new Rect(0, 20, 200, 60),
            12,
            3);
        var mapping = new CorridorSegmentMapping(
            "edge",
            1,
            corridor.Id,
            new Segment(new Point(20, 20), new Point(100, 20)));
        var observation = new CorridorObservation(
            new Dictionary<string, RoutingCorridor> { [corridor.Id] = corridor },
            new Dictionary<string, CorridorJunction>(),
            new[] { mapping },
            new Dictionary<string, CorridorUsage>());
        var lane = new AllocatedCorridorLane(corridor.Id, "edge", 1, 50);
        var allocation = new CorridorLaneAllocation(
            new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>
            {
                [corridor.Id] = new Dictionary<string, AllocatedCorridorLane> { ["edge"] = lane }
            },
            Array.Empty<string>());

        var result = CorridorLaneGeometryCompiler.Compile(
            new Dictionary<string, LinkLayout> { ["edge"] = link },
            observation,
            allocation);

        Assert.Equal(new Point(20, 50), result["edge"].Points[0]);
        Assert.Equal(new Point(100, 50), result["edge"].Points[1]);
        Assert.Equal(new Point(20, 0), result["edge"].SourcePoint);
        Assert.Equal(new Point(100, 100), result["edge"].TargetPoint);
    }

    [Fact]
    public void Compile_keeps_three_corridor_routes_visibly_separate()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge_a"] = Link("edge_a", 0),
            ["edge_b"] = Link("edge_b", 1),
            ["edge_c"] = Link("edge_c", 2)
        };
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["top"] = Node("top", new Rect(0, 0, 120, 20)),
            ["bottom"] = Node("bottom", new Rect(0, 80, 120, 20))
        };
        var observation = CorridorObserver.Observe(nodes, links, 12, 4);
        var allocation = CorridorLaneAllocator.Allocate(observation);

        var result = CorridorLaneGeometryCompiler.Compile(links, observation, allocation);
        var coordinates = new[]
        {
            result["edge_a"].Points[0].Y,
            result["edge_b"].Points[0].Y,
            result["edge_c"].Points[0].Y
        };

        Assert.Equal(3, new HashSet<int>(coordinates).Count);
        Array.Sort(coordinates);
        Assert.True(coordinates[1] - coordinates[0] >= 12);
        Assert.True(coordinates[2] - coordinates[1] >= 12);
    }

    private static LinkLayout Link(string id, int order) =>
        new(
            new RenderLink(id, $"{id}_source", $"{id}_target", "internal", order),
            new Point(20, 0),
            new Point(100, 100),
            new[] { new Point(20, 50), new Point(100, 50) },
            0.5,
            0.5);

    private static NodeLayout Node(string id, Rect rect) =>
        new(
            new RenderNode(
                id,
                null,
                id,
                id,
                "Class",
                false,
                string.Empty,
                0,
                Array.Empty<string>(),
                Array.Empty<StandardIo.ArchitectureDiagram.Core.Models.TypeProperty>(),
                0),
            rect,
            0,
            false);
}
