using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DownwardLinkSegmentDemandFactory
{
    public static (IReadOnlyList<LinkSegmentDemand> Demands, IReadOnlyList<int> TransitionXs) Create(
        AdjacentDownwardLinkContext context,
        IReadOnlyList<InterLayerId> crossedInterLayers)
    {
        if (crossedInterLayers.Count == 0) return (Array.Empty<LinkSegmentDemand>(), Array.Empty<int>());
        var sourceX = context.Route.SourcePoint.X;
        var targetX = context.Route.TargetPoint.X;
        var transitionXs = Enumerable.Range(1, crossedInterLayers.Count)
            .Select(index => sourceX + (int)Math.Round((targetX - sourceX) * index / (double)crossedInterLayers.Count))
            .ToArray();
        var demands = new List<LinkSegmentDemand>
        {
            new($"{context.Route.Link.Id}:segment:departure", context.Route.Link.Id, LinkSegmentOrientation.Vertical,
                new AxisInterval(context.Route.SourcePoint.Y, context.InterLayerAxisRanges[crossedInterLayers[0]].Maximum),
                new AxisInterval(sourceX, sourceX), sourceX, LinkSegmentRole.ConnectionDeparture,
                context.Route.Link.Order, 0, new MovementScopeIdentity(MovementScopeKind.Node, context.Source.Node.Id),
                context.LayoutRevision, context.RouteRevision)
        };
        var startX = sourceX;
        for (var index = 0; index < crossedInterLayers.Count; index++)
        {
            var interLayer = crossedInterLayers[index];
            var endX = transitionXs[index];
            demands.Add(new LinkSegmentDemand(
                crossedInterLayers.Count == 1
                    ? $"{context.Route.Link.Id}:segment:through"
                    : $"{context.Route.Link.Id}:segment:through:{interLayer.UpperLayer}:{interLayer.LowerLayer}",
                context.Route.Link.Id, LinkSegmentOrientation.Horizontal, new AxisInterval(startX, endX),
                context.InterLayerAxisRanges[interLayer], null, LinkSegmentRole.Through, context.Route.Link.Order, index + 1,
                new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{interLayer.LowerLayer}"),
                context.LayoutRevision, context.RouteRevision));
            startX = endX;
        }
        demands.Add(new LinkSegmentDemand(
            $"{context.Route.Link.Id}:segment:arrival", context.Route.Link.Id, LinkSegmentOrientation.Vertical,
            new AxisInterval(context.InterLayerAxisRanges[crossedInterLayers[crossedInterLayers.Count - 1]].Minimum,
                context.Route.TargetPoint.Y), new AxisInterval(targetX, targetX), targetX,
            LinkSegmentRole.ConnectionArrival, context.Route.Link.Order, crossedInterLayers.Count + 1,
            new MovementScopeIdentity(MovementScopeKind.Node, context.Target.Node.Id),
            context.LayoutRevision, context.RouteRevision));
        return (demands, transitionXs);
    }
}
