using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public sealed class GridBoundaryIdentity : IEquatable<GridBoundaryIdentity>
{
    public GridBoundaryIdentity(
        PlanningGridId gridId,
        PlanningGridCellId cellId,
        GridSide side,
        LaneId? lane,
        string ownershipScope,
        string authorityId,
        PlanningGridCellId? adjacentCellId = null)
    {
        GridId = gridId;
        CellId = cellId;
        Side = side;
        Lane = lane;
        OwnershipScope = ownershipScope ?? string.Empty;
        AuthorityId = authorityId ?? string.Empty;
        AdjacentCellId = adjacentCellId;
        IsShared = adjacentCellId is not null;
        Orientation = side is GridSide.Left or GridSide.Right
            ? GridBoundaryOrientation.Vertical
            : GridBoundaryOrientation.Horizontal;
    }

    public PlanningGridId GridId { get; }
    public PlanningGridCellId CellId { get; }
    public PlanningGridCellId? AdjacentCellId { get; }
    public GridSide Side { get; }
    public GridBoundaryOrientation Orientation { get; }
    public LaneId? Lane { get; }
    public string OwnershipScope { get; }
    public string AuthorityId { get; }
    public bool IsShared { get; }
    public bool IsExterior => !IsShared;

    public static bool TryCreateShared(
        GridBoundaryIdentity first,
        GridBoundaryIdentity second,
        out GridBoundaryIdentity? shared)
    {
        shared = null;
        if (first.GridId != second.GridId || first.OwnershipScope != second.OwnershipScope || first.Lane != second.Lane)
            return false;
        if (!AreComplementary(first.Side, second.Side) || !AreAdjacent(first.CellId, first.Side, second.CellId, second.Side))
            return false;

        var ordered = CompareCells(first.CellId, second.CellId) <= 0
            ? (first, second)
            : (second, first);
        shared = new GridBoundaryIdentity(first.GridId, ordered.Item1.CellId, ordered.Item1.Side, first.Lane,
            first.OwnershipScope, "shared-boundary", ordered.Item2.CellId);
        return true;
    }

    public bool Equals(GridBoundaryIdentity? other)
    {
        if (other is null) return false;
        if (GridId != other.GridId || OwnershipScope != other.OwnershipScope || Lane != other.Lane || IsShared != other.IsShared)
            return false;
        if (!IsShared)
            return CellId == other.CellId && Side == other.Side;
        return SameUnorderedCells(CellId, AdjacentCellId!.Value, other.CellId, other.AdjacentCellId!.Value)
            && Orientation == other.Orientation;
    }

    public override bool Equals(object? obj) => Equals(obj as GridBoundaryIdentity);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GridId.GetHashCode();
            hash = (hash * 397) ^ OwnershipScope.GetHashCode();
            hash = (hash * 397) ^ (Lane?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ IsShared.GetHashCode();
            hash = (hash * 397) ^ CellKey(CellId).GetHashCode();
            if (IsShared) hash = (hash * 397) ^ CellKey(AdjacentCellId!.Value).GetHashCode();
            hash = (hash * 397) ^ (IsShared ? Orientation.GetHashCode() : Side.GetHashCode());
            return hash;
        }
    }

    public override string ToString()
    {
        var cells = IsShared
            ? $"{CellKey(CellId)}<->{CellKey(AdjacentCellId!.Value)}"
            : CellKey(CellId);
        return $"{GridId.Value}:{cells}:{(IsShared ? Orientation.ToString() : Side.ToString())}:{Lane?.Value ?? "none"}:{OwnershipScope}";
    }

    private static bool AreComplementary(GridSide first, GridSide second) =>
        (first == GridSide.Left && second == GridSide.Right) ||
        (first == GridSide.Right && second == GridSide.Left) ||
        (first == GridSide.Top && second == GridSide.Bottom) ||
        (first == GridSide.Bottom && second == GridSide.Top);

    private static bool AreAdjacent(PlanningGridCellId first, GridSide firstSide, PlanningGridCellId second, GridSide secondSide)
    {
        if (first.GridId != second.GridId) return false;
        if (firstSide is GridSide.Left or GridSide.Right)
            return first.RowId == second.RowId && Difference(first.ColumnId.Value, second.ColumnId.Value) == 1;
        return first.ColumnId == second.ColumnId && Difference(first.RowId.Value, second.RowId.Value) == 1;
    }

    private static int Difference(string first, string second)
    {
        return Math.Abs(ParseTrackIndex(first) - ParseTrackIndex(second));
    }

    private static int ParseTrackIndex(string value)
    {
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var result) ? result : int.MinValue;
    }

    private static int CompareCells(PlanningGridCellId first, PlanningGridCellId second) =>
        string.Compare(CellKey(first), CellKey(second), StringComparison.Ordinal);

    private static bool SameUnorderedCells(PlanningGridCellId first, PlanningGridCellId second,
        PlanningGridCellId otherFirst, PlanningGridCellId otherSecond) =>
        (first == otherFirst && second == otherSecond) || (first == otherSecond && second == otherFirst);

    private static string CellKey(PlanningGridCellId cell) =>
        $"{cell.GridId.Value}/{cell.RowId.Value}/{cell.ColumnId.Value}";
}

public enum GridBoundaryOrientation
{
    Horizontal,
    Vertical
}

public enum PlannedRouteComponentKind
{
    SourceTerminal,
    SourceDeparture,
    HorizontalStraightRun,
    VerticalStraightRun,
    Turn,
    OwnershipTransition,
    ProjectTransition,
    DestinationApproach,
    DestinationTerminal
}

public sealed record PlannedRouteComponentContract(
    string ComponentId,
    string PhysicalLinkId,
    PlannedRouteComponentKind Kind,
    int Order,
    IReadOnlyList<PlanningGridCellId> Cells,
    GridBoundaryIdentity? EntryBoundary,
    GridBoundaryIdentity? ExitBoundary,
    GridSide? EntrySide,
    GridSide? ExitSide,
    LaneId? Lane,
    string? RunId,
    string? TurnId,
    string OwnershipScope,
    string? PrecedingComponentId,
    string? FollowingComponentId,
    string Provenance);

public sealed record RouteBoundaryContractFinding(
    string Code,
    string PhysicalLinkId,
    string ComponentId,
    string? OtherComponentId,
    string Message,
    string? ExpectedBoundary,
    string? ActualBoundary);

public sealed record PlannedRouteBoundaryContract(
    string PhysicalLinkId,
    RouteTopologyFamily TopologyFamily,
    IReadOnlyList<PlannedRouteComponentContract> Components,
    IReadOnlyList<RouteBoundaryContractFinding> Findings,
    bool IsValid,
    string Provenance);

public sealed record ArchitectureRouteBoundaryValidationResult(
    IReadOnlyList<PlannedRouteBoundaryContract> Routes,
    IReadOnlyList<RouteBoundaryContractFinding> Findings)
{
    public int ValidRouteCount => Routes.Count(route => route.IsValid);
    public int InvalidRouteCount => Routes.Count(route => !route.IsValid);
}
