using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ReturnLinkCommonAllocator
{
    public static ReturnLinkAssignmentReport Assign(
        IEnumerable<AdjacentDownwardLinkContext> source,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        int separation,
        int padding)
    {
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
                Array.Empty<GeneralDownwardLinkAssignment>());

        var left = nodes.Values.Min(item => item.Rect.X) - padding - separation * contexts.Length;
        var right = nodes.Values.Max(item => item.Rect.Right) + padding + separation * contexts.Length;
        var plans = contexts.Select((context, index) =>
        {
            var leftPlan = Plan(context, left, left + separation * index, padding);
            var rightX = right - separation * index;
            var rightPlan = Plan(context, rightX, rightX, padding);
            return new[] { leftPlan, rightPlan }.OrderBy(plan => CollisionCount(plan, context, nodes))
                .ThenBy(plan => Math.Abs(plan.ColumnDemand.PreferredX - context.Route.SourcePoint.X) +
                    Math.Abs(plan.ColumnDemand.PreferredX - context.Route.TargetPoint.X))
                .ThenBy(plan => plan.ColumnDemand.PreferredX).First();
        }).ToArray();
        var columns = VerticalLinkColumnAllocator.Assign(plans.Select(item => item.ColumnDemand), separation);
        var assignments = plans.Select(plan => Compile(plan, contexts.Single(item => item.Route.Link.Id == plan.LogicalRouteId),
            columns.ColumnsByDemandId[plan.ColumnDemand.Id], nodes, padding)).ToArray();
        return new ReturnLinkAssignmentReport(plans, columns, assignments);
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
            context.Source.Node.Id, context.Target.Node.Id, demand, departureY, arrivalY);
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
