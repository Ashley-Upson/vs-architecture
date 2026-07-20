using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DeterministicSlotAllocator
{
    public static DeterministicSlotAssignment Assign(
        LinkSegmentAllocationRegionIdentity region,
        IEnumerable<LinkSegmentDemand> source,
        LinkSegmentAssignmentOptions options)
    {
        if (options.Separation < 0) throw new ArgumentOutOfRangeException(nameof(options));
        var demands = source.OrderBy(item => item.OccupiedInterval.Minimum)
            .ThenBy(item => item.OccupiedInterval.Maximum)
            .ThenBy(item => item.ConnectionOrder)
            .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .ThenBy(item => item.TurnOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (demands.Any(item => item.Orientation != region.Orientation ||
            item.AllowedAxisRange != region.AllowedAxisRange || item.PlacementRevision != region.PlacementRevision))
            throw new ArgumentException("Every demand must belong to the supplied allocation region.", nameof(source));
        if (demands.Length > 0 && demands.All(item => item.OccupiedInterval == demands[0].OccupiedInterval))
            return AssignCompleteOverlap(region, demands, options);

        var comparisons = 0L;
        var conflictTimer = Stopwatch.StartNew();
        var neighbours = demands.ToDictionary(item => item.Id,
            _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        for (var leftIndex = 0; leftIndex < demands.Length; leftIndex++)
        {
            var left = demands[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < demands.Length; rightIndex++)
            {
                comparisons++;
                var right = demands[rightIndex];
                var gap = right.OccupiedInterval.Minimum - left.OccupiedInterval.Maximum;
                if (gap >= options.Separation && gap > 0) break;
                if (!Conflicts(left.OccupiedInterval, right.OccupiedInterval, options)) continue;
                neighbours[left.Id].Add(right.Id);
                neighbours[right.Id].Add(left.Id);
            }
        }
        conflictTimer.Stop();

        var componentTimer = Stopwatch.StartNew();
        var byId = demands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var demandComponents = new List<IReadOnlyList<LinkSegmentDemand>>();
        foreach (var seed in demands.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!visited.Add(seed)) continue;
            var pending = new SortedSet<string>(StringComparer.Ordinal) { seed };
            var component = new List<LinkSegmentDemand>();
            while (pending.Count > 0)
            {
                var id = pending.Min!;
                pending.Remove(id);
                component.Add(byId[id]);
                foreach (var neighbour in neighbours[id].OrderBy(item => item, StringComparer.Ordinal))
                    if (visited.Add(neighbour)) pending.Add(neighbour);
            }
            demandComponents.Add(component.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
        }
        componentTimer.Stop();

        var assignmentTimer = Stopwatch.StartNew();
        var pendingComponents = demandComponents.Select(component => AssignComponent(region, component, options)).ToArray();
        assignmentTimer.Stop();
        var extentTimer = Stopwatch.StartNew();
        var currentExtent = Math.Max(0, region.AllowedAxisRange.Length);
        var components = pendingComponents.Select(item => new LinkSegmentConflictComponent(
            item.Id, region, item.Demands, item.Segments, item.RequiredExtent,
            Math.Max(0, item.RequiredExtent - currentExtent))).ToArray();
        var requiredExtent = components.Select(item => item.RequiredExtent).DefaultIfEmpty(currentExtent).Max();
        extentTimer.Stop();
        return new DeterministicSlotAssignment(
            components,
            components.SelectMany(item => item.Segments).ToDictionary(item => item.DemandId, StringComparer.Ordinal),
            requiredExtent, comparisons,
            Microseconds(conflictTimer), Microseconds(componentTimer), Microseconds(assignmentTimer), Microseconds(extentTimer));
    }

    private static (string Id, IReadOnlyList<LinkSegmentDemand> Demands, IReadOnlyList<AssignedLinkSegment> Segments, int RequiredExtent)
        AssignComponent(LinkSegmentAllocationRegionIdentity region, IReadOnlyList<LinkSegmentDemand> demands, LinkSegmentAssignmentOptions options)
    {
        var slotEnds = new List<int>();
        var assigned = new List<(LinkSegmentDemand Demand, int Slot)>();
        foreach (var demand in OrderComponent(demands))
        {
            var slot = slotEnds.FindIndex(end => CanReuse(end, demand.OccupiedInterval.Minimum, options));
            if (slot < 0) { slot = slotEnds.Count; slotEnds.Add(demand.OccupiedInterval.Maximum); }
            else slotEnds[slot] = demand.OccupiedInterval.Maximum;
            assigned.Add((demand, slot));
        }
        var required = options.Padding * 2 + Math.Max(1, slotEnds.Count) * options.Separation;
        var origin = region.AllowedAxisRange.Minimum + options.Padding;
        var segments = assigned.Select(item => new AssignedLinkSegment(
            $"{item.Demand.Id}:assigned", item.Demand.Id, item.Demand.LogicalRouteId,
            item.Demand.Orientation, origin + item.Slot * options.Separation, item.Slot,
            item.Demand.OccupiedInterval, item.Demand.Role, item.Demand.PlacementRevision,
            item.Demand.RouteRevision)).ToArray();
        var identity = string.Join("+", demands.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal));
        return ($"segment-component:{region.EnvelopeIdentity}:{identity}", demands, segments, required);
    }

    private static IReadOnlyList<LinkSegmentDemand> OrderComponent(IReadOnlyList<LinkSegmentDemand> demands)
    {
        var predecessors = demands.ToDictionary(item => item.Id,
            _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var left in demands)
        foreach (var right in demands)
        {
            if (left.Id == right.Id || left.OccupiedInterval.Maximum != right.OccupiedInterval.Minimum) continue;
            var first = EndpointOrder(left.MaximumEndpointRole, right.MinimumEndpointRole);
            if (first < 0) predecessors[right.Id].Add(left.Id);
            else predecessors[left.Id].Add(right.Id);
        }

        var remaining = demands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var ordered = new List<LinkSegmentDemand>(demands.Count);
        while (remaining.Count > 0)
        {
            var next = DefaultOrder(remaining.Values.Where(item =>
                predecessors[item.Id].All(predecessor => !remaining.ContainsKey(predecessor)))).FirstOrDefault()
                ?? DefaultOrder(remaining.Values).First();
            ordered.Add(next);
            remaining.Remove(next.Id);
        }
        return ordered;
    }

    private static IOrderedEnumerable<LinkSegmentDemand> DefaultOrder(IEnumerable<LinkSegmentDemand> demands) =>
        demands.OrderBy(item => item.OccupiedInterval.Minimum)
            .ThenBy(item => item.OccupiedInterval.Maximum)
            .ThenBy(item => item.ConnectionOrder)
            .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .ThenBy(item => item.TurnOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal);

    private static int EndpointOrder(LinkSegmentEndpointRole left, LinkSegmentEndpointRole right)
    {
        if (left == LinkSegmentEndpointRole.Departure && right == LinkSegmentEndpointRole.Arrival) return -1;
        if (right == LinkSegmentEndpointRole.Departure && left == LinkSegmentEndpointRole.Arrival) return 1;
        return 1;
    }

    private static DeterministicSlotAssignment AssignCompleteOverlap(
        LinkSegmentAllocationRegionIdentity region,
        IReadOnlyList<LinkSegmentDemand> demands,
        LinkSegmentAssignmentOptions options)
    {
        var assignmentTimer = Stopwatch.StartNew();
        var assigned = AssignComponent(region, demands, options);
        assignmentTimer.Stop();
        var extentTimer = Stopwatch.StartNew();
        var currentExtent = Math.Max(0, region.AllowedAxisRange.Length);
        var component = new LinkSegmentConflictComponent(
            assigned.Id, region, assigned.Demands, assigned.Segments, assigned.RequiredExtent,
            Math.Max(0, assigned.RequiredExtent - currentExtent));
        var byDemand = assigned.Segments.ToDictionary(item => item.DemandId, StringComparer.Ordinal);
        extentTimer.Stop();
        return new DeterministicSlotAssignment(
            new[] { component }, byDemand, assigned.RequiredExtent, Math.Max(0, demands.Count - 1),
            0, 0, Microseconds(assignmentTimer), Microseconds(extentTimer));
    }

    private static bool Conflicts(AxisInterval left, AxisInterval right, LinkSegmentAssignmentOptions options)
    {
        var overlap = Math.Min(left.Maximum, right.Maximum) - Math.Max(left.Minimum, right.Minimum);
        if (overlap > 0) return true;
        if (overlap == 0) return options.Separation > 0 || options.EndpointContactCreatesComponent;
        return -overlap < options.Separation;
    }

    private static bool CanReuse(int end, int start, LinkSegmentAssignmentOptions options) =>
        options.EndpointContactRequiresSeparateSlot
            ? end + options.Separation < start
            : end + options.Separation <= start;

    private static long Microseconds(Stopwatch timer) =>
        timer.ElapsedTicks * 1000000 / Stopwatch.Frequency;
}
