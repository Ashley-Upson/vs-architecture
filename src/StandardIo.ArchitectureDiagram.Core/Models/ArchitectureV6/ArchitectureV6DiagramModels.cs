using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public enum PhysicalNodeProjectionMode
{
    Canonical,
    DuplicateBranch
}

public sealed record DuplicationProvenance(string SemanticNodeId, string Reason, string? ParentPhysicalNodeId);

public sealed record PhysicalNodePlacementMetadata(
    string PhysicalNodeId,
    string SemanticNodeId,
    string? PositionalOwnerId,
    IReadOnlyList<string> PositionalChildIds,
    IReadOnlyList<string> SemanticParentIds,
    IReadOnlyList<string> SemanticChildIds,
    string SubtreeId,
    IReadOnlyList<string> SubtreeAncestorIds,
    string? ProjectId,
    int LogicalLayer,
    bool IsBaseline,
    bool IsExternal,
    bool IsStandalone,
    string PlacementReason,
    int SemanticDepth = 0,
    string RoleSelector = "Unmatched",
    int RoleBand = 0,
    string OwnershipGroup = "",
    string SiblingGroup = "",
    string HorizontalSpacingPolicy = "sibling",
    string VerticalSpacingPolicy = "logical-layer",
    int PhysicalRow = 0,
    int PhysicalColumn = 0,
    string TreeRootId = "",
    string? ParentSemanticId = null,
    int Subdepth = 0,
    int ConfiguredRoleOrder = 0,
    int SiblingOrder = 0,
    int BranchOrder = 0,
    string PlacementGroup = "");

public sealed record PlannedPhysicalNode(
    string PhysicalNodeId,
    string SemanticNodeId,
    PhysicalNodeProjectionMode ProjectionMode,
    string? PositionalOwnerId,
    string? ProjectId,
    DuplicationProvenance? DuplicationProvenance,
    bool IsExternal,
    bool IsStandalone)
{
    public string SemanticName { get; init; } = string.Empty;
    public string SemanticFullName { get; init; } = string.Empty;
}

public sealed record PlannedPhysicalLink(
    string PhysicalLinkId,
    string SemanticLinkId,
    string SourcePhysicalNodeId,
    string DestinationPhysicalNodeId,
    string? SourceProjectId,
    string? DestinationProjectId)
{
    public string Kind { get; init; } = string.Empty;
    public string? DisplayLabel { get; init; }
}

public sealed record PlannedPhysicalLinkMetadata(
    string PhysicalLinkId,
    string SemanticLinkId,
    string SourcePhysicalNodeId,
    string DestinationPhysicalNodeId,
    string? SourceProjectId,
    string? DestinationProjectId,
    int SourceLayer,
    int DestinationLayer,
    string RelativeDirection,
    bool IsCrossProject,
    bool IsExternal);

public sealed record ArchitectureProjectionResult(
    IReadOnlyList<PlannedPhysicalNode> PhysicalNodes,
    IReadOnlyList<PlannedPhysicalLink> PhysicalLinks,
    IReadOnlyList<PhysicalNodePlacementMetadata> NodeMetadata,
    IReadOnlyList<PlannedPhysicalLinkMetadata> LinkMetadata,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SemanticNodeToPhysicalNodeIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SemanticLinkToPhysicalLinkIds,
    IReadOnlyList<string> RootPhysicalNodeIds,
    IReadOnlyList<string> ExternalPhysicalNodeIds,
    IReadOnlyList<string> StandalonePhysicalNodeIds,
    IReadOnlyList<string> CycleSemanticNodeIds,
    IReadOnlyList<string> UnaccountedSemanticNodeIds,
    IReadOnlyList<string> UnaccountedSemanticLinkIds,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics);

public sealed record ArchitecturePlanningStageStatus(
    bool ProjectionCompleted,
    bool LogicalPlacementCompleted,
    bool RoutingDeferred,
    bool SizingDeferred,
    bool AbsoluteGeometryDeferred,
    bool SizingCompleted = false,
    bool AbsoluteGeometryCompleted = false);

public sealed class PlannedNodePlacement
{
    public PlannedNodePlacement(
        string physicalNodeId,
        PlanningGridId gridId,
        PlanningGridCellId anchorCellId,
        int columnSpan,
        int rowSpan,
        IReadOnlyList<PlanningGridCellId> footprint,
        PlanningGridColumnId centreColumnId)
    {
        if (string.IsNullOrWhiteSpace(physicalNodeId)) throw new ArgumentException("Physical node id is required.", nameof(physicalNodeId));
        if (columnSpan <= 0 || columnSpan % 2 == 0) throw new ArgumentException("Node column spans must be positive and odd.", nameof(columnSpan));
        if (rowSpan <= 0) throw new ArgumentException("Node row spans must be positive.", nameof(rowSpan));
        Footprint = Array.AsReadOnly((footprint ?? throw new ArgumentNullException(nameof(footprint))).ToArray());
        PhysicalNodeId = physicalNodeId;
        GridId = gridId;
        AnchorCellId = anchorCellId;
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
        CentreColumnId = centreColumnId;
    }

    public string PhysicalNodeId { get; }
    public PlanningGridId GridId { get; }
    public PlanningGridCellId AnchorCellId { get; }
    public int ColumnSpan { get; }
    public int RowSpan { get; }
    public IReadOnlyList<PlanningGridCellId> Footprint { get; }
    public PlanningGridColumnId CentreColumnId { get; }
}

public sealed class PlannedArchitectureDiagram
{
    public PlannedArchitectureDiagram(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> physicalNodes,
        IReadOnlyList<PlannedPhysicalLink> physicalLinks,
        DiagramRoutingGrid diagramGrid,
        IReadOnlyList<ProjectRoutingGrid> projectGrids,
        IReadOnlyList<PlannedNodePlacement> nodePlacements,
        IReadOnlyList<PlannedGridRoute> routes,
        GridTrackSizingPlan sizing,
        ArchitecturePlanningDiagnostics diagnostics,
        ArchitectureProjectionResult? projection = null,
        IReadOnlyList<PhysicalNodePlacementMetadata>? nodeMetadata = null,
        IReadOnlyList<PlannedPhysicalLinkMetadata>? linkMetadata = null,
        IReadOnlyList<SubtreeReservation>? subtreeReservations = null,
    ArchitecturePlanningStageStatus? stageStatus = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        PhysicalNodes = Array.AsReadOnly((physicalNodes ?? throw new ArgumentNullException(nameof(physicalNodes))).ToArray());
        PhysicalLinks = Array.AsReadOnly((physicalLinks ?? throw new ArgumentNullException(nameof(physicalLinks))).ToArray());
        DiagramGrid = diagramGrid ?? throw new ArgumentNullException(nameof(diagramGrid));
        ProjectGrids = Array.AsReadOnly((projectGrids ?? throw new ArgumentNullException(nameof(projectGrids))).ToArray());
        NodePlacements = Array.AsReadOnly((nodePlacements ?? throw new ArgumentNullException(nameof(nodePlacements))).ToArray());
        Routes = Array.AsReadOnly((routes ?? throw new ArgumentNullException(nameof(routes))).ToArray());
        Sizing = sizing ?? throw new ArgumentNullException(nameof(sizing));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        Projection = projection;
        NodeMetadata = Array.AsReadOnly((nodeMetadata ?? Array.Empty<PhysicalNodePlacementMetadata>()).ToArray());
        LinkMetadata = Array.AsReadOnly((linkMetadata ?? Array.Empty<PlannedPhysicalLinkMetadata>()).ToArray());
        SubtreeReservations = Array.AsReadOnly((subtreeReservations ?? Array.Empty<SubtreeReservation>()).ToArray());
        StageStatus = stageStatus ?? new ArchitecturePlanningStageStatus(false, false, true, true, true);
    }

    public ArchitecturePlanningRequest Request { get; }
    public IReadOnlyList<PlannedPhysicalNode> PhysicalNodes { get; }
    public IReadOnlyList<PlannedPhysicalLink> PhysicalLinks { get; }
    public DiagramRoutingGrid DiagramGrid { get; }
    public IReadOnlyList<ProjectRoutingGrid> ProjectGrids { get; }
    public IReadOnlyList<PlannedNodePlacement> NodePlacements { get; }
    public IReadOnlyList<PlannedGridRoute> Routes { get; }
    public GridTrackSizingPlan Sizing { get; }
    public ArchitecturePlanningDiagnostics Diagnostics { get; }
    public ArchitectureProjectionResult? Projection { get; }
    public IReadOnlyList<PhysicalNodePlacementMetadata> NodeMetadata { get; }
    public IReadOnlyList<PlannedPhysicalLinkMetadata> LinkMetadata { get; }
    public IReadOnlyList<SubtreeReservation> SubtreeReservations { get; }
    public ArchitecturePlanningStageStatus StageStatus { get; }
    public PlannedArchitectureGeometry? Geometry { get; init; }
}

public sealed class ArchitectureDiagramPlanningState
{
    public ArchitectureDiagramPlanningState(ArchitecturePlanningRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public ArchitecturePlanningRequest Request { get; }
    public List<PlannedPhysicalNode> PhysicalNodes { get; } = new();
    public List<PlannedPhysicalLink> PhysicalLinks { get; } = new();
    public List<PlannedGridRoute> Routes { get; } = new();
    public Dictionary<PlanningGridCellId, PlanningGridCell> Cells { get; } = new();
}
