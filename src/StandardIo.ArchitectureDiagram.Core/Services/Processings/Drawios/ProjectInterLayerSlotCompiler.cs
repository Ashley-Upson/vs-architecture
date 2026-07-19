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
        var bands = Bands(nodes, revision, padding);
        var demands = new List<LinkSegmentDemand>();
        foreach (var plan in plans.Values.OrderBy(item => terminalLayouts[item.LogicalRouteId].Link.Order)
                     .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal))
        {
            var route = terminalLayouts[plan.LogicalRouteId];
            var departureId = BandForDeparture(plan, nodes, bands);
            demands.Add(Demand(plan, route, departureId, DepartureRole(plan.Family), 0, bands[departureId], revision));
            if (plan.RequiresReturnColumn)
            {
                var arrivalId = BandForArrival(plan, nodes, bands);
                demands.Add(Demand(plan, route, arrivalId, LinkSegmentRole.ReturnArrival, 1, bands[arrivalId], revision));
            }
        }

        var assignments = new Dictionary<string, AssignedLinkSegment>(StringComparer.Ordinal);
        foreach (var group in demands.GroupBy(item => item.AllowedAxisRange)
                     .OrderBy(item => item.Key.Minimum).ThenBy(item => item.Key.Maximum))
        {
            var sample = group.First();
            var identity = new LinkSegmentAllocationRegionIdentity(
                LinkSegmentOrientation.Horizontal, group.Key,
                $"project-interLayer:{group.Key.Minimum}:{group.Key.Maximum}",
                sample.MovementScope, revision);
            var assigned = DeterministicSlotAllocator.Assign(identity, group,
                new LinkSegmentAssignmentOptions(separation, padding));
            foreach (var item in assigned.SegmentsByDemandId) assignments.Add(item.Key, item.Value);
        }

        var returnOrder = plans.Values.Where(item => item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .Select((plan, index) => (plan.LogicalRouteId, index))
            .ToDictionary(item => item.LogicalRouteId, item => item.index, StringComparer.Ordinal);
        var minimumX = nodes.Values.Min(item => item.Rect.X);
        var links = plans.Values.OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToDictionary(
            plan => plan.LogicalRouteId,
            plan => Materialize(plan, terminalLayouts[plan.LogicalRouteId], demands, assignments,
                minimumX - padding - separation * (returnOrder.TryGetValue(plan.LogicalRouteId, out var index) ? index + 1 : 1)),
            StringComparer.Ordinal);
        return new ProjectSlotCompilation(links, demands, assignments, bands.Count, 0);
    }

    private static LinkLayout Materialize(
        CanonicalTopologyPlan plan,
        LinkLayout route,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        int returnX)
    {
        var routeDemands = demands.Where(item => item.LogicalRouteId == plan.LogicalRouteId)
            .OrderBy(item => item.TurnOrder).ToArray();
        var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
        IReadOnlyList<Point> points;
        if (plan.RequiresReturnColumn)
        {
            var arrivalY = assignments[routeDemands[1].Id].AxisCoordinate;
            points = new[]
            {
                new Point(route.SourcePoint.X, departureY), new Point(returnX, departureY),
                new Point(returnX, arrivalY), new Point(route.TargetPoint.X, arrivalY)
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
        LayoutRevision revision) => new(
            $"{plan.LogicalRouteId}:horizontal:{order}", plan.LogicalRouteId,
            LinkSegmentOrientation.Horizontal,
            new AxisInterval(route.SourcePoint.X, route.TargetPoint.X), range, null, role,
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
        int padding)
    {
        var layers = nodes.Values.Where(item => !item.IsStandalone).GroupBy(item => item.Depth)
            .ToDictionary(item => item.Key, item => item.ToArray());
        var result = new Dictionary<InterLayerId, AxisInterval>();
        foreach (var upper in layers.Keys.OrderBy(item => item))
        {
            var upperBottom = layers[upper].Max(item => item.Rect.Bottom);
            var lowerTop = layers.TryGetValue(upper + 1, out var lower)
                ? lower.Min(item => item.Rect.Y) : upperBottom + padding * 4;
            result[new InterLayerId(upper, upper + 1, revision)] = new AxisInterval(upperBottom, lowerTop);
        }
        return result;
    }
}
