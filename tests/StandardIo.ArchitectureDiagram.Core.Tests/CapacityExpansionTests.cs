using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CapacityExpansionTests
{
    [Fact]
    public void Capacity_request_expands_affected_downstream_layer_by_smallest_required_amount()
    {
        var source = Node("source", 0, new Rect(0, 0, 100, 40));
        var target = Node("target", 1, new Rect(0, 80, 100, 40));
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["source"] = source,
            ["target"] = target
        };
        var link = new LinkLayout(
            new RenderLink("edge", "source", "target", "internal", 0),
            new Point(50, 40),
            new Point(50, 80),
            Array.Empty<Point>(),
            0.5,
            0.5);
        var corridor = new RoutingCorridor(
            "H:40:64:Ordinary:local:0:1:50:100",
            CorridorOrientation.Horizontal,
            new Rect(0, 40, 100, 24),
            12,
            1,
            Clearance: 10);
        var observation = new CorridorObservation(
            new Dictionary<string, RoutingCorridor> { [corridor.Id] = corridor },
            new Dictionary<string, CorridorJunction>(),
            new[] { new CorridorSegmentMapping("edge", 0, corridor.Id, new Segment(new Point(0, 50), new Point(100, 50)), 0) },
            new Dictionary<string, CorridorUsage>());
        var request = new CapacityRequest(
            corridor.Id,
            CorridorRole.Ordinary,
            new Dictionary<string, int> { ["edge"] = 0 },
            3,
            1,
            48,
            corridor.Bounds,
            24);
        var allocation = new CorridorLaneAllocation(
            new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>(),
            new[] { corridor.Id },
            new[] { request });

        var result = RenderLayout.ExpandLayersForCapacityRequests(
            nodes,
            new Dictionary<string, LinkLayout> { ["edge"] = link },
            observation,
            allocation,
            DiagramSettings.CreateDefault());

        Assert.True(result.Changed);
        Assert.Equal(source.Rect, result.Nodes["source"].Rect);
        Assert.Equal(target.Rect.Y + 24, result.Nodes["target"].Rect.Y);
        Assert.Contains(result.Attempts, attempt => attempt.Applied && attempt.FindingCategory == "CapacityRequest");
    }

    private static NodeLayout Node(string id, int depth, Rect rect) =>
        new(
            new RenderNode(id, "project", id, $"Fixture.{id}", "Class", false, string.Empty, depth,
                Array.Empty<string>(), Array.Empty<TypeProperty>(), 0),
            rect,
            depth,
            false);
}
