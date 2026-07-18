using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class EdgeTraversalCompiler
{
    public static EdgeTraversalCompilation Compile(
        IReadOnlyDictionary<string, LinkLayout> links,
        CorridorObservation observation,
        CorridorLaneAllocation allocation,
        IReadOnlyDictionary<string, NodeLayout>? nodes = null,
        IReadOnlyDictionary<string, LinkLayout>? logicalLinks = null)
    {
        PerformanceAudit.Increment("traversals compiled", links.Count);
        var mappings = observation.SegmentMappings
            .GroupBy(mapping => mapping.EdgeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(mapping => mapping.SegmentIndex),
                StringComparer.Ordinal);
        var mapped = new Dictionary<string, EdgeTraversal>(StringComparer.Ordinal);
        var geometry = new Dictionary<string, CompiledEdgeGeometry>(StringComparer.Ordinal);
        var diagnostics = new List<TraversalDiagnostic>();

        foreach (var link in links.Values.OrderBy(item => item.Link.Order).ThenBy(item => item.Link.Id, StringComparer.Ordinal))
        {
            mapped[link.Link.Id] = Map(link, observation, allocation, mappings.TryGetValue(link.Link.Id, out var edgeMappings)
                ? edgeMappings
                : new Dictionary<int, CorridorSegmentMapping>());
        }

        var junctionAllocation = SimpleJunctionAllocator.Allocate(mapped);
        var traversals = new Dictionary<string, EdgeTraversal>(StringComparer.Ordinal);
        foreach (var link in links.Values.OrderBy(item => item.Link.Order).ThenBy(item => item.Link.Id, StringComparer.Ordinal))
        {
            var traversal = junctionAllocation.Traversals[link.Link.Id];
            var compiled = Compile(traversal);
            var accepted = CompletePoints(link);
            if (!IsOrthogonal(compiled.Points) && IsOrthogonal(accepted))
            {
                var diagnostic = new TraversalDiagnostic(
                    link.Link.Id,
                    "TRAVERSAL_NON_ORTHOGONAL",
                    "Compiled traversal was not orthogonal; the current authoritative route was retained.");
                traversal = traversal with
                {
                    AcceptedFallbackPoints = accepted,
                    Diagnostics = traversal.Diagnostics.Concat(new[] { diagnostic }).ToArray()
                };
                compiled = new CompiledEdgeGeometry(link.Link.Id, accepted, true);
            }
            else if (!Normalize(compiled.Points).SequenceEqual(Normalize(accepted)) &&
                !junctionAllocation.AllocatedEdgeIds.Contains(link.Link.Id))
            {
                var diagnostic = new TraversalDiagnostic(
                    link.Link.Id,
                    "TRAVERSAL_ROUND_TRIP_MISMATCH",
                    "Compiled traversal did not reconstruct the current authoritative route; that route was retained.");
                traversal = traversal with
                {
                    AcceptedFallbackPoints = accepted,
                    Diagnostics = traversal.Diagnostics.Concat(new[] { diagnostic }).ToArray()
                };
                compiled = new CompiledEdgeGeometry(link.Link.Id, accepted, true);
            }
            else if (nodes is not null &&
                CrossesNode(compiled.Points, link, nodes) &&
                !CrossesNode(accepted, link, nodes))
            {
                var diagnostic = new TraversalDiagnostic(
                    link.Link.Id,
                    "TRAVERSAL_NODE_COLLISION",
                    "Compiled traversal crossed a node while the accepted lane geometry did not; the accepted route was retained.");
                traversal = traversal with
                {
                    AcceptedFallbackPoints = accepted,
                    Diagnostics = traversal.Diagnostics.Concat(new[] { diagnostic }).ToArray()
                };
                compiled = new CompiledEdgeGeometry(link.Link.Id, accepted, true);
            }

            traversals[link.Link.Id] = traversal;
            geometry[link.Link.Id] = compiled;
            diagnostics.AddRange(traversal.Diagnostics);
        }

        PerformanceAudit.Increment("traversal fallbacks", geometry.Values.Count(item => item.UsedFallback));
        PerformanceAudit.Increment("junction-allocated traversals", junctionAllocation.AllocatedEdgeIds.Count);

        return new EdgeTraversalCompilation(traversals, geometry, diagnostics);
    }

    private static bool CrossesNode(
        IReadOnlyList<Point> points,
        LinkLayout link,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var obstacles = nodes
            .Where(item => !string.Equals(item.Key, link.Link.SourceId, StringComparison.Ordinal) &&
                !string.Equals(item.Key, link.Link.TargetId, StringComparison.Ordinal))
            .Select(item => item.Value.Rect)
            .ToArray();
        return points.Zip(points.Skip(1), (start, end) => new Segment(start, end))
            .Any(segment => obstacles.Any(segment.Intersects));
    }

    private static bool IsOrthogonal(IReadOnlyList<Point> points) =>
        points.Zip(points.Skip(1), (start, end) => new Segment(start, end))
            .All(segment => segment.IsHorizontal || segment.IsVertical);

    public static IReadOnlyDictionary<string, LinkLayout> Apply(
        IReadOnlyDictionary<string, LinkLayout> links,
        EdgeTraversalCompilation compilation) =>
        links.Values.OrderBy(link => link.Link.Order).ThenBy(link => link.Link.Id, StringComparer.Ordinal).ToDictionary(
            link => link.Link.Id,
            link =>
            {
                var compiled = compilation.Geometry[link.Link.Id];
                var diagnostics = compilation.Traversals[link.Link.Id].Diagnostics
                    .Select(item => $"{item.Code}: {item.Message}")
                    .ToArray();
                return compiled.UsedFallback
                    ? link.RejectGeometry(nameof(EdgeTraversalCompiler), diagnostics)
                    : link.AcceptGeometry(
                        compiled.Points,
                        link.RouteState.Stage > LogicalRouteStage.Compiled
                            ? link.RouteState.Stage
                            : LogicalRouteStage.Compiled,
                        nameof(EdgeTraversalCompiler),
                        diagnostics: diagnostics);
            },
            StringComparer.Ordinal);

    private static EdgeTraversal Map(
        LinkLayout link,
        CorridorObservation observation,
        CorridorLaneAllocation allocation,
        IReadOnlyDictionary<int, CorridorSegmentMapping> mappings)
    {
        var points = CompletePoints(link);
        var diagnostics = new List<TraversalDiagnostic>();
        var corridors = new List<CorridorTraversal>();
        for (var index = 1; index < points.Length - 2; index++)
        {
            if (!mappings.TryGetValue(index, out var mapping) ||
                !allocation.TryGetLane(mapping.CorridorId, link.Link.Id, out var lane))
            {
                diagnostics.Add(new TraversalDiagnostic(
                    link.Link.Id,
                    "UNSUPPORTED_CORRIDOR_TRAVERSAL",
                    "A non-terminal segment has no successful corridor lane allocation.",
                    index));
                continue;
            }

            corridors.Add(new CorridorTraversal(
                index,
                mapping.CorridorId,
                Direction(observation.Corridors[mapping.CorridorId], points[index], points[index + 1]),
                lane,
                points[index],
                points[index + 1]));
        }

        var junctions = new List<JunctionTraversal>();
        foreach (var pair in corridors.Zip(corridors.Skip(1), (incoming, outgoing) => new { incoming, outgoing }))
        {
            if (pair.outgoing.SegmentIndex != pair.incoming.SegmentIndex + 1)
            {
                continue;
            }

            CorridorJunction? junction = null;
            PerformanceAudit.Increment("junction transition lookups");
            foreach (var candidate in observation.Junctions.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                PerformanceAudit.Increment("junction lookup candidate checks");
                if (candidate.CorridorIds.Contains(pair.incoming.CorridorId) &&
                    candidate.CorridorIds.Contains(pair.outgoing.CorridorId))
                {
                    junction = candidate;
                    break;
                }
            }
            var straight = string.Equals(pair.incoming.CorridorId, pair.outgoing.CorridorId, StringComparison.Ordinal) ||
                DirectionOrientation(pair.incoming.Direction) == DirectionOrientation(pair.outgoing.Direction);
            if (!straight && junction is null)
            {
                diagnostics.Add(new TraversalDiagnostic(
                    link.Link.Id,
                    "UNSUPPORTED_JUNCTION_TOPOLOGY",
                    "Adjacent corridor traversals do not have an observed bounded junction.",
                    pair.outgoing.SegmentIndex));
            }

            junctions.Add(new JunctionTraversal(
                junction?.Id ?? $"continuation:{pair.incoming.CorridorId}:{pair.outgoing.CorridorId}",
                pair.incoming.CorridorId,
                pair.incoming.Lane.LaneIndex,
                pair.outgoing.CorridorId,
                pair.outgoing.Lane.LaneIndex,
                pair.incoming.End,
                straight));
        }

        return new EdgeTraversal(
            link.Link,
            new TerminalAccess(points[0], points[1]),
            corridors,
            junctions,
            new TerminalAccess(points[points.Length - 1], points[points.Length - 2]),
            points,
            diagnostics);
    }

    private static CompiledEdgeGeometry Compile(EdgeTraversal traversal)
    {
        if (traversal.UsesFallback)
        {
            return new CompiledEdgeGeometry(traversal.Link.Id, traversal.AcceptedFallbackPoints, true);
        }

        var result = new List<Point> { traversal.SourceAccess.Terminal, traversal.SourceAccess.CorridorBoundary };
        foreach (var corridor in traversal.Corridors.OrderBy(item => item.SegmentIndex))
        {
            AppendDistinct(result, corridor.Start);
            AppendDistinct(result, corridor.End);
        }

        AppendDistinct(result, traversal.TargetAccess.CorridorBoundary);
        AppendDistinct(result, traversal.TargetAccess.Terminal);
        return new CompiledEdgeGeometry(traversal.Link.Id, result, false);
    }

    private static Point[] CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();

    private static TraversalDirection Direction(Point start, Point end)
    {
        if (start.Y == end.Y)
        {
            return end.X >= start.X ? TraversalDirection.Right : TraversalDirection.Left;
        }

        return end.Y >= start.Y ? TraversalDirection.Down : TraversalDirection.Up;
    }

    private static TraversalDirection Direction(RoutingCorridor corridor, Point start, Point end) =>
        corridor.Orientation == CorridorOrientation.Horizontal
            ? end.X >= start.X ? TraversalDirection.Right : TraversalDirection.Left
            : end.Y >= start.Y ? TraversalDirection.Down : TraversalDirection.Up;

    private static CorridorOrientation DirectionOrientation(TraversalDirection direction) =>
        direction is TraversalDirection.Left or TraversalDirection.Right
            ? CorridorOrientation.Horizontal
            : CorridorOrientation.Vertical;

    private static IReadOnlyList<Point> Normalize(IReadOnlyList<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            AppendDistinct(result, point);
            while (result.Count >= 3 &&
                (result[result.Count - 3].X == result[result.Count - 2].X &&
                 result[result.Count - 2].X == result[result.Count - 1].X ||
                 result[result.Count - 3].Y == result[result.Count - 2].Y &&
                 result[result.Count - 2].Y == result[result.Count - 1].Y))
            {
                result.RemoveAt(result.Count - 2);
            }
        }

        return result;
    }

    private static void AppendDistinct(ICollection<Point> points, Point point)
    {
        if (points.Count == 0 || points.Last() != point)
        {
            points.Add(point);
        }
    }
}
