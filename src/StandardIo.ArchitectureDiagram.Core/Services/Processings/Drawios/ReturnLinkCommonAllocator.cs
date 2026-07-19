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
        var contexts = source.Where(item =>
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
            var leftPlan = Plan(context, left, left + separation * index, padding);
            var rightX = right - separation * index;
            var rightPlan = Plan(context, rightX, rightX, padding);
            return new[] { leftPlan, rightPlan }.Select(plan => plan with { Ownership = ownership })
                .OrderBy(plan => CollisionCount(plan, context, nodes))
                .ThenBy(plan => Math.Abs(plan.ColumnDemand.PreferredX - context.Route.SourcePoint.X) +
                    Math.Abs(plan.ColumnDemand.PreferredX - context.Route.TargetPoint.X))
                .ThenBy(plan => plan.ColumnDemand.PreferredX).First();
        }).ToArray();
        var columns = VerticalLinkColumnAllocator.Assign(plans.Select(item => item.ColumnDemand), separation);
        var assignments = plans.Select(plan => Compile(plan, contexts.Single(item => item.Route.Link.Id == plan.LogicalRouteId),
            columns.ColumnsByDemandId[plan.ColumnDemand.Id], nodes, padding)).ToArray();
        var ownedColumns = plans.Select(plan => new AssignedReturnLinkColumn(
            columns.ColumnsByDemandId[plan.ColumnDemand.Id], plan.Ownership)).ToArray();
        return new ReturnLinkAssignmentReport(plans, columns, ownedColumns, assignments);
    }

    private static ReturnLinkPlan Plan(
        AdjacentDownwardLinkContext context,
        int minimumX,
        int preferredX,
        int padding)
    {
        var departureY = context.Source.Rect.Bottom + padding;
        var arrivalY = context.Target.Rect.Y - padding;
        var vertical = new AxisInterval(arrivalY, departureY);
        var demand = new VerticalLinkColumnDemand(
            $"{context.Route.Link.Id}:return-column", context.Route.Link.Id, preferredX,
            new AxisInterval(minimumX, preferredX), context.Source.Depth, context.Target.Depth, vertical,
            padding, context.Source.Node.Id, context.Target.Node.Id, context.Source.Node.ProjectId,
            new MovementScopeIdentity(MovementScopeKind.ProjectRoot, context.Source.Node.ProjectId!),
            context.LayoutRevision, context.RouteRevision);
        return new ReturnLinkPlan(context.Route.Link.Id,
            context.Source.Depth == context.Target.Depth ? ReturnLinkTopologyKind.SameLayer : ReturnLinkTopologyKind.Upward,
            context.Source.Node.Id, context.Target.Node.Id,
            new ReturnColumnOwnership(0, 0, Array.Empty<string>(), new Rect(0, 0, 0, 0), context.LayoutRevision),
            demand, departureY, arrivalY);
    }

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
        IReadOnlyDictionary<string, NodeLayout> nodes,
        int padding)
    {
        var source = context.Route.SourcePoint;
        var target = context.Route.TargetPoint;
        var points = AdjacentDownwardLinkDemandDiscovery.Normalize(new[]
        {
            source, new Point(source.X, plan.DepartureY), new Point(column.X, plan.DepartureY),
            new Point(column.X, plan.ArrivalY), new Point(target.X, plan.ArrivalY), target
        });
        var collision = points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).SelectMany(segment =>
                nodes.Values.Where(node => node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
                    segment.Intersects(node.Rect)).Select(node => node.Node.Id))
            .OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (collision is not null)
            return Invalid(plan.LogicalRouteId, $"ReturnTopologyBlocked:{collision}");
        if (points.Count < 2 || points[1].X != source.X || points[1].Y <= source.Y ||
            points[points.Count - 2].X != target.X || points[points.Count - 2].Y >= target.Y)
            return Invalid(plan.LogicalRouteId, "ReturnConnectionInvariantFailure");
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

    private static int CollisionCount(
        ReturnLinkPlan plan,
        AdjacentDownwardLinkContext context,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var points = new[]
        {
            context.Route.SourcePoint,
            new Point(context.Route.SourcePoint.X, plan.DepartureY),
            new Point(plan.ColumnDemand.PreferredX, plan.DepartureY),
            new Point(plan.ColumnDemand.PreferredX, plan.ArrivalY),
            new Point(context.Route.TargetPoint.X, plan.ArrivalY),
            context.Route.TargetPoint
        };
        return points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Sum(segment =>
            nodes.Values.Count(node => node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
                segment.Intersects(node.Rect)));
    }

    private static GeneralDownwardLinkAssignment Invalid(string id, string reason) =>
        new(id, Array.Empty<AssignedLinkSegment>(), Array.Empty<LinkTransition>(), Array.Empty<Point>(), new[] { reason }, false);
}
