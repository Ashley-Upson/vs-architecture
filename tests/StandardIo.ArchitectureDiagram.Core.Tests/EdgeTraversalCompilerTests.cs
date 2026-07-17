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
        Assert.All(result.Diagnostics, diagnostic => Assert.NotEqual("UNSUPPORTED_JUNCTION_TOPOLOGY", diagnostic.Code));
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
                link => new AllocatedCorridorLane(corridorId, link.Link.Id, link.Link.Order, 50 + link.Link.Order * 10),
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

    private static IReadOnlyDictionary<string, LinkLayout> Links(params LinkLayout[] links) => Links((IEnumerable<LinkLayout>)links);

    private static IReadOnlyDictionary<string, LinkLayout> Links(IEnumerable<LinkLayout> links) =>
        links.ToDictionary(link => link.Link.Id, StringComparer.Ordinal);

    private static IReadOnlyList<Point> CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();

    private sealed record TurnContext(CorridorObservation Observation, CorridorLaneAllocation Allocation);
}
