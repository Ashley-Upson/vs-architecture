using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class EdgeTraversalCompilerTests
{
    [Fact]
    public void Compile_round_trips_terminal_access_corridors_and_junctions()
    {
        var link = TurnLink("edge", 0, 0);
        var context = CreateTurnContext(new[] { link }, includeJunction: true);

        var result = EdgeTraversalCompiler.Compile(Links(link), context.Observation, context.Allocation);

        var traversal = result.Traversals["edge"];
        Assert.False(traversal.UsesFallback);
        Assert.Equal(link.SourcePoint, traversal.SourceAccess.Terminal);
        Assert.Equal(link.TargetPoint, traversal.TargetAccess.Terminal);
        Assert.Equal(2, traversal.Corridors.Count);
        Assert.Single(traversal.Junctions);
        Assert.Equal(CompletePoints(link), result.Geometry["edge"].Points);
    }

    [Fact]
    public void Compile_preserves_lane_order_for_three_parallel_turns()
    {
        var links = new[] { TurnLink("a", 0, 0), TurnLink("b", 1, 10), TurnLink("c", 2, 20) };
        var context = CreateTurnContext(links, includeJunction: true);

        var result = EdgeTraversalCompiler.Compile(Links(links), context.Observation, context.Allocation);

        Assert.Equal(new[] { 0, 1, 2 }, links.Select(link => result.Traversals[link.Link.Id].Corridors[0].Lane.LaneIndex));
        Assert.Equal(new[] { 0, 1, 2 }, links.Select(link => result.Traversals[link.Link.Id].Corridors[1].Lane.LaneIndex));
        Assert.Equal(3, links.Select(link => result.Traversals[link.Link.Id].Junctions[0].TransitionPoint).Distinct().Count());
        Assert.All(result.Diagnostics, diagnostic => Assert.NotEqual("UNSUPPORTED_JUNCTION_TOPOLOGY", diagnostic.Code));
    }

    [Fact]
    public void Compile_retains_selected_lane_geometry_when_allocated_junction_crosses_a_node()
    {
        var links = new[] { AmbiguousTurnLink("a", 0, 0), AmbiguousTurnLink("b", 1, 10) };
        var context = CreateTurnContext(links, includeJunction: true);
        var corridorAllocations = context.Allocation.Corridors.ToDictionary(
            item => item.Key,
            item => item.Value.ToDictionary(lane => lane.Key, lane => lane.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var escapedEdge = links[0].Link.Id;
        corridorAllocations["vertical"][escapedEdge] = corridorAllocations["vertical"][escapedEdge] with
        {
            Coordinate = 240
        };
        context = context with
        {
            Allocation = new CorridorLaneAllocation(
                corridorAllocations.ToDictionary(
                    item => item.Key,
                    item => (IReadOnlyDictionary<string, AllocatedCorridorLane>)item.Value,
                    StringComparer.Ordinal),
                Array.Empty<string>())
        };
        var withoutNodes = EdgeTraversalCompiler.Compile(Links(links), context.Observation, context.Allocation);
        var changed = links.First(link =>
            !withoutNodes.Geometry[link.Link.Id].Points.SequenceEqual(CompletePoints(link)));
        var compiledBend = withoutNodes.Traversals[changed.Link.Id].Junctions[0].TransitionPoint;
        var obstacle = new NodeLayout(
            new RenderNode(
                "obstacle",
                null,
                "Obstacle",
                "Fixture.Obstacle",
                "Class",
                false,
                string.Empty,
                0,
                Array.Empty<string>(),
                Array.Empty<TypeProperty>(),
                0),
            new Rect(compiledBend.X - 2, compiledBend.Y - 2, 4, 4),
            0,
            false);

        var result = EdgeTraversalCompiler.Compile(
            Links(links),
            context.Observation,
            context.Allocation,
            new Dictionary<string, NodeLayout> { [obstacle.Node.Id] = obstacle });

        Assert.Equal(CompletePoints(changed), result.Geometry[changed.Link.Id].Points);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.EdgeId == changed.Link.Id &&
            diagnostic.Code == "TRAVERSAL_NODE_COLLISION");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Compile_separates_parallel_turns_that_legacy_geometry_routes_through_one_bend(int routeCount)
    {
        var links = Enumerable.Range(0, routeCount)
            .Select(index => AmbiguousTurnLink(((char)('a' + index)).ToString(), index, index * 10))
            .ToArray();
        var context = CreateTurnContext(links, includeJunction: true);
        var legacyBends = links.Select(link => CompletePoints(link)[2]).ToArray();
        Assert.Equal(routeCount, context.Allocation.Corridors["horizontal"].Values.Select(lane => lane.Coordinate).Distinct().Count());
        Assert.Equal(routeCount, context.Allocation.Corridors["vertical"].Values.Select(lane => lane.Coordinate).Distinct().Count());

        var result = EdgeTraversalCompiler.Compile(Links(links), context.Observation, context.Allocation);

        Assert.Single(legacyBends.Distinct());
        Assert.Empty(result.Diagnostics);
        var allocatedBends = result.Geometry.Values
            .Select(geometry => result.Traversals[geometry.EdgeId].Junctions[0].TransitionPoint)
            .ToArray();
        Assert.True(allocatedBends.Distinct().Count() == routeCount,
            string.Join(";", allocatedBends.Select(point => $"{point.X},{point.Y}")));
        Assert.All(result.Geometry.Values, geometry => Assert.False(geometry.UsedFallback));
        Assert.All(result.Geometry.Values, geometry => AssertOrthogonal(geometry.Points));
        var legacyMetrics = JunctionGeometryMetrics.Measure(
            links.ToDictionary(
                link => link.Link.Id,
                link => (IReadOnlyList<Point>)CompletePoints(link).Skip(1).Take(3).ToArray(),
                StringComparer.Ordinal),
            10);
        var allocatedMetrics = JunctionGeometryMetrics.Measure(
            result.Geometry.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<Point>)item.Value.Points.Skip(1).Take(3).ToArray(),
                StringComparer.Ordinal),
            10);
        Assert.True(allocatedMetrics.SharedBends < legacyMetrics.SharedBends);
        Assert.True(allocatedMetrics.Crossings <= legacyMetrics.Crossings,
            $"legacy crossings={legacyMetrics.Crossings}; allocated crossings={allocatedMetrics.Crossings}");
        Assert.Equal(0, allocatedMetrics.SharedBends);
        Assert.Equal(0, allocatedMetrics.OverlapLength);
        Assert.Equal(0, allocatedMetrics.SpacingDeficits);
        var validatedLinks = links.ToDictionary(
            link => link.Link.Id,
            link =>
            {
                var points = result.Geometry[link.Link.Id].Points;
                return new LinkLayout(
                    link.Link,
                    points[0],
                    points[points.Count - 1],
                    points.Skip(1).Take(points.Count - 2),
                    link.ExitX,
                    link.EntryX);
            },
            StringComparer.Ordinal);
        var validation = TraceabilityValidator.Validate(
            new Dictionary<string, NodeLayout>(StringComparer.Ordinal),
            validatedLinks,
            10);
        Assert.DoesNotContain(validation.Violations, finding =>
            finding.Code != TraceabilityViolationCode.PerpendicularCrossing);
    }

    [Fact]
    public void Compile_falls_back_when_parallel_turn_lane_order_is_inverted()
    {
        var links = new[] { TurnLink("a", 0, 0), TurnLink("b", 1, 10), TurnLink("c", 2, 20) };
        var context = CreateTurnContext(links, includeJunction: true);
        var vertical = context.Allocation.Corridors["vertical"].ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        vertical["a"] = vertical["a"] with { LaneIndex = 2 };
        vertical["b"] = vertical["b"] with { LaneIndex = 1 };
        vertical["c"] = vertical["c"] with { LaneIndex = 0 };
        var corridors = context.Allocation.Corridors.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        corridors["vertical"] = vertical;

        var result = EdgeTraversalCompiler.Compile(
            Links(links),
            context.Observation,
            new CorridorLaneAllocation(corridors, Array.Empty<string>()));

        Assert.Equal(3, result.Diagnostics.Count(diagnostic => diagnostic.Code == "JUNCTION_LANE_ORDER_INVERSION"));
        Assert.All(links, link =>
        {
            Assert.True(result.Geometry[link.Link.Id].UsedFallback);
            Assert.Equal(CompletePoints(link), result.Geometry[link.Link.Id].Points);
        });
    }

    [Fact]
    public void Compile_keeps_straight_route_distinct_when_neighbor_departs()
    {
        var departing = TurnLink("departing", 0, 0);
        var context = CreateTurnContext(new[] { departing }, includeJunction: true);
        var straight = new LinkLayout(
            new RenderLink("straight", "source_straight", "target_straight", "internal", 1),
            new Point(40, 20),
            new Point(160, 60),
            new[] { new Point(40, 70), new Point(140, 70) },
            0.5,
            0.5);
        var horizontal = context.Observation.Corridors["horizontal"];
        var mappings = context.Observation.SegmentMappings.Concat(new[]
        {
            new CorridorSegmentMapping("straight", 1, "horizontal", new Segment(straight.Points[0], straight.Points[1]))
        }).ToArray();
        var usage = context.Observation.Usage.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        usage["horizontal"] = new CorridorUsage(horizontal, new[] { "departing", "straight" }, 2);
        var observation = context.Observation with { SegmentMappings = mappings, Usage = usage };
        var allocations = context.Allocation.Corridors.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var horizontalLanes = allocations["horizontal"].ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        horizontalLanes["straight"] = new AllocatedCorridorLane("horizontal", "straight", 1, 70);
        allocations["horizontal"] = horizontalLanes;

        var result = EdgeTraversalCompiler.Compile(
            Links(new[] { departing, straight }),
            observation,
            new CorridorLaneAllocation(allocations, Array.Empty<string>()));

        Assert.Equal(CompletePoints(departing), result.Geometry["departing"].Points);
        Assert.Equal(CompletePoints(straight), result.Geometry["straight"].Points);
        Assert.NotEqual(
            result.Traversals["departing"].Junctions[0].TransitionPoint,
            result.Traversals["straight"].Corridors[0].End);
    }

    [Fact]
    public void Compile_emits_structured_fallback_for_unobserved_turn_junction()
    {
        var link = TurnLink("edge", 0, 0);
        var context = CreateTurnContext(new[] { link }, includeJunction: false);

        var result = EdgeTraversalCompiler.Compile(Links(link), context.Observation, context.Allocation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("UNSUPPORTED_JUNCTION_TOPOLOGY", diagnostic.Code);
        Assert.Equal("edge", diagnostic.EdgeId);
        Assert.True(result.Geometry["edge"].UsedFallback);
        Assert.Equal(CompletePoints(link), result.Geometry["edge"].Points);
    }

    [Fact]
    public void Compile_preserves_selected_path_when_its_junction_topology_is_unsupported()
    {
        var accepted = TurnLink("edge", 0, 0);
        var selectedPoints = CompletePoints(accepted);
        var selection = GlobalCorridorPathSelector.Select(
            new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
            {
                ["edge"] = new[]
                {
                    new CorridorPathCandidate(
                        "edge",
                        new[] { "horizontal", "vertical" },
                        new[] { "missing-junction" },
                        new CorridorPathSignature("horizontal-vertical"),
                        new CorridorPathLocalCost(200, 1, 0),
                        selectedPoints,
                        IsAcceptedPath: true)
                }
            },
            new Dictionary<string, int>(StringComparer.Ordinal),
            10,
            4);
        var selected = selection.Selected["edge"].Points;
        var link = new LinkLayout(
            accepted.Link,
            selected[0],
            selected[^1],
            selected.Skip(1).Take(selected.Count - 2),
            accepted.ExitX,
            accepted.EntryX);
        var context = CreateTurnContext(new[] { link }, includeJunction: false);

        var result = EdgeTraversalCompiler.Compile(Links(link), context.Observation, context.Allocation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("UNSUPPORTED_JUNCTION_TOPOLOGY", diagnostic.Code);
        Assert.True(result.Geometry["edge"].UsedFallback);
        Assert.Equal(selected, result.Geometry["edge"].Points);

        var final = EdgeTraversalCompiler.Apply(Links(link), result)["edge"];
        Assert.Equal(link.RouteState.Revision, final.RouteState.Revision);
        Assert.Equal(link.RouteState.AuthoritativePoints, final.RouteState.AuthoritativePoints);
        Assert.Equal(LogicalRouteCompilationStatus.Rejected, final.RouteState.CompilationStatus);
        Assert.Contains(final.RouteState.Diagnostics, message => message.Contains("UNSUPPORTED_JUNCTION_TOPOLOGY"));
    }

    [Fact]
    public void Compile_is_deterministic_when_link_dictionary_order_is_reversed()
    {
        var links = new[] { TurnLink("a", 0, 0), TurnLink("b", 1, 10), TurnLink("c", 2, 20) };
        var context = CreateTurnContext(links, includeJunction: true);

        var forward = EdgeTraversalCompiler.Compile(Links(links), context.Observation, context.Allocation);
        var reverse = EdgeTraversalCompiler.Compile(Links(links.AsEnumerable().Reverse()), context.Observation, context.Allocation);

        Assert.Equal(forward.Geometry.Keys, reverse.Geometry.Keys);
        Assert.All(forward.Geometry.Keys, id =>
        {
            Assert.Equal(forward.Geometry[id].UsedFallback, reverse.Geometry[id].UsedFallback);
            Assert.Equal(forward.Geometry[id].Points, reverse.Geometry[id].Points);
        });
    }

    [Fact]
    public void Compile_indexes_junction_pairs_and_preserves_lowest_id_selection()
    {
        var link = TurnLink("edge", 0, 0);
        var context = CreateTurnContext(new[] { link }, includeJunction: true);
        var junctions = new Dictionary<string, CorridorJunction>(StringComparer.Ordinal)
        {
            ["z-matching"] = new("z-matching", new Rect(80, 40, 60, 60), new[] { "vertical", "horizontal" }),
            ["unrelated"] = new("unrelated", new Rect(0, 0, 10, 10), new[] { "other-a", "other-b" }),
            ["a-matching"] = new("a-matching", new Rect(80, 40, 60, 60), new[] { "horizontal", "vertical" })
        };
        var observation = context.Observation with { Junctions = junctions };
        using var session = GenerationPerformanceSession.Start();

        var result = EdgeTraversalCompiler.Compile(Links(link), observation, context.Allocation);
        var report = session.Snapshot();

        Assert.Equal("a-matching", Assert.Single(result.Traversals["edge"].Junctions).JunctionId);
        Assert.Equal(1, report.Counters.Single(item => item.Name == "junction transition lookups").Value);
        Assert.Equal(1, report.Counters.Single(item => item.Name == "junction transition indexes built").Value);
        Assert.DoesNotContain(report.Counters, item => item.Name == "junction lookup candidate checks");
    }

    private static TurnContext CreateTurnContext(IReadOnlyList<LinkLayout> links, bool includeJunction)
    {
        const string horizontal = "horizontal";
        const string vertical = "vertical";
        var mappings = links.SelectMany(link => new[]
        {
            new CorridorSegmentMapping(link.Link.Id, 1, horizontal, new Segment(link.Points[0], link.Points[1])),
            new CorridorSegmentMapping(link.Link.Id, 2, vertical, new Segment(link.Points[1], link.Points[2]))
        }).ToArray();
        var horizontalCorridor = new RoutingCorridor(horizontal, CorridorOrientation.Horizontal, new Rect(0, 40, 120, 60), 10, 6);
        var verticalCorridor = new RoutingCorridor(vertical, CorridorOrientation.Vertical, new Rect(80, 40, 60, 140), 10, 6);
        var corridors = new Dictionary<string, RoutingCorridor>(StringComparer.Ordinal)
        {
            [horizontal] = horizontalCorridor,
            [vertical] = verticalCorridor
        };
        var junctions = includeJunction
            ? new Dictionary<string, CorridorJunction>(StringComparer.Ordinal)
            {
                ["junction"] = new("junction", new Rect(80, 40, 60, 60), new[] { horizontal, vertical })
            }
            : new Dictionary<string, CorridorJunction>(StringComparer.Ordinal);
        var usage = corridors.ToDictionary(
            item => item.Key,
            item => new CorridorUsage(item.Value, links.Select(link => link.Link.Id).ToArray(), links.Count),
            StringComparer.Ordinal);
        var observation = new CorridorObservation(corridors, junctions, mappings, usage);
        var allocated = corridors.Keys.ToDictionary(
            corridorId => corridorId,
            corridorId => (IReadOnlyDictionary<string, AllocatedCorridorLane>)links.ToDictionary(
                link => link.Link.Id,
                link => new AllocatedCorridorLane(
                    corridorId,
                    link.Link.Id,
                    link.Link.Order,
                    corridorId == horizontal ? link.Points[0].Y : link.Points[2].X),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        return new TurnContext(observation, new CorridorLaneAllocation(allocated, Array.Empty<string>()));
    }

    private static LinkLayout TurnLink(string id, int order, int offset)
    {
        var points = new[]
        {
            new Point(20 + offset, 20),
            new Point(20 + offset, 60 + offset),
            new Point(100 + offset, 60 + offset),
            new Point(100 + offset, 140),
            new Point(120 + offset, 180)
        };
        return new LinkLayout(
            new RenderLink(id, $"source_{id}", $"target_{id}", "internal", order),
            points[0],
            points[points.Length - 1],
            points.Skip(1).Take(points.Length - 2),
            0.5,
            0.5);
    }

    private static LinkLayout AmbiguousTurnLink(string id, int order, int offset)
    {
        var points = new[]
        {
            new Point(20 + offset, 20),
            new Point(20 + offset, 60 + offset),
            new Point(100, 60),
            new Point(120 - offset, 140),
            new Point(120 - offset, 180)
        };
        return new LinkLayout(
            new RenderLink(id, $"source_{id}", $"target_{id}", "internal", order),
            points[0],
            points[points.Length - 1],
            points.Skip(1).Take(points.Length - 2),
            0.5,
            0.5);
    }

    private static void AssertOrthogonal(IReadOnlyList<Point> points)
    {
        Assert.All(points.Zip(points.Skip(1), (start, end) => new Segment(start, end)), segment =>
            Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
    }

    private static IReadOnlyDictionary<string, LinkLayout> Links(params LinkLayout[] links) => Links((IEnumerable<LinkLayout>)links);

    private static IReadOnlyDictionary<string, LinkLayout> Links(IEnumerable<LinkLayout> links) =>
        links.ToDictionary(link => link.Link.Id, StringComparer.Ordinal);

    private static IReadOnlyList<Point> CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();

    private sealed record TurnContext(CorridorObservation Observation, CorridorLaneAllocation Allocation);
}
