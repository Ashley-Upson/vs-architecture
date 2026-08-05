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
    RouteTopologyFamily TopologyFamily);

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
    bool HasCrossing);

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
    RelativeRectangle? DiagramBounds);

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
    bool IsStandalone);

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
