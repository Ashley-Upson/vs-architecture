using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public enum PlanningDiagnosticSubject
{
    SemanticNode,
    SemanticLink,
    PhysicalNode,
    PhysicalLink,
    Grid,
    Cell,
    Row,
    Column,
    RouteStep,
    StraightRun,
    Lane,
    TrackConstraint,
    PhysicalSegment
}

public sealed record ArchitecturePlanningDiagnostic(
    string Code,
    string Message,
    PlanningDiagnosticSubject Subject,
    string? SubjectId);

public sealed record ArchitecturePlanningMetrics(
    int SemanticNodeCount,
    int SemanticLinkCount,
    int PhysicalNodeCount,
    int PhysicalLinkCount,
    int ProjectGridCount,
    int GridRowCount,
    int GridColumnCount,
    int OccupiedCellCount,
    int RouteUsedCellCount,
    IReadOnlyDictionary<string, int> TopologyFamilyCounts,
    int RouteStepCount,
    int StraightRunCount,
    int LaneCount,
    RelativeRectangle? DiagramBounds,
    int ValidationFindingCount,
    IReadOnlyDictionary<string, int>? DuplicateCountsBySemanticNode = null,
    int RootCount = 0,
    int ExternalNodeCount = 0,
    int StandaloneNodeCount = 0,
    int CycleCount = 0,
    int LogicalLayerCount = 0,
    int AnchorCellCount = 0,
    int FootprintCellCount = 0,
    int SubtreeReservationCount = 0,
    int PositionalOwnerCount = 0,
    int UnplacedNodeCount = 0,
    int OverlappingFootprintCount = 0,
    int IncompatibleReservationCount = 0,
    int SizedNodeCount = 0,
    int GeometryProjectCount = 0,
    int GeometryGridCount = 0,
    int GeometryWidth = 0,
    int GeometryHeight = 0,
    int GeometryCollisionCount = 0,
    int GeometryContainmentViolationCount = 0,
    int InvalidDimensionCount = 0,
    int FootprintExpansionRequirementCount = 0,
    int PlacementRebuildCount = 0,
    int ExpandedNodeCount = 0,
    int UnsupportedRouteCount = 0,
    IReadOnlyDictionary<string, int>? LaneAllocationConflictCounts = null,
    IReadOnlyDictionary<string, string>? ExpandedNodeSpans = null,
    int InvalidatedRouteCount = 0,
    int RebuiltReservationCount = 0,
    int ShiftedRegionCount = 0);

public sealed record ArchitecturePlanningDiagnostics(
    IReadOnlyList<ArchitecturePlanningDiagnostic> Findings,
    ArchitecturePlanningMetrics Metrics);

public sealed record PlannedArchitectureValidationResult(
    bool IsValid,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Findings);
