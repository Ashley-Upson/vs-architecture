using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DownwardLinkSegmentDemandFactory
{
    public static (IReadOnlyList<LinkSegmentDemand> Demands, IReadOnlyList<int> TransitionXs) Create(
        AdjacentDownwardLinkContext context,
        IReadOnlyList<InterLayerId> crossedBands)
    {
        if (crossedBands.Count == 0) return (Array.Empty<LinkSegmentDemand>(), Array.Empty<int>());
        var sourceX = context.Route.SourcePoint.X;
        var targetX = context.Route.TargetPoint.X;
        var transitionXs = Enumerable.Range(1, crossedBands.Count)
            .Select(index => sourceX + (int)Math.Round((targetX - sourceX) * index / (double)crossedBands.Count))
            .ToArray();
        var demands = new List<LinkSegmentDemand>
        {
            new($"{context.Route.Link.Id}:rail:departure", context.Route.Link.Id, LinkSegmentOrientation.Vertical,
                new AxisInterval(context.Route.SourcePoint.Y, context.BandAxisRanges[crossedBands[0]].Maximum),
                new AxisInterval(sourceX, sourceX), sourceX, LinkSegmentRole.ConnectionDeparture,
                context.Route.Link.Order, 0, new MovementScopeIdentity(MovementScopeKind.Node, context.Source.Node.Id),
                context.LayoutRevision, context.RouteRevision)
        };
        var startX = sourceX;
        for (var index = 0; index < crossedBands.Count; index++)
        {
            var band = crossedBands[index];
            var endX = transitionXs[index];
            demands.Add(new LinkSegmentDemand(
                crossedBands.Count == 1
                    ? $"{context.Route.Link.Id}:rail:through"
                    : $"{context.Route.Link.Id}:rail:through:{band.UpperLayer}:{band.LowerLayer}",
                context.Route.Link.Id, LinkSegmentOrientation.Horizontal, new AxisInterval(startX, endX),
                context.BandAxisRanges[band], null, LinkSegmentRole.Through, context.Route.Link.Order, index + 1,
                new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{band.LowerLayer}"),
                context.LayoutRevision, context.RouteRevision));
            startX = endX;
        }
        demands.Add(new LinkSegmentDemand(
            $"{context.Route.Link.Id}:rail:arrival", context.Route.Link.Id, LinkSegmentOrientation.Vertical,
            new AxisInterval(context.BandAxisRanges[crossedBands[crossedBands.Count - 1]].Minimum,
                context.Route.TargetPoint.Y), new AxisInterval(targetX, targetX), targetX,
            LinkSegmentRole.ConnectionArrival, context.Route.Link.Order, crossedBands.Count + 1,
            new MovementScopeIdentity(MovementScopeKind.Node, context.Target.Node.Id),
            context.LayoutRevision, context.RouteRevision));
        return (demands, transitionXs);
    }
}
