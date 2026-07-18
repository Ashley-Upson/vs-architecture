using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DeterministicSharedTurnAllocator
{
    public static SharedTurnAllocation Assign(IEnumerable<AssignedRail> sourceRails)
    {
        var byRoute = sourceRails.GroupBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        var transitions = new Dictionary<string, IReadOnlyList<RailTransition>>(StringComparer.Ordinal);
        var rejected = new List<string>();
        var occupiedTurns = new HashSet<Point>();
        foreach (var route in byRoute)
        {
            var departure = route.SingleOrDefault(item => item.Role == RailSemanticRole.TerminalDeparture);
            var through = route.SingleOrDefault(item => item.Role == RailSemanticRole.Through);
            var arrival = route.SingleOrDefault(item => item.Role == RailSemanticRole.TerminalArrival);
            if (departure is null || through is null || arrival is null ||
                departure.Orientation != RailOrientation.Vertical ||
                through.Orientation != RailOrientation.Horizontal ||
                arrival.Orientation != RailOrientation.Vertical ||
                departure.PlacementRevision != through.PlacementRevision ||
                arrival.PlacementRevision != through.PlacementRevision ||
                departure.RouteRevision != through.RouteRevision ||
                arrival.RouteRevision != through.RouteRevision)
            {
                rejected.Add(route.Key);
                continue;
            }

            var first = new Point(departure.AxisCoordinate, through.AxisCoordinate);
            var second = new Point(arrival.AxisCoordinate, through.AxisCoordinate);
            if (first == second || occupiedTurns.Contains(first) || occupiedTurns.Contains(second))
            {
                rejected.Add(route.Key);
                continue;
            }

            occupiedTurns.Add(first);
            occupiedTurns.Add(second);
            transitions[route.Key] = new[]
            {
                new RailTransition($"{route.Key}:common-turn:0", route.Key, departure.Id, through.Id,
                    first, 0, through.PlacementRevision, through.RouteRevision),
                new RailTransition($"{route.Key}:common-turn:1", route.Key, through.Id, arrival.Id,
                    second, 1, through.PlacementRevision, through.RouteRevision)
            };
        }

        return new SharedTurnAllocation(transitions, rejected.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }
}
