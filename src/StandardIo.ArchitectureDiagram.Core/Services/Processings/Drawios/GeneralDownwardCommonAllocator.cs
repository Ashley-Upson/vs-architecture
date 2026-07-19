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
        var eligible = report.Routes.Where(item => item.Eligible).ToArray();
        var timer = Stopwatch.StartNew();
        var regions = eligible.SelectMany(item => item.Demands)
            .Where(item => item.Role == LinkSegmentRole.Through)
            .GroupBy(RegionKey).OrderBy(item => item.Key, StringComparer.Ordinal).Select(group =>
            {
                var sample = group.First();
                var region = new LinkSegmentAllocationRegionIdentity(sample.Orientation, sample.AllowedAxisRange,
                    group.Key, sample.MovementScope, sample.PlacementRevision);
                var assignment = DeterministicSlotAllocator.Assign(region, group,
                    new LinkSegmentAssignmentOptions(separation, padding));
                GenerationConstraint? proposal = null;
                if (assignment.RequiredExtent > region.AllowedAxisRange.Length)
                {
                    var lowerDepth = int.Parse(region.MovementScope!.Value.Id.Substring("depth:".Length));
                    var currentLowerY = nodes.Values.Where(item => !item.IsStandalone && item.Depth == lowerDepth)
                        .Select(item => item.Rect.Y).DefaultIfEmpty(region.AllowedAxisRange.Maximum).Min();
                    proposal = LayerSuffixConstraintMaterializer.ProposeMinimumY(
                        region, assignment.RequiredExtent, currentLowerY);
                }
                return new SlotRegionAssignment(region, assignment,
                    proposal);
            }).ToArray();
        timer.Stop();
        var verticalDemands = eligible.SelectMany(item => item.VerticalColumnDemands).ToArray();
        VerticalLinkColumnAssignment verticalColumns;
        try
        {
            verticalColumns = VerticalLinkColumnAllocator.Assign(verticalDemands, separation);
        }
        catch (InvalidOperationException)
        {
            verticalColumns = new VerticalLinkColumnAssignment(verticalDemands.ToDictionary(item => item.Id, item =>
                new AssignedVerticalLinkColumn(item.Id, item.LinkId, item.PreferredX, item.SourceLayer,
                    item.DestinationLayer, item.VerticalInterval, 0, item.PlacementRevision, item.LinkRevision),
                StringComparer.Ordinal), 1, 0);
        }
        var assignedByDemand = regions.SelectMany(item => item.Assignment.SegmentsByDemandId)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var transitionTimer = Stopwatch.StartNew();
        var routes = eligible.OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .Select(plan => Reconstruct(plan, assignedByDemand, verticalColumns.ColumnsByDemandId, nodes)).ToArray();
        transitionTimer.Stop();
        return new GeneralDownwardAssignmentReport(regions, verticalColumns, routes,
            timer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency,
            transitionTimer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
    }

    private static GeneralDownwardLinkAssignment Reconstruct(
        GeneralDownwardLinkPlan plan,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignedByDemand,
        IReadOnlyDictionary<string, AssignedVerticalLinkColumn> columnsByDemand,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var throughDemands = plan.Demands.Where(item => item.Role == LinkSegmentRole.Through)
            .OrderBy(item => item.TurnOrder).ToArray();
        if (throughDemands.Any(item => !assignedByDemand.ContainsKey(item.Id)))
            return Invalid(plan.LogicalRouteId, "A crossed-interLayer segment was not assigned.");
        var through = throughDemands.Select(item => assignedByDemand[item.Id]).ToArray();
        var source = plan.CanonicalAuthoritativePoints[0];
        var target = plan.CanonicalAuthoritativePoints[plan.CanonicalAuthoritativePoints.Count - 1];
        if (plan.VerticalColumnDemands.Count > 0)
            return ReconstructVerticalColumn(plan, through, columnsByDemand, nodes, source, target);
        var departure = ConnectionSegment(plan, LinkSegmentRole.ConnectionDeparture, source.X,
            new AxisInterval(source.Y, through[0].AxisCoordinate));
        var arrival = ConnectionSegment(plan, LinkSegmentRole.ConnectionArrival, target.X,
            new AxisInterval(through[through.Length - 1].AxisCoordinate, target.Y));
        var segments = new[] { departure }.Concat(through).Concat(new[] { arrival }).ToArray();
        var points = new List<Point> { source };
        var transitions = new List<LinkTransition>();
        var currentX = source.X;
        for (var index = 0; index < through.Length; index++)
        {
            var segment = through[index];
            var nextX = target.X;
            var firstTurn = new Point(currentX, segment.AxisCoordinate);
            var secondTurn = new Point(nextX, segment.AxisCoordinate);
            Add(points, firstTurn);
            Add(points, secondTurn);
            transitions.Add(new LinkTransition($"{plan.LogicalRouteId}:turn:{index}:entry",
                plan.LogicalRouteId, index == 0 ? departure.Id : through[index - 1].Id,
                segment.Id, firstTurn, transitions.Count, segment.PlacementRevision, segment.RouteRevision));
            if (index + 1 < through.Length)
            {
                var vertical = new Segment(secondTurn, new Point(nextX, through[index + 1].AxisCoordinate));
                var obstacle = nodes.Values.FirstOrDefault(node =>
                    node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId && vertical.Intersects(node.Rect));
                if (obstacle is not null)
                    return Invalid(plan.LogicalRouteId, $"ObstacleBypassRequired:{obstacle.Node.Id}");
            }
            currentX = nextX;
        }
        Add(points, target);
        var normalized = LogicalRouteNormalizer.NormalizePoints(points);
        if (normalized.Zip(normalized.Skip(1), (a, b) => new Segment(a, b)).Any(item => !item.IsOrthogonal) ||
            HasImmediateReversal(normalized))
            return Invalid(plan.LogicalRouteId, "ReconstructionInvariantFailure");
        return new GeneralDownwardLinkAssignment(plan.LogicalRouteId, segments, transitions,
            normalized, Array.Empty<string>(), true);
    }

    private static GeneralDownwardLinkAssignment ReconstructVerticalColumn(
        GeneralDownwardLinkPlan plan,
        IReadOnlyList<AssignedLinkSegment> through,
        IReadOnlyDictionary<string, AssignedVerticalLinkColumn> columnsByDemand,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        Point source,
        Point target)
    {
        if (through.Count != 1 || plan.VerticalColumnDemands.Count != 1)
            return Invalid(plan.LogicalRouteId, "VerticalColumnTopologyCardinality");
        var demand = plan.VerticalColumnDemands[0];
        if (!columnsByDemand.TryGetValue(demand.Id, out var column))
            return Invalid(plan.LogicalRouteId, "VerticalColumnUnassigned");
        if (column.X != target.X)
            return Invalid(plan.LogicalRouteId, "DestinationColumnMovementRequired");
        var horizontal = through[0];
        var departure = ConnectionSegment(plan, LinkSegmentRole.ConnectionDeparture, source.X,
            new AxisInterval(source.Y, horizontal.AxisCoordinate));
        var vertical = new Segment(new Point(column.X, horizontal.AxisCoordinate), target);
        var blocker = nodes.Values.FirstOrDefault(node =>
            node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
            vertical.Intersects(Inflate(node.Rect, demand.RequiredClearance)));
        if (blocker is not null)
            return Invalid(plan.LogicalRouteId, $"VerticalColumnBlocked:{blocker.Node.Id}");
        var assignedColumn = new AssignedLinkSegment(
            $"{demand.Id}:assigned", demand.Id, demand.LinkId, LinkSegmentOrientation.Vertical,
            column.X, column.ColumnIndex, new AxisInterval(horizontal.AxisCoordinate, target.Y),
            LinkSegmentRole.ConnectionArrival, column.PlacementRevision, column.LinkRevision);
        var points = LogicalRouteNormalizer.NormalizePoints(new[]
        {
            source,
            new Point(source.X, horizontal.AxisCoordinate),
            new Point(column.X, horizontal.AxisCoordinate),
            target
        });
        return new GeneralDownwardLinkAssignment(plan.LogicalRouteId,
            new[] { departure, horizontal, assignedColumn },
            new[]
            {
                new LinkTransition($"{plan.LogicalRouteId}:transition:departure", plan.LogicalRouteId,
                    departure.Id, horizontal.Id, points[1], 0, column.PlacementRevision, column.LinkRevision),
                new LinkTransition($"{plan.LogicalRouteId}:transition:column", plan.LogicalRouteId,
                    horizontal.Id, assignedColumn.Id, points[2], 1, column.PlacementRevision, column.LinkRevision)
            }, points, Array.Empty<string>(), true);
    }

    private static Rect Inflate(Rect rect, int clearance) => new(
        rect.X - clearance, rect.Y - clearance,
        rect.Width + clearance * 2, rect.Height + clearance * 2);

    private static AssignedLinkSegment ConnectionSegment(
        GeneralDownwardLinkPlan route,
        LinkSegmentRole role,
        int axis,
        AxisInterval occupied)
    {
        var demand = route.Demands.Single(item => item.Role == role);
        return new AssignedLinkSegment($"{demand.Id}:assigned", demand.Id, route.LogicalRouteId,
            LinkSegmentOrientation.Vertical, axis, 0, occupied, role, demand.PlacementRevision, demand.RouteRevision);
    }

    private static string RegionKey(LinkSegmentDemand demand) =>
        $"{demand.Orientation}:{demand.AllowedAxisRange.Minimum}:{demand.AllowedAxisRange.Maximum}:" +
        $"{demand.MovementScope?.Kind}:{demand.MovementScope?.Id}:{demand.PlacementRevision.Value}";
    private static GeneralDownwardLinkAssignment Invalid(string routeId, string reason) =>
        new(routeId, Array.Empty<AssignedLinkSegment>(), Array.Empty<LinkTransition>(), Array.Empty<Point>(), new[] { reason }, false);
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
