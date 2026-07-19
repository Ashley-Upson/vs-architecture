using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class AdjacentDownwardComponentProjector
{
    public static AdjacentDownwardComponentProjection Project(
        AdjacentDownwardObservationReport report,
        int requiredSpacing)
    {
        var timer = Stopwatch.StartNew();
        var routes = report.Routes.Where(item => item.Eligible).OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToArray();
        var unassigned = new List<ConflictEdge>();
        var assigned = new List<ConflictEdge>();
        for (var leftIndex = 0; leftIndex < routes.Length; leftIndex++)
        for (var rightIndex = leftIndex + 1; rightIndex < routes.Length; rightIndex++)
        {
            var left = routes[leftIndex];
            var right = routes[rightIndex];
            foreach (var leftDemand in left.Demands)
            foreach (var rightDemand in right.Demands.Where(item => item.Role == leftDemand.Role))
            {
                if (leftDemand.Role is LinkSegmentRole.ConnectionDeparture or LinkSegmentRole.ConnectionArrival)
                {
                    if (leftDemand.MovementScope == rightDemand.MovementScope)
                        unassigned.Add(Edge(left, right, "UnresolvedTerminalCompetition"));
                }
                else if (ContactInteractionPolicy.CreatesUnassignedRailEdge(leftDemand, rightDemand))
                    unassigned.Add(Edge(left, right, "UnassignedLinkSegmentDemand"));
            }

            foreach (var leftRail in left.SelectedAssignedLinkSegments)
            foreach (var rightRail in right.SelectedAssignedLinkSegments.Where(item => item.Role == leftRail.Role))
            {
                if (!ContactInteractionPolicy.CreatesAssignedLinkSegmentEdge(leftRail, rightRail, requiredSpacing)) continue;
                if (leftRail.Role is LinkSegmentRole.ConnectionDeparture or LinkSegmentRole.ConnectionArrival)
                {
                    var leftScope = left.Demands.Single(item => item.Id == leftRail.DemandId).MovementScope;
                    var rightScope = right.Demands.Single(item => item.Id == rightRail.DemandId).MovementScope;
                    if (leftScope == rightScope)
                        assigned.Add(Edge(left, right, "ConflictingAssignedTerminal"));
                }
                else
                    assigned.Add(Edge(left, right, "AssignedLinkSegmentConflict"));
            }

            if (left.Transitions.Select(item => item.Turn).Intersect(right.Transitions.Select(item => item.Turn)).Any())
                assigned.Add(Edge(left, right, "TurnTransitionConflict"));
        }

        var stableUnassigned = Stable(unassigned);
        var stableAssigned = Stable(assigned);
        var unassignedComponents = ConflictComponentBuilder.Build(routes, item => item.LogicalRouteId, stableUnassigned);
        var assignedComponents = ConflictComponentBuilder.Build(routes, item => item.LogicalRouteId, stableAssigned);
        timer.Stop();
        return new AdjacentDownwardComponentProjection(
            unassignedComponents, assignedComponents, stableUnassigned, stableAssigned,
            timer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
    }

    private static ConflictEdge Edge(
        AdjacentDownwardLinkObservation first,
        AdjacentDownwardLinkObservation second,
        string cause) => new(first.LogicalRouteId, second.LogicalRouteId, cause);

    private static IReadOnlyList<ConflictEdge> Stable(IEnumerable<ConflictEdge> edges) => edges
        .Select(edge => string.CompareOrdinal(edge.FirstId, edge.SecondId) <= 0
            ? edge : new ConflictEdge(edge.SecondId, edge.FirstId, edge.Cause))
        .Distinct().OrderBy(item => item.FirstId, StringComparer.Ordinal)
        .ThenBy(item => item.SecondId, StringComparer.Ordinal)
        .ThenBy(item => item.Cause, StringComparer.Ordinal).ToArray();
}
