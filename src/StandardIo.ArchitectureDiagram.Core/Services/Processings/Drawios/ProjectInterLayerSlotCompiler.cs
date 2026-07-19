using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectInterLayerSlotCompiler
{
    public static ProjectSlotCompilation Compile(
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> terminalLayouts,
        LayoutRevision revision,
        int separation,
        int padding)
    {
        var bands = Bands(nodes, revision, padding, separation, terminalLayouts.Count);
        var projectHorizontalSpan = new AxisInterval(
            nodes.Values.Min(item => item.Rect.X) - padding,
            nodes.Values.Max(item => item.Rect.Right) + padding);
        var demands = new List<LinkSegmentDemand>();
        foreach (var plan in plans.Values.OrderBy(item => terminalLayouts[item.LogicalRouteId].Link.Order)
                     .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal))
        {
            var route = terminalLayouts[plan.LogicalRouteId];
            var departureId = BandForDeparture(plan, nodes, bands);
            demands.Add(Demand(plan, route, departureId, DepartureRole(plan.Family), 0,
                bands[departureId], revision, projectHorizontalSpan));
            if (plan.RequiresReturnColumn || plan.RequiresDestinationColumn)
            {
                var arrivalId = BandForArrival(plan, nodes, bands);
                demands.Add(Demand(plan, route, arrivalId,
                    plan.RequiresReturnColumn ? LinkSegmentRole.ReturnArrival : LinkSegmentRole.LongArrival,
                    1, bands[arrivalId], revision, projectHorizontalSpan));
            }
        }

        var assignments = new Dictionary<string, AssignedLinkSegment>(StringComparer.Ordinal);
        var requiredExpansion = new Dictionary<int, int>();
        foreach (var group in demands.GroupBy(item =>
                     $"{item.MovementScope?.Id}:{item.AllowedAxisRange.Minimum}:{item.AllowedAxisRange.Maximum}",
                     StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var sample = group.First();
            var allowedRange = sample.AllowedAxisRange;
            var identity = new LinkSegmentAllocationRegionIdentity(
                LinkSegmentOrientation.Horizontal, allowedRange,
                $"project-interLayer:{group.Key}",
                sample.MovementScope, revision);
            var assigned = DeterministicSlotAllocator.Assign(identity, group,
                new LinkSegmentAssignmentOptions(separation, padding));
            foreach (var item in assigned.SegmentsByDemandId) assignments.Add(item.Key, item.Value);
            var missing = Math.Max(0, assigned.RequiredExtent - allowedRange.Length);
            if (missing > 0)
            {
                var depth = int.Parse(sample.MovementScope!.Value.Id.Substring("depth:".Length));
                requiredExpansion[depth] = Math.Max(
                    requiredExpansion.TryGetValue(depth, out var existing) ? existing : 0, missing);
            }
        }

        var returnOrder = plans.Values.Where(item => item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .Select((plan, index) => (plan.LogicalRouteId, index))
            .ToDictionary(item => item.LogicalRouteId, item => item.index, StringComparer.Ordinal);
        var minimumX = nodes.Values.Min(item => item.Rect.X);
        var maximumX = nodes.Values.Max(item => item.Rect.Right);
        var returnSides = plans.Values.Where(item => item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .ToDictionary(item => item.LogicalRouteId, item =>
            {
                var route = terminalLayouts[item.LogicalRouteId];
                var leftCost = route.SourcePoint.X - minimumX + route.TargetPoint.X - minimumX;
                var rightCost = maximumX - route.SourcePoint.X + maximumX - route.TargetPoint.X;
                return leftCost <= rightCost ? "Left" : "Right";
            }, StringComparer.Ordinal);
        var verticalDemands = plans.Values.Where(item => item.RequiresDestinationColumn || item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).Select(plan =>
            {
                var route = terminalLayouts[plan.LogicalRouteId];
                var routeDemands = demands.Where(item => item.LogicalRouteId == plan.LogicalRouteId)
                    .OrderBy(item => item.TurnOrder).ToArray();
                var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
                var arrivalY = assignments[routeDemands[1].Id].AxisCoordinate;
                var interval = new AxisInterval(departureY, arrivalY);
                if (plan.RequiresReturnColumn)
                {
                    var lane = returnOrder[plan.LogicalRouteId] + 1;
                    var left = returnSides[plan.LogicalRouteId] == "Left";
                    var preferred = left ? minimumX - padding - separation * lane : maximumX + padding + separation * lane;
                    return new VerticalLinkColumnDemand(
                        $"{plan.LogicalRouteId}:return-column", plan.LogicalRouteId, preferred,
                        new AxisInterval(preferred, preferred), nodes[plan.SourceNodeId].Depth, nodes[plan.TargetNodeId].Depth,
                        interval, padding, plan.SourceNodeId, plan.TargetNodeId, nodes[plan.SourceNodeId].Node.ProjectId,
                        null, revision, new RouteRevision(0));
                }
                var allowed = new AxisInterval(minimumX - padding - separation * plans.Count,
                    maximumX + padding + separation * plans.Count);
                var forbidden = nodes.Values.Where(node => node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
                        PositiveOverlap(interval, new AxisInterval(node.Rect.Y - padding, node.Rect.Bottom + padding)))
                    .Select(node => new AxisInterval(node.Rect.X - padding, node.Rect.Right + padding))
                    .Concat(FixedColumnExclusions(
                        plan, plans, terminalLayouts, demands, assignments, interval, separation))
                    .ToArray();
                return new VerticalLinkColumnDemand(
                    $"{plan.LogicalRouteId}:destination-column", plan.LogicalRouteId, route.TargetPoint.X,
                    allowed, nodes[plan.SourceNodeId].Depth, nodes[plan.TargetNodeId].Depth, interval, padding,
                    plan.SourceNodeId, plan.TargetNodeId, nodes[plan.TargetNodeId].Node.ProjectId, null,
                    revision, new RouteRevision(0), forbidden);
            }).ToArray();
        var verticalColumns = VerticalLinkColumnAllocator.Assign(verticalDemands, separation);
        var links = plans.Values.OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToDictionary(
            plan => plan.LogicalRouteId,
            plan => Materialize(plan, terminalLayouts[plan.LogicalRouteId], demands, assignments, verticalColumns),
            StringComparer.Ordinal);
        return new ProjectSlotCompilation(
            links, demands, assignments, verticalColumns, returnSides, requiredExpansion,
            bands.Count, requiredExpansion.Count);
    }

    private static LinkLayout Materialize(
        CanonicalTopologyPlan plan,
        LinkLayout route,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        VerticalLinkColumnAssignment verticalColumns)
    {
        var routeDemands = demands.Where(item => item.LogicalRouteId == plan.LogicalRouteId)
            .OrderBy(item => item.TurnOrder).ToArray();
        var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
        IReadOnlyList<Point> points;
        if (plan.RequiresReturnColumn || plan.RequiresDestinationColumn)
        {
            var arrivalY = assignments[routeDemands[1].Id].AxisCoordinate;
            var demandId = plan.RequiresReturnColumn
                ? $"{plan.LogicalRouteId}:return-column"
                : $"{plan.LogicalRouteId}:destination-column";
            var columnX = verticalColumns.ColumnsByDemandId[demandId].X;
            points = new[]
            {
                new Point(route.SourcePoint.X, departureY), new Point(columnX, departureY),
                new Point(columnX, arrivalY), new Point(route.TargetPoint.X, arrivalY)
            };
        }
        else
        {
            points = new[]
            {
                new Point(route.SourcePoint.X, departureY),
                new Point(route.TargetPoint.X, departureY)
            };
        }
        return route.AcceptGeometry(
            new[] { route.SourcePoint }.Concat(points).Concat(new[] { route.TargetPoint }),
            LogicalRouteStage.Allocated, nameof(ProjectInterLayerSlotCompiler));
    }

    private static LinkSegmentDemand Demand(
        CanonicalTopologyPlan plan,
        LinkLayout route,
        InterLayerId band,
        LinkSegmentRole role,
        int order,
        AxisInterval range,
        LayoutRevision revision,
        AxisInterval projectHorizontalSpan) => new(
            $"{plan.LogicalRouteId}:horizontal:{order}", plan.LogicalRouteId,
            LinkSegmentOrientation.Horizontal,
            plan.RequiresDestinationColumn || plan.RequiresReturnColumn
                ? projectHorizontalSpan : new AxisInterval(route.SourcePoint.X, route.TargetPoint.X),
            range, null, role,
            route.Link.Order, order,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{band.LowerLayer}"),
            revision, new RouteRevision(0));

    private static LinkSegmentRole DepartureRole(CanonicalTopologyFamily family) => family switch
    {
        CanonicalTopologyFamily.AdjacentDownward => LinkSegmentRole.AdjacentDeparture,
        CanonicalTopologyFamily.LongDownward => LinkSegmentRole.LongDeparture,
        CanonicalTopologyFamily.SameLayerReturn or CanonicalTopologyFamily.UpwardReturn => LinkSegmentRole.ReturnDeparture,
        _ => LinkSegmentRole.BoundaryHorizontal
    };

    private static InterLayerId BandForDeparture(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands)
    {
        var source = nodes[plan.SourceNodeId];
        return ClosestBand(bands, source.Depth);
    }

    private static InterLayerId BandForArrival(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands)
    {
        var target = nodes[plan.TargetNodeId];
        return ClosestBand(bands, Math.Max(-1, target.Depth - 1));
    }

    private static InterLayerId ClosestBand(IReadOnlyDictionary<InterLayerId, AxisInterval> bands, int upper) =>
        bands.Keys.OrderBy(id => Math.Abs(id.UpperLayer - upper)).ThenBy(id => id.UpperLayer).First();

    private static IReadOnlyDictionary<InterLayerId, AxisInterval> Bands(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        LayoutRevision revision,
        int padding,
        int separation,
        int routeCount)
    {
        var layers = nodes.Values.Where(item => item.Node.ProjectId is not null).GroupBy(item => item.Depth)
            .ToDictionary(item => item.Key, item => item.ToArray());
        var result = new Dictionary<InterLayerId, AxisInterval>();
        foreach (var upper in layers.Keys.OrderBy(item => item))
        {
            var upperBottom = layers[upper].Max(item => item.Rect.Bottom);
            var lowerTop = layers.TryGetValue(upper + 1, out var lower)
                ? lower.Min(item => item.Rect.Y)
                : upperBottom + padding * 2 + Math.Max(1, routeCount) * separation;
            result[new InterLayerId(upper, upper + 1, revision)] = new AxisInterval(upperBottom, lowerTop);
        }
        return result;
    }

    private static bool PositiveOverlap(AxisInterval first, AxisInterval second) =>
        Math.Min(first.Maximum, second.Maximum) > Math.Max(first.Minimum, second.Minimum);

    private static IEnumerable<AxisInterval> FixedColumnExclusions(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, LinkLayout> routes,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        AxisInterval verticalInterval,
        int separation)
    {
        foreach (var other in plans.Values.Where(item => item.LogicalRouteId != plan.LogicalRouteId)
                     .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal))
        {
            var route = routes[other.LogicalRouteId];
            var routeDemands = demands.Where(item => item.LogicalRouteId == other.LogicalRouteId)
                .OrderBy(item => item.TurnOrder).ToArray();
            var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
            var sourceInterval = new AxisInterval(route.SourcePoint.Y, departureY);
            if (PositiveOverlap(verticalInterval, sourceInterval))
                yield return new AxisInterval(route.SourcePoint.X - separation, route.SourcePoint.X + separation);
            if (other.RequiresDestinationColumn || other.RequiresReturnColumn) continue;
            var arrivalInterval = new AxisInterval(departureY, route.TargetPoint.Y);
            if (PositiveOverlap(verticalInterval, arrivalInterval))
                yield return new AxisInterval(route.TargetPoint.X - separation, route.TargetPoint.X + separation);
        }
    }
}
