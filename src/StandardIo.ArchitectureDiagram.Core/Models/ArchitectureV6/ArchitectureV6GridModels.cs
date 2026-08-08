using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public readonly record struct PlanningGridId
{
    public PlanningGridId(string value)
    {
        Value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Value)) throw new ArgumentException("Grid id is required.", nameof(value));
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct PlanningGridRowId
{
    public PlanningGridRowId(string value)
    {
        Value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Value)) throw new ArgumentException("Row id is required.", nameof(value));
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct PlanningGridColumnId
{
    public PlanningGridColumnId(string value)
    {
        Value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Value)) throw new ArgumentException("Column id is required.", nameof(value));
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct PlanningGridCellId(PlanningGridId GridId, PlanningGridRowId RowId, PlanningGridColumnId ColumnId);

public enum PlanningGridTrackRole
{
    Unknown,
    ProjectHeader,
    NodeBearing,
    InterLayerRouting,
    BaselineNode,
    ExternalRegion,
    StandaloneRegion,
    DiagramProjectPlacement,
    DiagramCrossProjectRouting,
    NodeFootprint,
    SubtreeSiblingGap,
    DestinationApproach,
    OwnershipLocalReturn,
    ProjectBoundaryTransition
}

[Flags]
public enum CellCapability
{
    None = 0,
    RoutingAllowed = 1,
    NodeAllowed = 2,
    ProjectBoundary = 4
}

public enum CellOccupancy
{
    Empty,
    NodeAnchor,
    ProjectLabel,
    ProjectFootprint
}

public sealed record PlanningGridRow(
    PlanningGridRowId Id,
    int LogicalOrder,
    int MinimumExtent,
    int RequiredExtent,
    int FinalExtent,
    int RelativeOffset,
    int AbsoluteOffset,
    PlanningGridTrackRole Role = PlanningGridTrackRole.Unknown,
    string? CreationStage = null,
    string? CreationProvenance = null,
    string? OwnerId = null);

public sealed record PlanningGridColumn(
    PlanningGridColumnId Id,
    int LogicalOrder,
    int MinimumExtent,
    int RequiredExtent,
    int FinalExtent,
    int RelativeOffset,
    int AbsoluteOffset,
    PlanningGridTrackRole Role = PlanningGridTrackRole.Unknown,
    string? CreationStage = null,
    string? CreationProvenance = null,
    string? OwnerId = null);

public sealed record PlanningGridCell(
    PlanningGridCellId Id,
    CellCapability Capabilities,
    CellOccupancy Occupancy,
    IReadOnlyList<string> ReservationIds,
    string? FootprintOwnerId = null);

public sealed record PlanningGrid(
    PlanningGridId Id,
    IReadOnlyList<PlanningGridRow> Rows,
    IReadOnlyList<PlanningGridColumn> Columns,
    IReadOnlyDictionary<PlanningGridCellId, PlanningGridCell> Cells,
    GridTransform Transform);

public sealed record DiagramRoutingGrid(
    PlanningGrid Grid,
    IReadOnlyList<RelativeRectangle> ProjectFootprints,
    IReadOnlyList<GridTransition> Transitions);

public sealed record ProjectRoutingGrid(
    string ProjectId,
    PlanningGrid Grid,
    IReadOnlyList<SubtreeReservation> SubtreeReservations,
    RelativeRectangle? ProjectLabelReservation,
    IReadOnlyList<string>? PhysicalNodeIds = null,
    IReadOnlyList<string>? ExternalNodeIds = null,
    string BoundaryOwnership = "project",
    IReadOnlyList<EndpointEnvelopeReservation>? EndpointReservations = null)
{
    public IReadOnlyList<string> OwnedPhysicalNodeIds => PhysicalNodeIds ?? Array.Empty<string>();
    public IReadOnlyList<string> OwnedExternalNodeIds => ExternalNodeIds ?? Array.Empty<string>();
    public IReadOnlyList<EndpointEnvelopeReservation> OwnedEndpointReservations => EndpointReservations ?? Array.Empty<EndpointEnvelopeReservation>();
}

public sealed record SubtreeReservation(
    string SubtreeId,
    string? PositionalOwnerId,
    PlanningGridId GridId,
    IReadOnlyList<PlanningGridCellId> Cells,
    string? AncestorReservationId,
    IReadOnlyList<SubtreeReservationRowInterval>? RowIntervals = null)
{
    public IReadOnlyList<SubtreeReservationRowInterval> OccupiedRowIntervals { get; } =
        Array.AsReadOnly((RowIntervals ?? Array.Empty<SubtreeReservationRowInterval>()).ToArray());
}

public sealed record SubtreeReservationRowInterval(
    PlanningGridRowId RowId,
    IReadOnlyList<PlanningGridColumnId> Columns);
