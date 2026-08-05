using System;
using System.Collections.Generic;

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
