using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class AdjacentDownwardContextFactory
{
    public static IReadOnlyList<AdjacentDownwardRouteContext> Create(
        RenderLayout layout,
        InterLayerBandReport bandReport)
    {
        var memberships = bandReport.Bands.SelectMany(item => item.Memberships)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<BandRouteMembership>)group.ToArray(), StringComparer.Ordinal);
        var demands = bandReport.Bands.SelectMany(item => item.Demands)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<BandRouteDemand>)group.ToArray(), StringComparer.Ordinal);
        var ranges = bandReport.Bands.ToDictionary(
            item => item.Id, item => new AxisInterval(item.UpperBoundary, item.LowerBoundary));
        var duplicatedExposureTree = layout.Graph.PlacementParentByNode.Count == 0 &&
            layout.Graph.Nodes.Any(item => item.Id.StartsWith("tree_", StringComparison.Ordinal));
        return layout.Links.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal).Select(route =>
            new AdjacentDownwardRouteContext(
                route,
                layout.Nodes[route.Link.SourceId],
                layout.Nodes[route.Link.TargetId],
                layout.LayoutRevision,
                bandReport.Telemetry.RouteRevision,
                memberships.TryGetValue(route.Link.Id, out var routeMemberships)
                    ? routeMemberships : Array.Empty<BandRouteMembership>(),
                demands.TryGetValue(route.Link.Id, out var routeDemands)
                    ? routeDemands : Array.Empty<BandRouteDemand>(),
                ranges,
                layout.Corridors,
                layout.Lanes,
                layout.GroupedSpacingPlan,
                duplicatedExposureTree)).ToArray();
    }
}
