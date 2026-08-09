using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7RouteCell(int Row, int Column);

public sealed record ArchitectureV7RouteDiagnostic(
    string Code,
    string Message,
    bool IsHardFailure,
    IReadOnlyList<ArchitectureV7RouteCell> AttemptedCells);

public sealed class ArchitectureV7LogicalRoute
{
    public ArchitectureV7LogicalRoute(
        string physicalLinkId,
        string semanticLinkId,
        string sourcePhysicalNodeId,
        string destinationPhysicalNodeId,
        IReadOnlyList<ArchitectureV7RouteCell> cells,
        bool isComplete,
        IReadOnlyList<ArchitectureV7RouteDiagnostic> diagnostics,
        string provenance)
    {
        PhysicalLinkId = physicalLinkId ?? throw new ArgumentNullException(nameof(physicalLinkId));
        SemanticLinkId = semanticLinkId ?? throw new ArgumentNullException(nameof(semanticLinkId));
        SourcePhysicalNodeId = sourcePhysicalNodeId ?? throw new ArgumentNullException(nameof(sourcePhysicalNodeId));
        DestinationPhysicalNodeId = destinationPhysicalNodeId ?? throw new ArgumentNullException(nameof(destinationPhysicalNodeId));
        Cells = Array.AsReadOnly((cells ?? Array.Empty<ArchitectureV7RouteCell>()).ToArray());
        IsComplete = isComplete;
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ArchitectureV7RouteDiagnostic>()).ToArray());
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public string PhysicalLinkId { get; }
    public string SemanticLinkId { get; }
    public string SourcePhysicalNodeId { get; }
    public string DestinationPhysicalNodeId { get; }
    public IReadOnlyList<ArchitectureV7RouteCell> Cells { get; }
    public bool IsComplete { get; }
    public IReadOnlyList<ArchitectureV7RouteDiagnostic> Diagnostics { get; }
    public string Provenance { get; }
}

public sealed class ArchitectureV7LogicalRouteFreeze
{
    public ArchitectureV7LogicalRouteFreeze(
        IReadOnlyList<ArchitectureV7LogicalRoute> routes,
        IReadOnlyList<ArchitectureV7RouteDiagnostic> diagnostics,
        string placementFingerprint,
        string projectionFingerprint,
        string routeFingerprint)
    {
        Routes = Array.AsReadOnly((routes ?? Array.Empty<ArchitectureV7LogicalRoute>()).OrderBy(route => route.PhysicalLinkId, StringComparer.Ordinal).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ArchitectureV7RouteDiagnostic>()).ToArray());
        PlacementFingerprint = placementFingerprint ?? throw new ArgumentNullException(nameof(placementFingerprint));
        ProjectionFingerprint = projectionFingerprint ?? throw new ArgumentNullException(nameof(projectionFingerprint));
        RouteFingerprint = routeFingerprint ?? throw new ArgumentNullException(nameof(routeFingerprint));
    }

    public IReadOnlyList<ArchitectureV7LogicalRoute> Routes { get; }
    public IReadOnlyList<ArchitectureV7RouteDiagnostic> Diagnostics { get; }
    public string PlacementFingerprint { get; }
    public string ProjectionFingerprint { get; }
    public string RouteFingerprint { get; }
}
