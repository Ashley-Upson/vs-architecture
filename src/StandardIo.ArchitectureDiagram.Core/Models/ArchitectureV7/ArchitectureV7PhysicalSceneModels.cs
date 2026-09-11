using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7PhysicalSceneConfiguration(
    double BaseCellWidth,
    double RoutingRowMinimum,
    double BoundaryRowMinimum,
    double NodeMinimumWidth,
    double NodeMinimumHeight,
    double ProjectHeaderHeight,
    double LabelCharacterWidth,
    double LabelHorizontalMargin,
    double RouteClearance,
    double NodeClearance,
    double ParallelLaneSpacing,
    double TerminalPortSpacing,
    double TerminalInset);

public sealed record ArchitectureV7PhysicalTrackDimension(
    int LogicalIndex,
    double Start,
    double End,
    double RequiredExtent,
    IReadOnlyList<double> LaneCoordinates);

public sealed record ArchitectureV7PhysicalBounds(double Left, double Top, double Right, double Bottom);
public sealed record ArchitectureV7PhysicalProjectBounds(string ProjectId, ArchitectureV7PhysicalBounds Bounds, string Provenance);
public sealed record ArchitectureV7PhysicalPoint(double X, double Y, string Provenance);

public sealed record ArchitectureV7PhysicalTerminal(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    int SlotOrdinal,
    ArchitectureV7PhysicalPoint Position,
    string Provenance);

public sealed record ArchitectureV7PhysicalSceneNode(
    string PhysicalNodeId,
    ArchitectureV7PhysicalBounds Bounds,
    string Provenance);

public sealed record ArchitectureV7PhysicalSegment(
    string PhysicalLinkId,
    ArchitectureV7PhysicalPoint Start,
    ArchitectureV7PhysicalPoint End,
    IReadOnlyList<int> RouteCellIndices,
    IReadOnlyList<ArchitectureV7RouteCell> LogicalCells,
    string RunId,
    string LaneId,
    string AllocationProvenance);

public sealed class ArchitectureV7PhysicalRoute
{
    public ArchitectureV7PhysicalRoute(string physicalLinkId, IReadOnlyList<ArchitectureV7PhysicalPoint> points,
        IReadOnlyList<ArchitectureV7PhysicalSegment> segments, string provenance)
    {
        PhysicalLinkId = physicalLinkId;
        Points = Array.AsReadOnly((points ?? Array.Empty<ArchitectureV7PhysicalPoint>()).ToArray());
        Segments = Array.AsReadOnly((segments ?? Array.Empty<ArchitectureV7PhysicalSegment>()).ToArray());
        Provenance = provenance;
    }
    public string PhysicalLinkId { get; }
    public IReadOnlyList<ArchitectureV7PhysicalPoint> Points { get; }
    public IReadOnlyList<ArchitectureV7PhysicalSegment> Segments { get; }
    public string Provenance { get; }
}

public sealed record ArchitectureV7PhysicalSceneDiagnostic(string Code, string Message, bool IsHardFailure, string? PhysicalLinkId = null);

public sealed class ArchitectureV7PhysicalSceneFreeze
{
    public ArchitectureV7PhysicalSceneFreeze(
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalTerminal> terminals,
        IReadOnlyList<ArchitectureV7PhysicalRoute> routes,
        IReadOnlyList<ArchitectureV7PhysicalSceneDiagnostic> diagnostics,
        string placementFingerprint, string routeFingerprint, string allocationFingerprint, string physicalSceneFingerprint,
        IReadOnlyList<string>? accountedPhysicalLinkIds = null,
        IReadOnlyList<ArchitectureV7PhysicalProjectBounds>? projectBounds = null)
    {
        Rows = Array.AsReadOnly((rows ?? Array.Empty<ArchitectureV7PhysicalTrackDimension>()).OrderBy(x => x.LogicalIndex).ToArray());
        Columns = Array.AsReadOnly((columns ?? Array.Empty<ArchitectureV7PhysicalTrackDimension>()).OrderBy(x => x.LogicalIndex).ToArray());
        Nodes = Array.AsReadOnly((nodes ?? Array.Empty<ArchitectureV7PhysicalSceneNode>()).OrderBy(x => x.PhysicalNodeId, StringComparer.Ordinal).ToArray());
        Terminals = Array.AsReadOnly((terminals ?? Array.Empty<ArchitectureV7PhysicalTerminal>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ThenBy(x => x.SlotOrdinal).ToArray());
        Routes = Array.AsReadOnly((routes ?? Array.Empty<ArchitectureV7PhysicalRoute>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ArchitectureV7PhysicalSceneDiagnostic>()).ToArray());
        AccountedPhysicalLinkIds = Array.AsReadOnly((accountedPhysicalLinkIds ?? Routes.Select(x => x.PhysicalLinkId))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        ProjectBounds = Array.AsReadOnly((projectBounds ?? Array.Empty<ArchitectureV7PhysicalProjectBounds>())
            .OrderBy(x => x.ProjectId, StringComparer.Ordinal).ToArray());
        PlacementFingerprint = placementFingerprint; RouteFingerprint = routeFingerprint; AllocationFingerprint = allocationFingerprint; PhysicalSceneFingerprint = physicalSceneFingerprint;
    }
    public IReadOnlyList<ArchitectureV7PhysicalTrackDimension> Rows { get; }
    public IReadOnlyList<ArchitectureV7PhysicalTrackDimension> Columns { get; }
    public IReadOnlyList<ArchitectureV7PhysicalSceneNode> Nodes { get; }
    public IReadOnlyList<ArchitectureV7PhysicalTerminal> Terminals { get; }
    public IReadOnlyList<ArchitectureV7PhysicalRoute> Routes { get; }
    public IReadOnlyList<ArchitectureV7PhysicalSceneDiagnostic> Diagnostics { get; }
    public IReadOnlyList<string> AccountedPhysicalLinkIds { get; }
    public IReadOnlyList<ArchitectureV7PhysicalProjectBounds> ProjectBounds { get; }
    public IReadOnlyList<string> CompiledPhysicalLinkIds => Routes.Select(route => route.PhysicalLinkId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> FailedPhysicalLinkIds => AccountedPhysicalLinkIds.Except(CompiledPhysicalLinkIds, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public bool RelationshipAccountingInvariant => CompiledPhysicalLinkIds.Count + FailedPhysicalLinkIds.Count == AccountedPhysicalLinkIds.Count;
    public string PlacementFingerprint { get; }
    public string RouteFingerprint { get; }
    public string AllocationFingerprint { get; }
    public string PhysicalSceneFingerprint { get; }
    public bool IsComplete => !Diagnostics.Any(x => x.IsHardFailure) &&
        RelationshipAccountingInvariant && FailedPhysicalLinkIds.Count == 0;
}
