using System;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CorridorObserverTests
{
    [Fact]
    public void Observe_maps_parallel_routes_to_shared_corridor_usage()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["a"] = Link("a", 0, 40),
            ["b"] = Link("b", 1, 50),
            ["c"] = Link("c", 2, 60)
        };
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["top"] = Node("top", new Rect(0, 0, 120, 20)),
            ["bottom"] = Node("bottom", new Rect(0, 80, 120, 20))
        };

        var observation = CorridorObserver.Observe(
            nodes,
            links,
            laneSpacing: 12,
            clearance: 4);

        var horizontalUsage = Assert.Single(observation.Usage.Values);
        Assert.Equal(CorridorOrientation.Horizontal, horizontalUsage.Corridor.Orientation);
        Assert.Equal(3, horizontalUsage.RequiredLanes);
        Assert.Equal(new[] { "a", "b", "c" }, horizontalUsage.EdgeIds);
    }

    [Fact]
    public void Observe_creates_junction_for_intersecting_corridors()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["horizontal"] = Link("horizontal", 0, 50),
            ["vertical"] = new LinkLayout(
                new RenderLink("vertical", "vertical_source", "vertical_target", "internal", 1),
                new Point(60, 20),
                new Point(60, 100),
                Array.Empty<Point>(),
                0.5,
                0.5)
        };

        var observation = CorridorObserver.Observe(
            new Dictionary<string, NodeLayout>(),
            links,
            laneSpacing: 12,
            clearance: 4);

        Assert.Single(observation.Junctions);
    }

    [Fact]
    public void Observe_does_not_merge_disconnected_segments_in_same_axis_band()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["left"] = LinkBetween("left", 0, 20, 100, 50),
            ["right"] = LinkBetween("right", 1, 500, 580, 50)
        };

        var observation = CorridorObserver.Observe(
            new Dictionary<string, NodeLayout>(), links, laneSpacing: 12, clearance: 4);

        Assert.Equal(2, observation.Corridors.Count);
        Assert.All(observation.Usage.Values, usage => Assert.Equal(1, usage.RequiredLanes));
    }

    [Fact]
    public void Observe_keeps_local_transition_and_cross_branch_highway_in_distinct_corridor_classes()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["local"] = RoutedLink("local", 0, 20, 100),
            ["cross"] = RoutedLink("cross", 1, 20, 500)
        };
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["local_source"] = Node("local_source", new Rect(-30, 0, 100, 20)),
            ["local_target"] = Node("local_target", new Rect(50, 80, 100, 20)),
            ["cross_source"] = Node("cross_source", new Rect(-30, 0, 100, 20)),
            ["cross_target"] = Node("cross_target", new Rect(450, 80, 100, 20))
        };

        var observation = CorridorObserver.Observe(nodes, links, laneSpacing: 12, clearance: 4);

        var ordinary = observation.Corridors.Values.Where(corridor =>
            corridor.Role == CorridorRole.Ordinary && corridor.Orientation == CorridorOrientation.Horizontal).ToArray();
        Assert.Equal(2, ordinary.Length);
        Assert.Contains(ordinary, corridor => corridor.RegionKey.StartsWith("local:"));
        Assert.Contains(ordinary, corridor => corridor.RegionKey.StartsWith("cross:"));
    }

    [Fact]
    public void Observe_models_ordered_terminal_transition_separately_from_ordinary_corridor()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["near"] = new(
                new RenderLink("near", "source", "near_target", "internal", 0),
                new Point(50, 20), new Point(140, 100),
                new[] { new Point(50, 40), new Point(140, 40) }, 0.5, 0.5),
            ["far"] = new(
                new RenderLink("far", "source", "far_target", "internal", 1),
                new Point(60, 20), new Point(300, 100),
                new[] { new Point(60, 52), new Point(300, 52) }, 0.6, 0.5)
        };
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["source"] = Node("source", new Rect(0, 0, 100, 20)),
            ["near_target"] = Node("near_target", new Rect(90, 100, 100, 20)),
            ["far_target"] = Node("far_target", new Rect(250, 100, 100, 20))
        };

        var observation = CorridorObserver.Observe(nodes, links, 12, 4);
        var sourceTransitions = observation.TerminalTransitions!
            .Where(item => item.Direction == FanoutDirection.Source)
            .OrderBy(item => item.LaneOrder)
            .ToArray();

        Assert.Equal(new[] { "near", "far" }, sourceTransitions.Select(item => item.EdgeId));
        Assert.All(sourceTransitions, transition => Assert.Equal(12, transition.RequiredSpread));
        Assert.Contains(observation.Corridors.Values, corridor => corridor.Role == CorridorRole.SourceTransition);
    }

    private static LinkLayout Link(string id, int order, int y) =>
        LinkBetween(id, order, 20, 100, y);

    private static LinkLayout RoutedLink(string id, int order, int left, int right) =>
        new(
            new RenderLink(id, $"{id}_source", $"{id}_target", "internal", order),
            new Point(left, 0),
            new Point(right, 100),
            new[]
            {
                new Point(left, 10),
                new Point(left, 50),
                new Point(right, 50),
                new Point(right, 90)
            },
            0.5,
            0.5);

    private static LinkLayout LinkBetween(string id, int order, int left, int right, int y) =>
        new(
            new RenderLink(id, $"{id}_source", $"{id}_target", "internal", order),
            new Point(left, y),
            new Point(right, y),
            Array.Empty<Point>(),
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
