using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class AdjacentDownwardContextFactory
{
    public static IReadOnlyList<AdjacentDownwardLinkContext> Create(
        RenderLayout layout,
        InterLayerReport bandReport)
    {
        var memberships = bandReport.InterLayers.SelectMany(item => item.Memberships)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InterLayerLinkMembership>)group.ToArray(), StringComparer.Ordinal);
        var demands = bandReport.InterLayers.SelectMany(item => item.Demands)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InterLayerLinkDemand>)group.ToArray(), StringComparer.Ordinal);
        var ranges = bandReport.InterLayers.ToDictionary(
            item => item.Id, item => new AxisInterval(item.UpperBoundary, item.LowerBoundary));
        var maximumDepth = layout.Nodes.Values.Where(item => !item.IsStandalone).Select(item => item.Depth).DefaultIfEmpty(0).Max();
        for (var depth = 0; depth < maximumDepth; depth++)
        {
            var id = new InterLayerId(depth, depth + 1, layout.LayoutRevision);
            if (ranges.ContainsKey(id)) continue;
            var upper = layout.Nodes.Values.Where(item => !item.IsStandalone && item.Depth == depth)
                .Select(item => item.Rect.Bottom).DefaultIfEmpty(0).Max();
            var lower = layout.Nodes.Values.Where(item => !item.IsStandalone && item.Depth == depth + 1)
                .Select(item => item.Rect.Y).DefaultIfEmpty(upper).Min();
            ranges[id] = new AxisInterval(upper, lower);
        }
        var duplicatedExposureTree = layout.Graph.PlacementParentByNode.Count == 0 &&
            layout.Graph.Nodes.Any(item => item.Id.StartsWith("tree_", StringComparison.Ordinal));
        return layout.Links.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal).Select(route =>
            new AdjacentDownwardLinkContext(
                route,
                layout.Nodes[route.Link.SourceId],
                layout.Nodes[route.Link.TargetId],
                layout.LayoutRevision,
                bandReport.Telemetry.RouteRevision,
                memberships.TryGetValue(route.Link.Id, out var routeMemberships)
                    ? routeMemberships : Array.Empty<InterLayerLinkMembership>(),
                demands.TryGetValue(route.Link.Id, out var routeDemands)
                    ? routeDemands : Array.Empty<InterLayerLinkDemand>(),
                ranges,
                layout.Corridors,
                layout.Lanes,
                layout.GroupedSpacingPlan,
                duplicatedExposureTree)).ToArray();
    }
}
