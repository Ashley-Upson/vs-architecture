using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DownwardLinkSegmentDemandFactory
{
    public static DownwardLinkTopologyDemand Create(
        AdjacentDownwardLinkContext context,
        IReadOnlyList<InterLayerId> crossedInterLayers,
        int requiredClearance = 0)
    {
        if (crossedInterLayers.Count == 0)
            return new DownwardLinkTopologyDemand(Array.Empty<LinkSegmentDemand>(), Array.Empty<VerticalLinkColumnDemand>());
        var sourceX = context.Route.SourcePoint.X;
        var targetX = context.Route.TargetPoint.X;
        var demands = new List<LinkSegmentDemand>
        {
            new($"{context.Route.Link.Id}:segment:departure", context.Route.Link.Id, LinkSegmentOrientation.Vertical,
                new AxisInterval(context.Route.SourcePoint.Y, context.InterLayerAxisRanges[crossedInterLayers[0]].Maximum),
                new AxisInterval(sourceX, sourceX), sourceX, LinkSegmentRole.ConnectionDeparture,
                context.Route.Link.Order, 0, new MovementScopeIdentity(MovementScopeKind.Node, context.Source.Node.Id),
                context.LayoutRevision, context.RouteRevision)
        };
        if (crossedInterLayers.Count == 1)
        {
            var interLayer = crossedInterLayers[0];
            demands.Add(new LinkSegmentDemand(
                $"{context.Route.Link.Id}:segment:through", context.Route.Link.Id,
                LinkSegmentOrientation.Horizontal, new AxisInterval(sourceX, targetX),
                context.InterLayerAxisRanges[interLayer], null, LinkSegmentRole.Through, context.Route.Link.Order, 1,
                new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{interLayer.LowerLayer}"),
                context.LayoutRevision, context.RouteRevision));
            demands.Add(new LinkSegmentDemand(
                $"{context.Route.Link.Id}:segment:arrival", context.Route.Link.Id, LinkSegmentOrientation.Vertical,
                new AxisInterval(context.InterLayerAxisRanges[interLayer].Minimum, context.Route.TargetPoint.Y),
                new AxisInterval(targetX, targetX), targetX, LinkSegmentRole.ConnectionArrival,
                context.Route.Link.Order, 2, new MovementScopeIdentity(MovementScopeKind.Node, context.Target.Node.Id),
                context.LayoutRevision, context.RouteRevision));
            return new DownwardLinkTopologyDemand(demands, Array.Empty<VerticalLinkColumnDemand>());
        }

        var departureInterLayer = crossedInterLayers[0];
        demands.Add(new LinkSegmentDemand(
            $"{context.Route.Link.Id}:segment:horizontal-departure", context.Route.Link.Id,
            LinkSegmentOrientation.Horizontal, new AxisInterval(sourceX, targetX),
            context.InterLayerAxisRanges[departureInterLayer], null, LinkSegmentRole.Through,
            context.Route.Link.Order, 1,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{departureInterLayer.LowerLayer}"),
            context.LayoutRevision, context.RouteRevision));
        var column = new VerticalLinkColumnDemand(
            $"{context.Route.Link.Id}:vertical-column", context.Route.Link.Id, targetX,
            new AxisInterval(targetX, targetX),
            context.Source.Depth, context.Target.Depth,
            new AxisInterval(context.Route.SourcePoint.Y, context.Route.TargetPoint.Y),
            requiredClearance, context.Source.Node.Id, context.Target.Node.Id, context.Source.Node.ProjectId,
            new MovementScopeIdentity(MovementScopeKind.LayoutSubtree, context.Target.Node.Id),
            context.LayoutRevision, context.RouteRevision);
        return new DownwardLinkTopologyDemand(demands, new[] { column });
    }
}
