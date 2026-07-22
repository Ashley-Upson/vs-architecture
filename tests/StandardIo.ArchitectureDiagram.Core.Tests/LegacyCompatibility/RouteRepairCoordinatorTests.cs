using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class RouteRepairCoordinatorTests
{
    [Fact]
    public void Repair_reroutes_compact_node_intersection()
    {
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["source"] = Node("source", new Rect(0, 40, 20, 20)),
            ["obstacle"] = Node("obstacle", new Rect(50, 35, 30, 30)),
            ["target"] = Node("target", new Rect(110, 40, 20, 20))
        };
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge"] = Link("edge", "source", "target", new Point(20, 50), new Point(110, 50)),
            ["unrelated"] = Link("unrelated", "far_source", "far_target", new Point(1000, 500), new Point(1100, 500), 1)
        };

        var result = RouteRepairCoordinator.Repair(nodes, links, DiagramSettings.CreateDefault());

        Assert.Contains(result.PreRepairValidation.Violations, x => x.Code == TraceabilityViolationCode.NodeCollision);
        Assert.DoesNotContain(result.PostRepairValidation.Violations, x => x.Code == TraceabilityViolationCode.NodeCollision);
        Assert.Contains(result.Attempts, x => x.FindingCategory == "NodeInteriorIntersection" && x.Applied);
        Assert.True(result.RoutesInvalidated < result.EstimatedWorkUsed * links.Count);
        Assert.Equal(0, result.RoutePairsRevalidated);
        Assert.True(result.CorridorRebuildCount >= 2);
    }

    [Fact]
    public void Repair_separates_shared_routes_independent_of_input_order()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge_a"] = Link("edge_a", "a", "b", new Point(0, 20), new Point(100, 20), 0),
            ["edge_b"] = Link("edge_b", "c", "d", new Point(0, 20), new Point(100, 20), 1)
        };

        var forward = RouteRepairCoordinator.Repair(new Dictionary<string, NodeLayout>(), links, DiagramSettings.CreateDefault());
        var reversed = RouteRepairCoordinator.Repair(new Dictionary<string, NodeLayout>(),
            links.Reverse().ToDictionary(x => x.Key, x => x.Value), DiagramSettings.CreateDefault());

        Assert.DoesNotContain(forward.PostRepairValidation.Violations, x => x.Code == TraceabilityViolationCode.SharedSegment);
        Assert.Equal(Points(forward.Links["edge_a"]), Points(reversed.Links["edge_a"]));
        Assert.Equal(Points(forward.Links["edge_b"]), Points(reversed.Links["edge_b"]));
    }

    [Fact]
    public void Repair_retains_advisory_when_budget_is_exhausted()
    {
        var nodes = new Dictionary<string, NodeLayout> { ["obstacle"] = Node("obstacle", new Rect(40, 40, 40, 40)) };
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge"] = Link("edge", "source", "target", new Point(0, 60), new Point(100, 60))
        };

        var result = RouteRepairCoordinator.Repair(nodes, links, DiagramSettings.CreateDefault(),
            new RouteRepairBudget(MaximumEstimatedWork: 0));

        Assert.True(result.WorkBudgetExhausted);
        Assert.Contains(result.PostRepairValidation.Violations, x => x.Code == TraceabilityViolationCode.NodeCollision);
    }

    [Fact]
    public void Adaptive_capacity_moves_only_affected_downstream_layers()
    {
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["source"] = Node("source", new Rect(0, 0, 20, 20)) with { Depth = 0 },
            ["target"] = Node("target", new Rect(0, 100, 20, 20)) with { Depth = 1 },
            ["downstream"] = Node("downstream", new Rect(0, 200, 20, 20)) with { Depth = 2 }
        };
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge"] = Link("edge", "source", "target", new Point(10, 20), new Point(10, 100))
        };
        var validation = new TraceabilityValidationResult(new[]
        {
            new TraceabilityViolation(TraceabilityViolationCode.ParallelSpacing, "edge", "other", 5, "spacing")
        });

        var result = RenderLayout.ExpandLayersForLaneDemand(nodes, links, validation, DiagramSettings.CreateDefault());

        Assert.True(result.Changed);
        Assert.Equal(0, result.Nodes["source"].Rect.Y);
        Assert.Equal(105, result.Nodes["target"].Rect.Y);
        Assert.Equal(205, result.Nodes["downstream"].Rect.Y);
        Assert.Contains(result.Attempts, attempt => attempt.FindingCategory == "AdaptiveLayerSpacing");
    }

    private static NodeLayout Node(string id, Rect rect) => new(
        new RenderNode(id, null, id, id, "Class", false, string.Empty, 0,
            Array.Empty<string>(), Array.Empty<TypeProperty>(), 0), rect, 0, false);

    private static LinkLayout Link(string id, string source, string target, Point start, Point end, int order = 0) =>
        new(new RenderLink(id, source, target, "internal", order), start, end, Array.Empty<Point>(), .5, .5);

    private static Point[] Points(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
}
