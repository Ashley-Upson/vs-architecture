using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class GeneralDownwardCommonAllocator
{
    public static GeneralDownwardAssignmentReport Assign(
        GeneralDownwardObservationReport report,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        int separation,
        int padding)
    {
        var eligible = report.Routes.Where(item => item.Observation.Eligible).ToArray();
        var timer = Stopwatch.StartNew();
        var regions = eligible.SelectMany(item => item.Observation.Demands)
            .Where(item => item.Role == RailSemanticRole.Through)
            .GroupBy(RegionKey).OrderBy(item => item.Key, StringComparer.Ordinal).Select(group =>
            {
                var sample = group.First();
                var region = new RailAllocationRegionIdentity(sample.Orientation, sample.AllowedAxisRange,
                    group.Key, sample.MovementScope, sample.PlacementRevision);
                var assignment = DeterministicRailAllocator.Assign(region, group,
                    new RailAssignmentOptions(separation, padding));
                GenerationConstraint? proposal = null;
                if (assignment.RequiredExtent > region.AllowedAxisRange.Length)
                {
                    var lowerDepth = int.Parse(region.MovementScope!.Value.Id.Substring("depth:".Length));
                    var currentLowerY = nodes.Values.Where(item => !item.IsStandalone && item.Depth == lowerDepth)
                        .Select(item => item.Rect.Y).DefaultIfEmpty(region.AllowedAxisRange.Maximum).Min();
                    proposal = LayerSuffixConstraintMaterializer.ProposeMinimumY(
                        region, assignment.RequiredExtent, currentLowerY);
                }
                return new CommonRailRegionObservation(region, assignment,
                    proposal);
            }).ToArray();
        timer.Stop();
        var assignedByDemand = regions.SelectMany(item => item.Assignment.RailsByDemandId)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var transitionTimer = Stopwatch.StartNew();
        var routes = eligible.OrderBy(item => item.Observation.LogicalRouteId, StringComparer.Ordinal)
            .Select(plan => Reconstruct(plan, assignedByDemand, nodes)).ToArray();
        transitionTimer.Stop();
        return new GeneralDownwardAssignmentReport(regions, routes,
            timer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency,
            transitionTimer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
    }

    private static GeneralDownwardRouteAssignment Reconstruct(
        GeneralDownwardRoutePlan plan,
        IReadOnlyDictionary<string, AssignedRail> assignedByDemand,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var observation = plan.Observation;
        var throughDemands = observation.Demands.Where(item => item.Role == RailSemanticRole.Through)
            .OrderBy(item => item.TurnOrder).ToArray();
        if (throughDemands.Any(item => !assignedByDemand.ContainsKey(item.Id)))
            return Invalid(observation.LogicalRouteId, "A crossed-band rail was not assigned.");
        var through = throughDemands.Select(item => assignedByDemand[item.Id]).ToArray();
        var source = observation.CanonicalAuthoritativePoints[0];
        var target = observation.CanonicalAuthoritativePoints[observation.CanonicalAuthoritativePoints.Count - 1];
        var departure = TerminalRail(observation, RailSemanticRole.TerminalDeparture, source.X,
            new AxisInterval(source.Y, through[0].AxisCoordinate));
        var arrival = TerminalRail(observation, RailSemanticRole.TerminalArrival, target.X,
            new AxisInterval(through[through.Length - 1].AxisCoordinate, target.Y));
        var rails = new[] { departure }.Concat(through).Concat(new[] { arrival }).ToArray();
        var points = new List<Point> { source };
        var transitions = new List<RailTransition>();
        var currentX = source.X;
        for (var index = 0; index < through.Length; index++)
        {
            var rail = through[index];
            var nextX = plan.TransitionXCoordinates[index];
            var firstTurn = new Point(currentX, rail.AxisCoordinate);
            var secondTurn = new Point(nextX, rail.AxisCoordinate);
            Add(points, firstTurn);
            Add(points, secondTurn);
            transitions.Add(new RailTransition($"{observation.LogicalRouteId}:turn:{index}:entry",
                observation.LogicalRouteId, index == 0 ? departure.Id : through[index - 1].Id,
                rail.Id, firstTurn, transitions.Count, rail.PlacementRevision, rail.RouteRevision));
            if (index + 1 < through.Length)
            {
                var vertical = new Segment(secondTurn, new Point(nextX, through[index + 1].AxisCoordinate));
                var obstacle = nodes.Values.FirstOrDefault(node =>
                    node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId && vertical.Intersects(node.Rect));
                if (obstacle is not null)
                    return Invalid(observation.LogicalRouteId, $"ObstacleBypassRequired:{obstacle.Node.Id}");
            }
            currentX = nextX;
        }
        Add(points, target);
        var normalized = AdjacentDownwardRailDemandObserver.Normalize(points);
        if (normalized.Zip(normalized.Skip(1), (a, b) => new Segment(a, b)).Any(item => !item.IsOrthogonal) ||
            HasImmediateReversal(normalized))
            return Invalid(observation.LogicalRouteId, "ReconstructionInvariantFailure");
        return new GeneralDownwardRouteAssignment(observation.LogicalRouteId, rails, transitions,
            normalized, Array.Empty<string>(), true);
    }

    private static AssignedRail TerminalRail(
        AdjacentDownwardRouteObservation route,
        RailSemanticRole role,
        int axis,
        AxisInterval occupied)
    {
        var demand = route.Demands.Single(item => item.Role == role);
        return new AssignedRail($"{demand.Id}:assigned", demand.Id, route.LogicalRouteId,
            RailOrientation.Vertical, axis, 0, occupied, role, demand.PlacementRevision, demand.RouteRevision);
    }

    private static string RegionKey(RailDemand demand) =>
        $"{demand.Orientation}:{demand.AllowedAxisRange.Minimum}:{demand.AllowedAxisRange.Maximum}:" +
        $"{demand.MovementScope?.Kind}:{demand.MovementScope?.Id}:{demand.PlacementRevision.Value}";
    private static GeneralDownwardRouteAssignment Invalid(string routeId, string reason) =>
        new(routeId, Array.Empty<AssignedRail>(), Array.Empty<RailTransition>(), Array.Empty<Point>(), new[] { reason }, false);
    private static void Add(ICollection<Point> points, Point point)
    {
        if (!points.Contains(point)) points.Add(point);
    }
    private static bool HasImmediateReversal(IReadOnlyList<Point> points) =>
        points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Zip(
            points.Skip(1).Zip(points.Skip(2), (a, b) => new Segment(a, b)),
            (first, second) => first.IsHorizontal == second.IsHorizontal &&
                (first.IsHorizontal
                    ? Math.Sign(first.End.X - first.Start.X) != Math.Sign(second.End.X - second.Start.X)
                    : Math.Sign(first.End.Y - first.Start.Y) != Math.Sign(second.End.Y - second.Start.Y)))
            .Any(item => item);
}
