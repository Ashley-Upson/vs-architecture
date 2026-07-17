using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class SimpleJunctionAllocator
{
    public static JunctionAllocationResult Allocate(IReadOnlyDictionary<string, EdgeTraversal> traversals)
    {
        var result = traversals.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var diagnostics = new List<TraversalDiagnostic>();
        var allocatedEdgeIds = new HashSet<string>(StringComparer.Ordinal);
        var turns = traversals.Values
            .SelectMany(traversal => traversal.Junctions
                .Where(junction => !junction.IsStraightContinuation)
                .Select(junction => new Turn(traversal.Link.Id, junction)))
            .GroupBy(turn => turn.Junction.JunctionId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in turns)
        {
            var items = group.OrderBy(item => item.EdgeId, StringComparer.Ordinal).ToArray();
            var topology = items
                .Select(item => $"{item.Junction.IncomingCorridorId}->{item.Junction.OutgoingCorridorId}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (topology.Length != 1)
            {
                Diagnose(items, "UNSUPPORTED_JUNCTION_TOPOLOGY",
                    "The bounded allocator supports one incoming-to-outgoing corridor pair per junction.");
                continue;
            }

            var ordered = items.OrderBy(item => item.Junction.IncomingLaneIndex).ThenBy(item => item.EdgeId, StringComparer.Ordinal).ToArray();
            if (!IsStrictlyIncreasing(ordered.Select(item => item.Junction.OutgoingLaneIndex)))
            {
                Diagnose(items, "JUNCTION_LANE_ORDER_INVERSION",
                    "Outgoing lane order does not preserve incoming lane order.");
                continue;
            }

            var allocated = new List<(Turn Turn, Point Point)>();
            foreach (var item in items)
            {
                var traversal = result[item.EdgeId];
                var incomingMatches = traversal.Corridors.Where(corridor =>
                    corridor.CorridorId == item.Junction.IncomingCorridorId &&
                    corridor.Lane.LaneIndex == item.Junction.IncomingLaneIndex).ToArray();
                var outgoingMatches = traversal.Corridors.Where(corridor =>
                    corridor.CorridorId == item.Junction.OutgoingCorridorId &&
                    corridor.Lane.LaneIndex == item.Junction.OutgoingLaneIndex).ToArray();
                if (incomingMatches.Length != 1 || outgoingMatches.Length != 1)
                {
                    Diagnose(items, "AMBIGUOUS_JUNCTION_CORRIDOR",
                        "The bounded allocator requires exactly one matching incoming and outgoing corridor traversal.");
                    allocated.Clear();
                    break;
                }

                allocated.Add((item, Intersection(incomingMatches[0], outgoingMatches[0])));
            }

            if (allocated.Count == 0)
            {
                continue;
            }

            if (allocated.Select(item => item.Point).Distinct().Count() != allocated.Count)
            {
                Diagnose(items, "JUNCTION_TRANSITION_POINT_REUSE",
                    "Two routes would reuse the same junction bend point.");
                continue;
            }

            foreach (var allocation in allocated)
            {
                var traversal = result[allocation.Turn.EdgeId];
                var junctions = traversal.Junctions
                    .Select(junction => IsSameJunction(junction, allocation.Turn.Junction)
                        ? junction with { TransitionPoint = allocation.Point }
                        : junction)
                    .ToArray();
                var corridors = traversal.Corridors.Select(corridor =>
                {
                    if (corridor.CorridorId == allocation.Turn.Junction.IncomingCorridorId &&
                        corridor.Lane.LaneIndex == allocation.Turn.Junction.IncomingLaneIndex)
                    {
                        return corridor with { End = allocation.Point };
                    }

                    if (corridor.CorridorId == allocation.Turn.Junction.OutgoingCorridorId &&
                        corridor.Lane.LaneIndex == allocation.Turn.Junction.OutgoingLaneIndex)
                    {
                        return corridor with { Start = allocation.Point };
                    }

                    return corridor;
                }).ToArray();
                result[allocation.Turn.EdgeId] = traversal with { Corridors = corridors, Junctions = junctions };
                allocatedEdgeIds.Add(allocation.Turn.EdgeId);
            }
        }

        return new JunctionAllocationResult(result, diagnostics, allocatedEdgeIds);

        void Diagnose(IEnumerable<Turn> affected, string code, string message)
        {
            foreach (var item in affected)
            {
                var diagnostic = new TraversalDiagnostic(item.EdgeId, code, message, JunctionId: item.Junction.JunctionId);
                diagnostics.Add(diagnostic);
                var traversal = result[item.EdgeId];
                result[item.EdgeId] = traversal with
                {
                    Diagnostics = traversal.Diagnostics.Concat(new[] { diagnostic }).ToArray()
                };
            }
        }
    }

    private static Point Intersection(CorridorTraversal incoming, CorridorTraversal outgoing)
    {
        var incomingOrientation = Orientation(incoming.Direction);
        var outgoingOrientation = Orientation(outgoing.Direction);
        if (incomingOrientation == outgoingOrientation)
        {
            return incoming.End;
        }

        return incomingOrientation == CorridorOrientation.Horizontal
            ? new Point(outgoing.Lane.Coordinate, incoming.Lane.Coordinate)
            : new Point(incoming.Lane.Coordinate, outgoing.Lane.Coordinate);
    }

    private static CorridorOrientation Orientation(TraversalDirection direction) =>
        direction is TraversalDirection.Left or TraversalDirection.Right
            ? CorridorOrientation.Horizontal
            : CorridorOrientation.Vertical;

    private static bool IsStrictlyIncreasing(IEnumerable<int> values)
    {
        var previous = int.MinValue;
        foreach (var value in values)
        {
            if (value <= previous)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private static bool IsSameJunction(JunctionTraversal left, JunctionTraversal right) =>
        string.Equals(left.JunctionId, right.JunctionId, StringComparison.Ordinal) &&
        string.Equals(left.IncomingCorridorId, right.IncomingCorridorId, StringComparison.Ordinal) &&
        left.IncomingLaneIndex == right.IncomingLaneIndex &&
        string.Equals(left.OutgoingCorridorId, right.OutgoingCorridorId, StringComparison.Ordinal) &&
        left.OutgoingLaneIndex == right.OutgoingLaneIndex;

    private sealed record Turn(string EdgeId, JunctionTraversal Junction);
}
