using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7TreeGridNodePlacement(
    string PhysicalNodeId,
    string SemanticNodeId,
    int LocalRow,
    int LocalColumn,
    int LogicalSpan,
    int CentreCell,
    int NodeLayer,
    bool IsDetached,
    string Provenance);

public sealed record ArchitectureV7TreeGridCell(
    int Row,
    int Column,
    ArchitectureV7CellCapability Capabilities,
    string? OccupantId = null);

public sealed class ArchitectureV7TreeGridPlacementUnit
{
    public ArchitectureV7TreeGridPlacementUnit(
        string unitId,
        string rootPhysicalNodeId,
        IReadOnlyList<ArchitectureV7TreeGridNodePlacement> placements,
        int width,
        int height,
        int rootCentreCell,
        int rootRow,
        bool isDetached,
        string provenance,
        IReadOnlyList<ArchitectureV7TreeGridCell>? cells = null)
    {
        UnitId = unitId ?? throw new ArgumentNullException(nameof(unitId));
        RootPhysicalNodeId = rootPhysicalNodeId ?? throw new ArgumentNullException(nameof(rootPhysicalNodeId));
        Placements = Array.AsReadOnly((placements ?? Array.Empty<ArchitectureV7TreeGridNodePlacement>()).OrderBy(item => item.LocalRow).ThenBy(item => item.LocalColumn).ThenBy(item => item.PhysicalNodeId, StringComparer.Ordinal).ToArray());
        Width = width;
        Height = height;
        RootCentreCell = rootCentreCell;
        RootRow = rootRow;
        IsDetached = isDetached;
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        Cells = Array.AsReadOnly((cells ?? Array.Empty<ArchitectureV7TreeGridCell>()).OrderBy(item => item.Row).ThenBy(item => item.Column).ToArray());
    }

    public string UnitId { get; }
    public string RootPhysicalNodeId { get; }
    public IReadOnlyList<ArchitectureV7TreeGridNodePlacement> Placements { get; }
    public int Width { get; }
    public int Height { get; }
    public int RootCentreCell { get; }
    public int RootRow { get; }
    public bool IsDetached { get; }
    public string Provenance { get; }
    public IReadOnlyList<ArchitectureV7TreeGridCell> Cells { get; }
}

public sealed class ArchitectureV7TopLevelTreeGrid
{
    public ArchitectureV7TopLevelTreeGrid(
        string treeId,
        string rootPhysicalNodeId,
        ArchitectureV7TreeGridPlacementUnit mainUnit,
        IReadOnlyList<ArchitectureV7TreeGridPlacementUnit> detachedUnits,
        IReadOnlyList<ArchitectureV7TreeGridNodePlacement> placements,
        int width,
        int height,
        string projectionFreezeFingerprint,
        string ownershipFreezeFingerprint,
        string spanFreezeFingerprint,
        string reservationFingerprint,
        string provenance,
        int analyserOrdinal = -1,
        IReadOnlyList<ArchitectureV7TreeGridCell>? cells = null,
        long constructionElapsedMilliseconds = 0)
    {
        TreeId = treeId ?? throw new ArgumentNullException(nameof(treeId));
        RootPhysicalNodeId = rootPhysicalNodeId ?? throw new ArgumentNullException(nameof(rootPhysicalNodeId));
        MainUnit = mainUnit ?? throw new ArgumentNullException(nameof(mainUnit));
        DetachedUnits = Array.AsReadOnly((detachedUnits ?? Array.Empty<ArchitectureV7TreeGridPlacementUnit>()).ToArray());
        Placements = Array.AsReadOnly((placements ?? Array.Empty<ArchitectureV7TreeGridNodePlacement>()).OrderBy(item => item.LocalRow).ThenBy(item => item.LocalColumn).ThenBy(item => item.PhysicalNodeId, StringComparer.Ordinal).ToArray());
        Width = width;
        Height = height;
        ProjectionFreezeFingerprint = projectionFreezeFingerprint ?? throw new ArgumentNullException(nameof(projectionFreezeFingerprint));
        OwnershipFreezeFingerprint = ownershipFreezeFingerprint ?? throw new ArgumentNullException(nameof(ownershipFreezeFingerprint));
        SpanFreezeFingerprint = spanFreezeFingerprint ?? throw new ArgumentNullException(nameof(spanFreezeFingerprint));
        ReservationFingerprint = reservationFingerprint ?? throw new ArgumentNullException(nameof(reservationFingerprint));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        AnalyserOrdinal = analyserOrdinal;
        Cells = Array.AsReadOnly((cells ?? Array.Empty<ArchitectureV7TreeGridCell>()).OrderBy(item => item.Row).ThenBy(item => item.Column).ToArray());
        ConstructionElapsedMilliseconds = constructionElapsedMilliseconds;
    }

    public string TreeId { get; }
    public string RootPhysicalNodeId { get; }
    public ArchitectureV7TreeGridPlacementUnit MainUnit { get; }
    public IReadOnlyList<ArchitectureV7TreeGridPlacementUnit> DetachedUnits { get; }
    public IReadOnlyList<ArchitectureV7TreeGridNodePlacement> Placements { get; }
    public int Width { get; }
    public int Height { get; }
    public string ProjectionFreezeFingerprint { get; }
    public string OwnershipFreezeFingerprint { get; }
    public string SpanFreezeFingerprint { get; }
    public string ReservationFingerprint { get; }
    public string Provenance { get; }
    public int AnalyserOrdinal { get; }
    public IReadOnlyList<ArchitectureV7TreeGridCell> Cells { get; }
    public long ConstructionElapsedMilliseconds { get; }
}

public sealed class ArchitectureV7RecursiveTreeGridResult
{
    public ArchitectureV7RecursiveTreeGridResult(
        ArchitectureV7NodeSpanSizingResult sizing,
        ArchitectureV7FrozenReservationTable reservations,
        IReadOnlyList<ArchitectureV7TopLevelTreeGrid> trees,
        string freezeFingerprint,
        int parallelWorkerCount = 0,
        long parallelConstructionElapsedMilliseconds = 0,
        long deterministicJoinElapsedMilliseconds = 0,
        ArchitectureV7FrozenLayerSchedule? layerSchedule = null)
    {
        Sizing = sizing ?? throw new ArgumentNullException(nameof(sizing));
        Reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        Trees = Array.AsReadOnly((trees ?? Array.Empty<ArchitectureV7TopLevelTreeGrid>()).ToArray());
        FreezeFingerprint = freezeFingerprint ?? throw new ArgumentNullException(nameof(freezeFingerprint));
        ParallelWorkerCount = parallelWorkerCount;
        ParallelConstructionElapsedMilliseconds = parallelConstructionElapsedMilliseconds;
        DeterministicJoinElapsedMilliseconds = deterministicJoinElapsedMilliseconds;
        LayerSchedule = layerSchedule;
    }

    public ArchitectureV7NodeSpanSizingResult Sizing { get; }
    public ArchitectureV7FrozenReservationTable Reservations { get; }
    public IReadOnlyList<ArchitectureV7TopLevelTreeGrid> Trees { get; }
    public string FreezeFingerprint { get; }
    public int ParallelWorkerCount { get; }
    public long ParallelConstructionElapsedMilliseconds { get; }
    public long DeterministicJoinElapsedMilliseconds { get; }
    public ArchitectureV7FrozenLayerSchedule? LayerSchedule { get; }
}
