using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

[Flags]
public enum ArchitectureV7CellCapability
{
    None = 0,
    RoutingAllowed = 1,
    NodeAllowed = 2,
    ProjectBoundary = 4,
    StraightPassthroughOnly = 8,
    HeaderBlocked = 16,
    GeneralRouting = 32,
    Blocked = 64
}

public sealed record ArchitectureV7LogicalCell(int Row, int Column, ArchitectureV7CellCapability Capabilities, string? OccupantId = null);

public sealed record ArchitectureV7ProjectTransform(
    string ProjectId,
    int RegionOriginRow,
    int RegionOriginColumn,
    int InteriorOriginRow,
    int InteriorOriginColumn,
    int Width,
    int Height);

public sealed class ArchitectureV7ProjectRegion
{
    public ArchitectureV7ProjectRegion(
        string projectId,
        ArchitectureV7ProjectTransform transform,
        IReadOnlyList<string> treeIds,
        IReadOnlyList<ArchitectureV7LogicalCell> cells,
        int interiorWidth,
        int interiorHeight)
    {
        ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        Transform = transform ?? throw new ArgumentNullException(nameof(transform));
        TreeIds = Array.AsReadOnly((treeIds ?? Array.Empty<string>()).ToArray());
        Cells = Array.AsReadOnly((cells ?? Array.Empty<ArchitectureV7LogicalCell>()).ToArray());
        InteriorWidth = interiorWidth;
        InteriorHeight = interiorHeight;
    }

    public string ProjectId { get; }
    public ArchitectureV7ProjectTransform Transform { get; }
    public IReadOnlyList<string> TreeIds { get; }
    public IReadOnlyList<ArchitectureV7LogicalCell> Cells { get; }
    public int InteriorWidth { get; }
    public int InteriorHeight { get; }
    public int Width => Transform.Width;
    public int Height => Transform.Height;
}

public sealed record ArchitectureV7FrozenNodePlacement(
    string PhysicalNodeId,
    string SemanticNodeId,
    string? ProjectId,
    int DiagramRow,
    int DiagramColumn,
    int LogicalSpan,
    int CentreCell,
    IReadOnlyList<(int Row, int Column)> LogicalFootprint,
    bool IsExternal,
    bool IsStandalone,
    bool IsDetached,
    string TreeId,
    string VisibleLabel,
    string FullName,
    string Provenance);

public sealed record ArchitectureV7ExternalRegion(
    int NodeRow,
    IReadOnlyList<string> PhysicalNodeIds,
    IReadOnlyList<ArchitectureV7FrozenNodePlacement> Placements);

public sealed record ArchitectureV7StandaloneRegion(
    int FirstNodeRow,
    int Width,
    int Height,
    IReadOnlyList<string> PhysicalNodeIds,
    IReadOnlyList<ArchitectureV7FrozenNodePlacement> Placements);

public sealed class ArchitectureV7CommonDiagramGrid
{
    public ArchitectureV7CommonDiagramGrid(int rowCount, int columnCount, IReadOnlyList<ArchitectureV7LogicalCell> cells)
    {
        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = Array.AsReadOnly((cells ?? Array.Empty<ArchitectureV7LogicalCell>()).ToArray());
    }

    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<ArchitectureV7LogicalCell> Cells { get; }
}

public sealed class ArchitectureV7PlacementFreeze
{
    public ArchitectureV7PlacementFreeze(
        IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes,
        IReadOnlyList<ArchitectureV7ProjectRegion> projects,
        ArchitectureV7ExternalRegion external,
        ArchitectureV7StandaloneRegion standalone,
        ArchitectureV7CommonDiagramGrid diagramGrid,
        IReadOnlyList<ArchitectureV7ProjectTransform> transforms,
        string projectionFingerprint,
        string ownershipFingerprint,
        string sizingFingerprint,
        string reservationFingerprint,
        string placementFingerprint)
    {
        Nodes = Array.AsReadOnly((nodes ?? Array.Empty<ArchitectureV7FrozenNodePlacement>()).ToArray());
        Projects = Array.AsReadOnly((projects ?? Array.Empty<ArchitectureV7ProjectRegion>()).ToArray());
        External = external ?? throw new ArgumentNullException(nameof(external));
        Standalone = standalone ?? throw new ArgumentNullException(nameof(standalone));
        DiagramGrid = diagramGrid ?? throw new ArgumentNullException(nameof(diagramGrid));
        Transforms = Array.AsReadOnly((transforms ?? Array.Empty<ArchitectureV7ProjectTransform>()).ToArray());
        ProjectionFingerprint = projectionFingerprint ?? throw new ArgumentNullException(nameof(projectionFingerprint));
        OwnershipFingerprint = ownershipFingerprint ?? throw new ArgumentNullException(nameof(ownershipFingerprint));
        SizingFingerprint = sizingFingerprint ?? throw new ArgumentNullException(nameof(sizingFingerprint));
        ReservationFingerprint = reservationFingerprint ?? throw new ArgumentNullException(nameof(reservationFingerprint));
        PlacementFingerprint = placementFingerprint ?? throw new ArgumentNullException(nameof(placementFingerprint));
    }

    public IReadOnlyList<ArchitectureV7FrozenNodePlacement> Nodes { get; }
    public IReadOnlyList<ArchitectureV7ProjectRegion> Projects { get; }
    public ArchitectureV7ExternalRegion External { get; }
    public ArchitectureV7StandaloneRegion Standalone { get; }
    public ArchitectureV7CommonDiagramGrid DiagramGrid { get; }
    public IReadOnlyList<ArchitectureV7ProjectTransform> Transforms { get; }
    public string ProjectionFingerprint { get; }
    public string OwnershipFingerprint { get; }
    public string SizingFingerprint { get; }
    public string ReservationFingerprint { get; }
    public string PlacementFingerprint { get; }
}
