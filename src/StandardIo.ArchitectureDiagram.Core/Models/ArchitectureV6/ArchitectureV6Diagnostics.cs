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
    int IncompatibleReservationCount = 0);

public sealed record ArchitecturePlanningDiagnostics(
    IReadOnlyList<ArchitecturePlanningDiagnostic> Findings,
    ArchitecturePlanningMetrics Metrics);

public sealed record PlannedArchitectureValidationResult(
    bool IsValid,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Findings);
