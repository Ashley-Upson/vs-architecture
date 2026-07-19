using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ReturnLinkCommonAllocator
{
    public static ReturnLinkAssignmentReport Assign(
        IEnumerable<AdjacentDownwardLinkContext> source,
        PlacedGraph placement,
        int separation,
        int padding)
    {
        var nodes = placement.Nodes;
        var allContexts = source.OrderBy(item => item.Route.Link.Id, StringComparer.Ordinal).ToArray();
        var contexts = allContexts.Where(item =>
                !item.ExposureTreeSpecific &&
                item.Source.Node.ProjectId is not null &&
                item.Target.Depth <= item.Source.Depth &&
                item.Route.ExitY == 1 && item.Route.EntryY == 0 &&
                item.Route.SourcePoint.Y == item.Source.Rect.Bottom &&
                item.Route.TargetPoint.Y == item.Target.Rect.Y)
            .OrderBy(item => item.Route.Link.Id, StringComparer.Ordinal).ToArray();
        if (contexts.Length == 0)
            return new ReturnLinkAssignmentReport(Array.Empty<ReturnLinkPlan>(),
                VerticalLinkColumnAllocator.Assign(Array.Empty<VerticalLinkColumnDemand>(), separation),
                Array.Empty<AssignedReturnLinkColumn>(),
                Array.Empty<CommonAuthorityRegionObservation>(),
                Array.Empty<GeneralDownwardLinkAssignment>());

        var owned = contexts.Select(context => new { Context = context, Ownership = Ownership(context, placement, padding) })
            .OrderBy(item => item.Context.Route.Link.Id, StringComparer.Ordinal).ToArray();
        var indexByRoute = owned.GroupBy(item => item.Ownership.Id, StringComparer.Ordinal)
            .SelectMany(group => group.Select((item, index) => new { item.Context.Route.Link.Id, Index = index, Count = group.Count() }))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var plans = owned.Select(item =>
        {
            var context = item.Context;
            var ownership = item.Ownership;
            var lane = indexByRoute[context.Route.Link.Id];
            var left = ownership.OwnershipBounds.X - padding - separation * lane.Count;
            var right = ownership.OwnershipBounds.Right + padding + separation * lane.Count;
            var index = lane.Index;
            var leftPlan = Plan(context, ownership, left, left + separation * index, placement, separation, padding);
            var rightX = right - separation * index;
            var rightPlan = Plan(context, ownership, rightX, rightX, placement, separation, padding);
            return new[] { leftPlan, rightPlan }
                .OrderBy(plan => Math.Abs(plan.ColumnDemand.PreferredX - context.Route.SourcePoint.X) +
                    Math.Abs(plan.ColumnDemand.PreferredX - context.Route.TargetPoint.X))
                .ThenBy(plan => plan.ColumnDemand.PreferredX).First();
        }).ToArray();

        var ordinary = allContexts.SelectMany(item => item.InterLayerDemands)
            .Where(item => item.Role != InterLayerMembershipRole.Return)
            .GroupBy(item => item.Id, StringComparer.Ordinal).Select(item => item.First()).ToArray();
        var regions = plans.SelectMany(plan => new[] { plan.DepartureDemand, plan.ArrivalDemand })
            .GroupBy(demand => InterLayerKey(demand), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var sample = group.First();
                var interLayer = plans.SelectMany(plan => new[]
                    {
                        (plan.DepartureDemand.Id, plan.DepartureInterLayer),
                        (plan.ArrivalDemand.Id, plan.ArrivalInterLayer)
                    }).Single(item => item.Id == sample.Id).Item2;
                var existing = ordinary.Where(item => item.InterLayerId.UpperLayer == interLayer.UpperLayer &&
                        item.InterLayerId.LowerLayer == interLayer.LowerLayer)
                    .Select(item => ExistingDemand(item, sample.AllowedAxisRange, placement.Revision));
                var demands = group.Concat(existing).GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => item.First()).ToArray();
                var region = new LinkSegmentAllocationRegionIdentity(LinkSegmentOrientation.Horizontal,
                    sample.AllowedAxisRange, $"return-slots:{interLayer.UpperLayer}:{interLayer.LowerLayer}",
                    sample.MovementScope, placement.Revision);
                var assignment = DeterministicSlotAllocator.Assign(region, demands,
                    new LinkSegmentAssignmentOptions(separation, padding));
                GenerationConstraint? proposal = null;
                if (assignment.RequiredExtent > region.AllowedAxisRange.Length)
                {
                    var lowerY = nodes.Values.Where(item => !item.IsStandalone && item.Depth == interLayer.LowerLayer)
                        .Select(item => item.Rect.Y).DefaultIfEmpty(region.AllowedAxisRange.Maximum).Min();
                    proposal = LayerSuffixConstraintMaterializer.ProposeMinimumY(region, assignment.RequiredExtent, lowerY);
                }
                return new CommonAuthorityRegionObservation(region, assignment, proposal);
            }).ToArray();
        var assignedSlots = regions.SelectMany(item => item.Assignment.SegmentsByDemandId)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var columnDemands = plans.Select(plan => plan.ColumnDemand with
        {
            VerticalInterval = new AxisInterval(
                assignedSlots[plan.ArrivalDemand.Id].AxisCoordinate,
                assignedSlots[plan.DepartureDemand.Id].AxisCoordinate)
        }).ToArray();
        var columns = VerticalLinkColumnAllocator.Assign(columnDemands, separation);
        var assignments = plans.Select(plan => Compile(plan, contexts.Single(item => item.Route.Link.Id == plan.LogicalRouteId),
            columns.ColumnsByDemandId[plan.ColumnDemand.Id], assignedSlots, nodes)).ToArray();
        var ownedColumns = plans.Select(plan => new AssignedReturnLinkColumn(
            columns.ColumnsByDemandId[plan.ColumnDemand.Id], plan.Ownership)).ToArray();
        return new ReturnLinkAssignmentReport(plans, columns, ownedColumns, regions, assignments);
    }

    private static ReturnLinkPlan Plan(
        AdjacentDownwardLinkContext context,
        ReturnColumnOwnership ownership,
        int minimumX,
        int preferredX,
        PlacedGraph placement,
        int separation,
        int padding)
    {
        var departureInterLayer = new InterLayerId(context.Source.Depth, context.Source.Depth + 1, placement.Revision);
        var arrivalInterLayer = new InterLayerId(context.Target.Depth - 1, context.Target.Depth, placement.Revision);
        var departureRange = AxisRange(context, departureInterLayer, placement, separation, padding);
        var arrivalRange = AxisRange(context, arrivalInterLayer, placement, separation, padding);
        var vertical = new AxisInterval(arrivalRange.Minimum + padding, departureRange.Minimum + padding);
        var demand = new VerticalLinkColumnDemand(
            $"{context.Route.Link.Id}:return-column", context.Route.Link.Id, preferredX,
            new AxisInterval(minimumX, preferredX), context.Source.Depth, context.Target.Depth, vertical,
            padding, context.Source.Node.Id, context.Target.Node.Id, context.Source.Node.ProjectId,
            new MovementScopeIdentity(MovementScopeKind.ProjectRoot, context.Source.Node.ProjectId!),
            context.LayoutRevision, context.RouteRevision);
        return new ReturnLinkPlan(context.Route.Link.Id,
            context.Source.Depth == context.Target.Depth ? ReturnLinkTopologyKind.SameLayer : ReturnLinkTopologyKind.Upward,
            context.Source.Node.Id, context.Target.Node.Id,
            ownership, demand, departureInterLayer, arrivalInterLayer,
            SegmentDemand(context, ownership, departureInterLayer, departureRange,
                context.Route.SourcePoint.X, preferredX, LinkSegmentRole.ReturnDeparture),
            SegmentDemand(context, ownership, arrivalInterLayer, arrivalRange,
                preferredX, context.Route.TargetPoint.X, LinkSegmentRole.ReturnArrival));
    }

    private static LinkSegmentDemand SegmentDemand(
        AdjacentDownwardLinkContext context,
        ReturnColumnOwnership ownership,
        InterLayerId interLayer,
        AxisInterval axisRange,
        int firstX,
        int secondX,
        LinkSegmentRole role) =>
        new($"{context.Route.Link.Id}:{(role == LinkSegmentRole.ReturnDeparture ? "return-departure" : "return-arrival")}",
            context.Route.Link.Id, LinkSegmentOrientation.Horizontal, new AxisInterval(firstX, secondX), axisRange,
            null, role, context.Route.Link.Order, role == LinkSegmentRole.ReturnDeparture ? 0 : 2,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{interLayer.LowerLayer}"),
            context.LayoutRevision, context.RouteRevision, ownership.Id);

    private static AxisInterval AxisRange(
        AdjacentDownwardLinkContext context,
        InterLayerId id,
        PlacedGraph placement,
        int separation,
        int padding)
    {
        var defaultExtent = padding * 2 + separation;
        var lowerNodes = placement.Nodes.Values.Where(item => item.Node.ProjectId is not null &&
            item.Depth == id.LowerLayer).ToArray();
        var upperNodes = placement.Nodes.Values.Where(item => item.Node.ProjectId is not null &&
            item.Depth == id.UpperLayer).ToArray();
        var lower = lowerNodes.Select(item => item.Rect.Y).DefaultIfEmpty(
            upperNodes.Select(item => item.Rect.Bottom).DefaultIfEmpty(0).Max() + defaultExtent).Min();
        var upper = upperNodes.Select(item => item.Rect.Bottom).DefaultIfEmpty(lower - defaultExtent).Max();
        if (lower < upper) lower = upper;
        return new AxisInterval(upper, lower);
    }

    private static LinkSegmentDemand ExistingDemand(
        InterLayerLinkDemand demand,
        AxisInterval range,
        LayoutRevision revision) =>
        new(demand.Id, demand.LogicalEdgeIdentity, LinkSegmentOrientation.Horizontal,
            new AxisInterval(demand.XStart, demand.XEnd), range, null, LinkSegmentRole.Through,
            demand.ConnectionOrder, demand.SegmentIndex,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{demand.InterLayerId.LowerLayer}"),
            revision, demand.RouteRevision);

    private static string InterLayerKey(LinkSegmentDemand demand) =>
        $"{demand.AllowedAxisRange.Minimum}:{demand.AllowedAxisRange.Maximum}:{demand.MovementScope?.Id}";

    internal static ReturnColumnOwnership Ownership(
        AdjacentDownwardLinkContext context, PlacedGraph placement, int padding)
    {
        var order = placement.ProjectPlacement.StableProjectOrder;
        var sourceIndex = context.Source.Node.ProjectId is null ? -1 : order.ToList().IndexOf(context.Source.Node.ProjectId);
        var targetIndex = context.Target.Node.ProjectId is null ? -1 : order.ToList().IndexOf(context.Target.Node.ProjectId);
        var first = sourceIndex < 0 || targetIndex < 0 ? 0 : Math.Min(sourceIndex, targetIndex);
        var last = sourceIndex < 0 || targetIndex < 0 ? order.Count - 1 : Math.Max(sourceIndex, targetIndex);
        var ids = order.Skip(first).Take(last - first + 1).ToArray();
        var layouts = ids.Where(placement.Projects.ContainsKey).Select(id => placement.Projects[id].Rect).ToArray();
        Rect bounds;
        if (layouts.Length == ids.Length && layouts.Length > 0)
            bounds = Union(layouts);
        else
        {
            var ownedNodes = placement.Nodes.Values.Where(node => node.Node.ProjectId is not null &&
                ids.Contains(node.Node.ProjectId, StringComparer.Ordinal)).Select(node => node.Rect).ToArray();
            bounds = Union(ownedNodes).Inflate(padding);
        }
        return new ReturnColumnOwnership(first, last, ids, bounds, placement.Revision);
    }

    private static Rect Union(IReadOnlyList<Rect> rects)
    {
        if (rects.Count == 0) return new Rect(0, 0, 0, 0);
        var left = rects.Min(item => item.X);
        var top = rects.Min(item => item.Y);
        var right = rects.Max(item => item.Right);
        var bottom = rects.Max(item => item.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static GeneralDownwardLinkAssignment Compile(
        ReturnLinkPlan plan,
        AdjacentDownwardLinkContext context,
        AssignedVerticalLinkColumn column,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignedSlots,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var source = context.Route.SourcePoint;
        var target = context.Route.TargetPoint;
        var departureY = assignedSlots[plan.DepartureDemand.Id].AxisCoordinate;
        var arrivalY = assignedSlots[plan.ArrivalDemand.Id].AxisCoordinate;
        var points = AdjacentDownwardLinkDemandDiscovery.Normalize(new[]
        {
            source, new Point(source.X, departureY), new Point(column.X, departureY),
            new Point(column.X, arrivalY), new Point(target.X, arrivalY), target
        });
        var collision = points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Select((segment, index) =>
                new { segment, index })
            .SelectMany(item => nodes.Values.Where(node => node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
                    item.segment.Intersects(node.Rect)).Select(node => new { node.Node.Id, item.index, item.segment }))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.index).FirstOrDefault();
        if (collision is not null)
            return Invalid(plan.LogicalRouteId,
                $"ReturnTopologyBlocked:{collision.Id}:segment-{collision.index}:{collision.segment.Start.X},{collision.segment.Start.Y}-{collision.segment.End.X},{collision.segment.End.Y}");
        if (points.Count < 2 || points[1].X != source.X || points[1].Y <= source.Y ||
            points[points.Count - 2].X != target.X || points[points.Count - 2].Y >= target.Y)
            return Invalid(plan.LogicalRouteId,
                $"ReturnConnectionInvariantFailure:source-{source.X},{source.Y}:departure-{departureY}:arrival-{arrivalY}:target-{target.X},{target.Y}");
        var assigned = points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Select((segment, index) =>
            new AssignedLinkSegment($"{plan.LogicalRouteId}:return:{index}:assigned", $"{plan.LogicalRouteId}:return:{index}",
                plan.LogicalRouteId, segment.IsHorizontal ? LinkSegmentOrientation.Horizontal : LinkSegmentOrientation.Vertical,
                segment.IsHorizontal ? segment.Start.Y : segment.Start.X, index,
                segment.IsHorizontal ? new AxisInterval(segment.Start.X, segment.End.X) : new AxisInterval(segment.Start.Y, segment.End.Y),
                index == 0 ? LinkSegmentRole.ConnectionDeparture :
                index == points.Count - 2 ? LinkSegmentRole.ConnectionArrival : LinkSegmentRole.Return,
                column.PlacementRevision, column.LinkRevision)).ToArray();
        var transitions = points.Skip(1).Take(points.Count - 2).Select((point, index) =>
            new LinkTransition($"{plan.LogicalRouteId}:return-transition:{index}", plan.LogicalRouteId,
                assigned[index].Id, assigned[index + 1].Id, point, index, column.PlacementRevision, column.LinkRevision)).ToArray();
        return new GeneralDownwardLinkAssignment(plan.LogicalRouteId, assigned, transitions, points, Array.Empty<string>(), true);
    }

    private static GeneralDownwardLinkAssignment Invalid(string id, string reason) =>
        new(id, Array.Empty<AssignedLinkSegment>(), Array.Empty<LinkTransition>(), Array.Empty<Point>(), new[] { reason }, false);
}
