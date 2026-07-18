using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class RouteInvalidationCalculator
{
    public static IReadOnlyList<RouteInvalidation> ForChangedNodes(
        IEnumerable<SemanticRouteReference> routes,
        IEnumerable<string> changedNodeIds,
        RouteInvalidationCause cause,
        LayoutRevision sourcePlacementRevision,
        LayoutRevision targetPlacementRevision)
    {
        if (targetPlacementRevision.Value <= sourcePlacementRevision.Value)
            throw new InvalidOperationException("Route invalidation must target a later placement revision.");
        if (cause != RouteInvalidationCause.EndpointMoved && cause != RouteInvalidationCause.EndpointResized)
            throw new ArgumentException("Direct node changes require EndpointMoved or EndpointResized.", nameof(cause));
        var changed = new HashSet<string>(changedNodeIds, StringComparer.Ordinal);
        return routes.Where(route => changed.Contains(route.SourceNodeId) || changed.Contains(route.TargetNodeId))
            .OrderBy(route => route.LogicalRouteId, StringComparer.Ordinal)
            .Select(route => new RouteInvalidation(
                route.LogicalRouteId,
                cause,
                route.RouteRevision,
                sourcePlacementRevision,
                targetPlacementRevision,
                new MovementScopeIdentity(MovementScopeKind.Node,
                    changed.Contains(route.SourceNodeId) ? route.SourceNodeId : route.TargetNodeId)))
            .ToArray();
    }
}
