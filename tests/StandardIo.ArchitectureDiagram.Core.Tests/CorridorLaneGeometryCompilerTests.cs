using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void Compile_intersects_adjacent_allocated_lanes_without_diagonal_corrections()
    {
        var link = new LinkLayout(
            new RenderLink("edge", "source", "target", "internal", 0),
            new Point(20, 0), new Point(100, 100),
            new[] { new Point(20, 20), new Point(80, 20), new Point(80, 80), new Point(100, 80) },
            0.5, 0.5);
        var horizontal = new RoutingCorridor("H:0:40:20:80", CorridorOrientation.Horizontal, new Rect(20, 0, 60, 40), 12, 2);
        var vertical = new RoutingCorridor("V:60:100:20:80", CorridorOrientation.Vertical, new Rect(60, 20, 40, 60), 12, 2);
        var observation = new CorridorObservation(
            new Dictionary<string, RoutingCorridor> { [horizontal.Id] = horizontal, [vertical.Id] = vertical },
            new Dictionary<string, CorridorJunction>(),
            new[]
            {
                new CorridorSegmentMapping("edge", 1, horizontal.Id, new Segment(new Point(20, 20), new Point(80, 20))),
                new CorridorSegmentMapping("edge", 2, vertical.Id, new Segment(new Point(80, 20), new Point(80, 80)))
            },
            new Dictionary<string, CorridorUsage>());
        var allocation = new CorridorLaneAllocation(
            new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>
            {
                [horizontal.Id] = new Dictionary<string, AllocatedCorridorLane> { ["edge"] = new(horizontal.Id, "edge", 0, 30) },
                [vertical.Id] = new Dictionary<string, AllocatedCorridorLane> { ["edge"] = new(vertical.Id, "edge", 0, 90) }
            },
            Array.Empty<string>());

        var result = CorridorLaneGeometryCompiler.Compile(
            new Dictionary<string, LinkLayout> { ["edge"] = link }, observation, allocation)["edge"];

        Assert.Equal(new[] { new Point(20, 30), new Point(90, 30), new Point(90, 80), new Point(100, 80) }, result.Points);
        Assert.All(CompleteSegments(result), segment => Assert.True(segment.IsHorizontal || segment.IsVertical));
    }

    private static IEnumerable<Segment> CompleteSegments(LinkLayout link)
    {
        var points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
        return Enumerable.Range(0, points.Length - 1).Select(index => new Segment(points[index], points[index + 1]));
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
