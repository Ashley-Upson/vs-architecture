using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class BandConflictGrouper
{
    public static IReadOnlyList<BandConflictGroup> Group(
        InterLayerBandObservation band,
        int clearance,
        int padding,
        out long comparisons)
    {
        comparisons = 0;
        var demands = band.Demands.OrderBy(item => item.XStart).ThenBy(item => item.XEnd)
            .ThenBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal).ThenBy(item => item.SegmentIndex).ToArray();
        var neighbours = demands.ToDictionary(item => item.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        for (var leftIndex = 0; leftIndex < demands.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < demands.Length; rightIndex++)
            {
                comparisons++;
                var left = demands[leftIndex];
                var right = demands[rightIndex];
                if (right.XStart > left.XEnd + clearance) break;
                if (Contact(left.XStart, left.XEnd, right.XStart, right.XEnd, clearance) == IntervalContactKind.Disjoint) continue;
                neighbours[left.Id].Add(right.Id);
                neighbours[right.Id].Add(left.Id);
            }
        }

        var byId = demands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<BandConflictGroup>();
        foreach (var seed in demands.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!visited.Add(seed)) continue;
            var pending = new SortedSet<string>(StringComparer.Ordinal) { seed };
            var component = new List<BandRouteDemand>();
            while (pending.Count > 0)
            {
                var id = pending.Min!;
                pending.Remove(id);
                component.Add(byId[id]);
                foreach (var neighbour in neighbours[id].OrderBy(item => item, StringComparer.Ordinal))
                    if (visited.Add(neighbour)) pending.Add(neighbour);
            }

            var assigned = AssignLanes(component, clearance);
            var laneCount = assigned.Values.DefaultIfEmpty(-1).Max() + 1;
            var required = component.Count == 0 ? band.CurrentExtent : padding * 2 + Math.Max(1, laneCount) * clearance;
            var identity = string.Join("+", component.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal));
            groups.Add(new BandConflictGroup(
                $"group:{band.Id}:{identity}", band.Id,
                component.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                assigned,
                component.Select(item => item.LaneIndex).DefaultIfEmpty(-1).Max() + 1,
                laneCount, band.CurrentExtent, required, Math.Max(0, required - band.CurrentExtent),
                SpacingConstraintScope.LayerBoundary));
        }
        return groups.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public static IntervalContactKind Contact(int firstStart, int firstEnd, int secondStart, int secondEnd, int clearance = 0)
    {
        var left = Math.Max(Math.Min(firstStart, firstEnd), Math.Min(secondStart, secondEnd));
        var right = Math.Min(Math.Max(firstStart, firstEnd), Math.Max(secondStart, secondEnd));
        if (right > left) return IntervalContactKind.PositiveOverlap;
        if (right == left) return IntervalContactKind.EndpointContact;
        var gap = left - right;
        return gap < clearance ? IntervalContactKind.EndpointContact : IntervalContactKind.Disjoint;
    }

    public static RoutePointContactKind ClassifyContact(
        Segment first,
        Segment? firstContinuation,
        Segment second,
        Segment? secondContinuation)
    {
        if (first.IsHorizontal != second.IsHorizontal && first.IsOrthogonal && second.IsOrthogonal)
        {
            var horizontal = first.IsHorizontal ? first : second;
            var vertical = first.IsVertical ? first : second;
            var point = new Point(vertical.Start.X, horizontal.Start.Y);
            var strict = point.X > Math.Min(horizontal.Start.X, horizontal.End.X) &&
                point.X < Math.Max(horizontal.Start.X, horizontal.End.X) &&
                point.Y > Math.Min(vertical.Start.Y, vertical.End.Y) &&
                point.Y < Math.Max(vertical.Start.Y, vertical.End.Y);
            if (strict && firstContinuation is null && secondContinuation is null)
                return RoutePointContactKind.CleanCrossover;
        }
        if (firstContinuation is not null || secondContinuation is not null)
            return RoutePointContactKind.AmbiguousBend;
        return RoutePointContactKind.StraightContinuation;
    }

    private static IReadOnlyDictionary<string, int> AssignLanes(IReadOnlyList<BandRouteDemand> demands, int clearance)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var laneEnds = new List<int>();
        foreach (var demand in demands.OrderBy(item => item.XStart).ThenBy(item => item.XEnd)
                     .ThenBy(item => item.TerminalOrder).ThenBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
                     .ThenBy(item => item.SegmentIndex))
        {
            var lane = laneEnds.FindIndex(end => end + clearance <= demand.XStart);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(demand.XEnd); }
            else laneEnds[lane] = demand.XEnd;
            result[demand.Id] = lane;
        }
        return result;
    }
}
