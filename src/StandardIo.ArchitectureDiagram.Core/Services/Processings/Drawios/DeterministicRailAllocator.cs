using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DeterministicRailAllocator
{
    public static DeterministicRailAssignment Assign(
        RailAllocationRegionIdentity region,
        IEnumerable<RailDemand> source,
        RailAssignmentOptions options)
    {
        if (options.Separation < 0) throw new ArgumentOutOfRangeException(nameof(options));
        var demands = source.OrderBy(item => item.OccupiedInterval.Minimum)
            .ThenBy(item => item.OccupiedInterval.Maximum)
            .ThenBy(item => item.TerminalOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (demands.Any(item => item.Orientation != region.Orientation ||
            item.AllowedAxisRange != region.AllowedAxisRange || item.PlacementRevision != region.PlacementRevision))
            throw new ArgumentException("Every demand must belong to the supplied allocation region.", nameof(source));

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
        var demandComponents = new List<IReadOnlyList<RailDemand>>();
        foreach (var seed in demands.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!visited.Add(seed)) continue;
            var pending = new SortedSet<string>(StringComparer.Ordinal) { seed };
            var component = new List<RailDemand>();
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
        var components = pendingComponents.Select(item => new RailAssignmentComponent(
            item.Id, region, item.Demands, item.Rails, item.RequiredExtent,
            Math.Max(0, item.RequiredExtent - currentExtent))).ToArray();
        var requiredExtent = components.Select(item => item.RequiredExtent).DefaultIfEmpty(currentExtent).Max();
        extentTimer.Stop();
        return new DeterministicRailAssignment(
            components,
            components.SelectMany(item => item.Rails).ToDictionary(item => item.DemandId, StringComparer.Ordinal),
            requiredExtent, comparisons,
            Microseconds(conflictTimer), Microseconds(componentTimer), Microseconds(assignmentTimer), Microseconds(extentTimer));
    }

    private static (string Id, IReadOnlyList<RailDemand> Demands, IReadOnlyList<AssignedRail> Rails, int RequiredExtent)
        AssignComponent(RailAllocationRegionIdentity region, IReadOnlyList<RailDemand> demands, RailAssignmentOptions options)
    {
        var laneEnds = new List<int>();
        var assigned = new List<(RailDemand Demand, int Lane)>();
        foreach (var demand in demands.OrderBy(item => item.OccupiedInterval.Minimum)
                     .ThenBy(item => item.OccupiedInterval.Maximum).ThenBy(item => item.TerminalOrder)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var lane = laneEnds.FindIndex(end => CanReuse(end, demand.OccupiedInterval.Minimum, options));
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(demand.OccupiedInterval.Maximum); }
            else laneEnds[lane] = demand.OccupiedInterval.Maximum;
            assigned.Add((demand, lane));
        }
        var required = options.Padding * 2 + Math.Max(1, laneEnds.Count) * options.Separation;
        var origin = region.AllowedAxisRange.Minimum + options.Padding;
        var rails = assigned.Select(item => new AssignedRail(
            $"{item.Demand.Id}:assigned", item.Demand.Id, item.Demand.LogicalRouteId,
            item.Demand.Orientation, origin + item.Lane * options.Separation, item.Lane,
            item.Demand.OccupiedInterval, item.Demand.Role, item.Demand.PlacementRevision,
            item.Demand.RouteRevision)).ToArray();
        var identity = string.Join("+", demands.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal));
        return ($"rail-component:{region.EnvelopeIdentity}:{identity}", demands, rails, required);
    }

    private static bool Conflicts(AxisInterval left, AxisInterval right, RailAssignmentOptions options)
    {
        var overlap = Math.Min(left.Maximum, right.Maximum) - Math.Max(left.Minimum, right.Minimum);
        if (overlap > 0) return true;
        if (overlap == 0) return options.EndpointContactCreatesComponent;
        return -overlap < options.Separation;
    }

    private static bool CanReuse(int end, int start, RailAssignmentOptions options) =>
        options.EndpointContactRequiresSeparateLane
            ? end + options.Separation < start
            : end + options.Separation <= start;

    private static long Microseconds(Stopwatch timer) =>
        timer.ElapsedTicks * 1000000 / Stopwatch.Frequency;
}
