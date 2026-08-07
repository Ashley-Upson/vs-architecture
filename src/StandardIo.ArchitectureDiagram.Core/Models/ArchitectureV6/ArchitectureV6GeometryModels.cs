using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public readonly record struct RelativePoint(int X, int Y);
public readonly record struct AbsolutePoint(int X, int Y);
public readonly record struct RelativeRectangle(int X, int Y, int Width, int Height);
public readonly record struct AbsoluteRectangle(int X, int Y, int Width, int Height);

public sealed record GridTransform(PlanningGridId GridId, RelativePoint Origin, int Scale = 1);

public sealed record PlannedPhysicalRouteSegment(
    string PhysicalLinkId,
    RelativePoint? RelativeStart,
    RelativePoint? RelativeEnd,
    AbsolutePoint Start,
    AbsolutePoint End,
    PlanningGridId GridId,
    RouteAxis Axis,
    RouteStepRole Role,
    LaneId Lane,
    RouteTopologyFamily TopologyFamily,
    string ComponentId = "",
    int RouteStepOrder = -1,
    string? StraightRunId = null,
    string? LaneDomainId = null,
    IReadOnlyList<PlanningGridCellId>? AllocatedCells = null,
    PlanningGridRowId? OwningRowId = null,
    PlanningGridColumnId? OwningColumnId = null,
    string StartProvenance = "",
    string EndProvenance = "");

public sealed record PlannedPhysicalRoutePoint(
    string PointId,
    string PhysicalLinkId,
    PlanningGridId GridId,
    AbsolutePoint Point,
    RouteStepRole Role,
    string ComponentId,
    int RouteStepOrder,
    string? StraightRunId,
    string? TurnIdentity,
    string? TransitionIdentity,
    PlanningGridCellId? CellId,
    string Provenance,
    RelativePoint? RelativePoint = null);

public sealed record PlannedPhysicalRouteComponent(
    string ComponentId,
    string PhysicalLinkId,
    RouteStepRole Role,
    int RouteStepOrder,
    string? StraightRunId,
    string? LaneDomainId,
    LaneId? Lane,
    IReadOnlyList<PlanningGridCellId> AllocatedCells,
    IReadOnlyList<PlannedPhysicalRoutePoint> Points,
    string? TurnIdentity,
    string? TransitionIdentity,
    string OwnershipScope,
    string Provenance,
    AbsolutePoint? EntryPoint = null,
    AbsolutePoint? ExitPoint = null,
    GridSide? EntrySide = null,
    GridSide? ExitSide = null,
    string? PrecedingComponentId = null,
    string? FollowingComponentId = null,
    string? ExpectedBoundary = null);

public sealed record PlannedPhysicalMaterialisationAttempt(
    string PhysicalLinkId,
    string ComponentId,
    string? PreviousComponentId,
    string? NextComponentId,
    AbsolutePoint Start,
    AbsolutePoint End,
    string FailureCode,
    string FailureMessage,
    IReadOnlyList<PlanningGridCellId> AllocatedCells,
    string? RouteStepId = null,
    string? StraightRunId = null);

public sealed record PlannedPhysicalRoute(
    string PhysicalLinkId,
    string SemanticLinkId,
    string SourceSemanticNodeId,
    string DestinationSemanticNodeId,
    string SourceProjection,
    string DestinationProjection,
    RouteTopologyFamily TopologyFamily,
    IReadOnlyList<PlannedPhysicalRouteSegment> Segments,
    int BendCount,
    int RouteLength,
    bool HasNodeIntersection,
    bool HasSharedSegment,
    bool HasCrossing,
    IReadOnlyList<PlannedPhysicalRoutePoint>? RawPoints = null,
    IReadOnlyList<PlannedPhysicalRouteComponent>? Components = null,
    int NormalizedPointCount = 0,
    int RemovedDuplicatePointCount = 0,
    int MergedCollinearSegmentCount = 0,
    bool IsInvalid = false,
    IReadOnlyList<PlannedPhysicalRoutePoint>? ReducedPoints = null);

public sealed record PlannedPhysicalTerminal(
    string TerminalId,
    string PhysicalLinkId,
    string PhysicalNodeId,
    GridSide Side,
    AbsolutePoint Point,
    int Ordinal,
    string DomainId,
    string Provenance);

public sealed record PlannedPhysicalTurn(
    string BendIdentity,
    string PhysicalLinkId,
    PlanningGridId GridId,
    PlanningGridCellId CellId,
    AbsolutePoint Point,
    string? HorizontalRunId,
    string? VerticalRunId,
    string Provenance);

public sealed record PlannedPhysicalCrossing(
    string PhysicalLinkId,
    string OtherPhysicalLinkId,
    PlanningGridId GridId,
    PlanningGridCellId CellId,
    AbsolutePoint Point,
    string Provenance);

public sealed record PlannedPhysicalTransition(
    string PhysicalLinkId,
    PlanningGridId SourceGridId,
    PlanningGridId DestinationGridId,
    AbsolutePoint SourcePoint,
    AbsolutePoint DestinationPoint,
    string OwnershipTransition,
    string Provenance);

public sealed record PlannedPhysicalSceneMetrics(
    int AbsoluteNodeCount,
    int TerminalCount,
    int PhysicalRouteCount,
    int SegmentCount,
    int BendCount,
    int CleanCrossingCount,
    int TransitionCount,
    int TotalRouteLength,
    int MaximumRouteLength,
    int NodeOverlapCount,
    int RouteNodeIntersectionCount,
    int SharedCollinearSegmentCount,
    int SharedBendCount,
    int InvalidCrossingCount,
    int TerminalFindingCount,
    int OwnershipFindingCount,
    int LabelGeometryUnavailableCount,
    IReadOnlyDictionary<string, int> TopologyCounts,
    IReadOnlyDictionary<string, long> StageTimingsMilliseconds,
    int InvalidRouteCount = 0,
    int AttemptedSegmentCount = 0,
    int DiagonalSegmentCount = 0,
    int CorridorEscapeCount = 0,
    int ComponentContinuityFailureCount = 0,
    int SourceStubDirectionFailureCount = 0,
    int DestinationStubDirectionFailureCount = 0);

public sealed class PlannedArchitecturePhysicalScene
{
    public PlannedArchitecturePhysicalScene(
        PlannedArchitectureGeometry geometry,
        IReadOnlyList<GridTransform> transforms,
        IReadOnlyList<PlannedPhysicalTerminal> terminals,
        IReadOnlyList<PlannedPhysicalTurn> turns,
        IReadOnlyList<PlannedPhysicalCrossing> crossings,
        IReadOnlyList<PlannedPhysicalTransition> transitions,
        IReadOnlyList<PlannedSubtreeGeometry> reservations,
        IReadOnlyList<ArchitecturePlanningDiagnostic> diagnostics,
        PlannedPhysicalSceneMetrics metrics,
        IReadOnlyList<PlannedPhysicalMaterialisationAttempt>? attemptedSegments = null,
        IReadOnlyList<string>? invalidRouteIds = null)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Transforms = Array.AsReadOnly((transforms ?? throw new ArgumentNullException(nameof(transforms))).ToArray());
        Terminals = Array.AsReadOnly((terminals ?? throw new ArgumentNullException(nameof(terminals))).ToArray());
        Turns = Array.AsReadOnly((turns ?? throw new ArgumentNullException(nameof(turns))).ToArray());
        Crossings = Array.AsReadOnly((crossings ?? throw new ArgumentNullException(nameof(crossings))).ToArray());
        Transitions = Array.AsReadOnly((transitions ?? throw new ArgumentNullException(nameof(transitions))).ToArray());
        Reservations = Array.AsReadOnly((reservations ?? throw new ArgumentNullException(nameof(reservations))).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        AttemptedSegments = Array.AsReadOnly((attemptedSegments ?? Array.Empty<PlannedPhysicalMaterialisationAttempt>()).ToArray());
        InvalidRouteIds = Array.AsReadOnly((invalidRouteIds ?? Array.Empty<string>()).ToArray());
    }

    public PlannedArchitectureGeometry Geometry { get; }
    public IReadOnlyList<GridTransform> Transforms { get; }
    public IReadOnlyList<PlannedPhysicalTerminal> Terminals { get; }
    public IReadOnlyList<PlannedPhysicalTurn> Turns { get; }
    public IReadOnlyList<PlannedPhysicalCrossing> Crossings { get; }
    public IReadOnlyList<PlannedPhysicalTransition> Transitions { get; }
    public IReadOnlyList<PlannedSubtreeGeometry> Reservations { get; }
    public IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics { get; }
    public PlannedPhysicalSceneMetrics Metrics { get; }
    public IReadOnlyList<PlannedPhysicalMaterialisationAttempt> AttemptedSegments { get; }
    public IReadOnlyList<string> InvalidRouteIds { get; }
}

public enum TrackConstraintKind
{
    SingleRowMinimum,
    SingleColumnMinimum,
    MultiColumnSpanMinimum,
    MultiRowSpanMinimum,
    HorizontalLaneEnvelope,
    VerticalLaneEnvelope,
    TurnClearance,
    CrossingClearance,
    NodeFootprint,
    ProjectLabelFootprint,
    ProjectFootprint
}

public sealed record GridTrackConstraint(
    TrackConstraintKind Kind,
    PlanningGridId GridId,
    IReadOnlyList<PlanningGridRowId> Rows,
    IReadOnlyList<PlanningGridColumnId> Columns,
    int MinimumExtent,
    string Reason,
    string? OwnerId);

public sealed record GridTrackSizingPlan(
    IReadOnlyList<PlanningGridRow> Rows,
    IReadOnlyList<PlanningGridColumn> Columns,
    IReadOnlyList<GridTrackConstraint> Constraints,
    RelativeRectangle? DiagramBounds,
    IReadOnlyList<GridTrackProvenance>? Provenance = null);

public sealed record GridTrackContribution(TrackConstraintKind Kind, string? OwnerId, int Extent);

public sealed record GridTrackProvenance(
    PlanningGridId GridId,
    string Axis,
    string TrackId,
    int MinimumExtent,
    int FinalExtent,
    IReadOnlyList<GridTrackContribution> Contributions,
    PlanningGridTrackRole StructuralRole = PlanningGridTrackRole.Unknown,
    string? OwnerId = null,
    int LaneCount = 0,
    int UnusedCapacity = 0,
    TrackConstraintKind? DominantConstraint = null);

public sealed record PlannedPhysicalNodeGeometry(
    string PhysicalNodeId,
    string SemanticNodeId,
    string? ProjectId,
    RelativeRectangle RelativeBounds,
    AbsoluteRectangle AbsoluteBounds,
    PlanningGridId GridId,
    PlanningGridCellId AnchorCellId,
    string? PositionalOwnerId,
    PhysicalNodeProjectionMode ProjectionMode,
    bool IsExternal,
    bool IsStandalone,
    RelativeRectangle? RelativeRoutingBounds = null,
    AbsoluteRectangle? AbsoluteRoutingBounds = null);

public sealed record PlannedProjectGeometry(
    string ProjectId,
    RelativeRectangle RelativeBounds,
    AbsoluteRectangle AbsoluteBounds,
    RelativeRectangle? RelativeLabelBounds,
    AbsoluteRectangle? AbsoluteLabelBounds,
    IReadOnlyList<string> PhysicalNodeIds);

public sealed record PlannedGridGeometry(
    PlanningGridId GridId,
    RelativeRectangle RelativeBounds,
    AbsoluteRectangle AbsoluteBounds,
    GridTransform Transform,
    IReadOnlyList<PlanningGridRow> Rows,
    IReadOnlyList<PlanningGridColumn> Columns);

public sealed record PlannedRelativeNodeGeometry(
    string PhysicalNodeId,
    string SemanticNodeId,
    string? ProjectId,
    RelativeRectangle Bounds,
    PlanningGridId GridId,
    PlanningGridCellId AnchorCellId,
    string? PositionalOwnerId,
    PhysicalNodeProjectionMode ProjectionMode,
    bool IsExternal,
    bool IsStandalone,
    RelativeRectangle? VisibleBounds = null);

public sealed record PlannedRelativeProjectGeometry(
    string ProjectId,
    RelativeRectangle Bounds,
    RelativeRectangle LabelBounds,
    IReadOnlyList<string> PhysicalNodeIds);

public sealed record PlannedRelativeSubtreeGeometry(
    string SubtreeId,
    string? PositionalOwnerId,
    PlanningGridId GridId,
    RelativeRectangle Bounds,
    string? AncestorSubtreeId,
    IReadOnlyList<PlannedRelativeSubtreeInterval>? Intervals = null);

public sealed record PlannedRelativeSubtreeInterval(
    PlanningGridRowId RowId,
    RelativeRectangle Bounds);

public sealed class PlannedArchitectureRelativeGeometry
{
    public PlannedArchitectureRelativeGeometry(
        IReadOnlyList<PlannedRelativeNodeGeometry> nodes,
        IReadOnlyList<PlannedRelativeProjectGeometry> projects,
        IReadOnlyList<PlannedGridGeometry> grids,
        IReadOnlyList<PlannedRelativeSubtreeGeometry> subtrees,
        RelativeRectangle diagramBounds)
    {
        Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
        Projects = Array.AsReadOnly((projects ?? throw new ArgumentNullException(nameof(projects))).ToArray());
        Grids = Array.AsReadOnly((grids ?? throw new ArgumentNullException(nameof(grids))).ToArray());
        Subtrees = Array.AsReadOnly((subtrees ?? throw new ArgumentNullException(nameof(subtrees))).ToArray());
        if (diagramBounds.Width <= 0 || diagramBounds.Height <= 0) throw new ArgumentException("Diagram bounds must be positive.", nameof(diagramBounds));
        DiagramBounds = diagramBounds;
    }

    public IReadOnlyList<PlannedRelativeNodeGeometry> Nodes { get; }
    public IReadOnlyList<PlannedRelativeProjectGeometry> Projects { get; }
    public IReadOnlyList<PlannedGridGeometry> Grids { get; }
    public IReadOnlyList<PlannedRelativeSubtreeGeometry> Subtrees { get; }
    public RelativeRectangle DiagramBounds { get; }
}

public sealed record PlannedSubtreeGeometry(
    string SubtreeId,
    string? PositionalOwnerId,
    PlanningGridId GridId,
    RelativeRectangle RelativeBounds,
    AbsoluteRectangle AbsoluteBounds,
    string? AncestorSubtreeId);

public sealed class PlannedArchitectureGeometry
{
    public PlannedArchitectureGeometry(
        IReadOnlyList<PlannedPhysicalNodeGeometry> nodes,
        IReadOnlyList<PlannedProjectGeometry> projects,
        IReadOnlyList<PlannedGridGeometry> grids,
        IReadOnlyList<PlannedSubtreeGeometry> subtrees,
        RelativeRectangle diagramBounds,
        AbsoluteRectangle absoluteDiagramBounds,
        IReadOnlyList<PlannedPhysicalRoute>? routes = null)
    {
        Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
        Projects = Array.AsReadOnly((projects ?? throw new ArgumentNullException(nameof(projects))).ToArray());
        Grids = Array.AsReadOnly((grids ?? throw new ArgumentNullException(nameof(grids))).ToArray());
        Subtrees = Array.AsReadOnly((subtrees ?? throw new ArgumentNullException(nameof(subtrees))).ToArray());
        if (diagramBounds.Width <= 0 || diagramBounds.Height <= 0) throw new ArgumentException("Diagram bounds must be positive.", nameof(diagramBounds));
        if (absoluteDiagramBounds.Width <= 0 || absoluteDiagramBounds.Height <= 0) throw new ArgumentException("Diagram bounds must be positive.", nameof(absoluteDiagramBounds));
        DiagramBounds = diagramBounds;
        AbsoluteDiagramBounds = absoluteDiagramBounds;
        Routes = Array.AsReadOnly((routes ?? Array.Empty<PlannedPhysicalRoute>()).ToArray());
    }

    public IReadOnlyList<PlannedPhysicalNodeGeometry> Nodes { get; }
    public IReadOnlyList<PlannedProjectGeometry> Projects { get; }
    public IReadOnlyList<PlannedGridGeometry> Grids { get; }
    public IReadOnlyList<PlannedSubtreeGeometry> Subtrees { get; }
    public RelativeRectangle DiagramBounds { get; }
    public AbsoluteRectangle AbsoluteDiagramBounds { get; }
    public IReadOnlyList<PlannedPhysicalRoute> Routes { get; }

    public PlannedArchitectureGeometry WithRoutes(IReadOnlyList<PlannedPhysicalRoute> routes) =>
        new(Nodes, Projects, Grids, Subtrees, DiagramBounds, AbsoluteDiagramBounds, routes);
}
