using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal enum ArchitectureV6OccupancyStatus
{
    Unoccupied,
    Occupied,
    Ambiguous,
    Inconsistent
}

internal sealed record ArchitectureV6OccupancyResolution(
    ArchitectureV6OccupancyStatus Status,
    IReadOnlyList<string> PhysicalNodeIds,
    string? Message)
{
    public bool IsOccupied => Status != ArchitectureV6OccupancyStatus.Unoccupied;
}

/// <summary>
/// Resolves cell ownership from the placement model. Cell capabilities and
/// subtree reservations describe an empty cell; neither can make a placed
/// node footprint routable.
/// </summary>
internal sealed class ArchitectureV6OccupancyAuthority
{
    private readonly Dictionary<PlanningGridCellId, string[]> ownersByCell;
    private readonly Dictionary<string, PlannedNodePlacement> placementsByNode;
    private readonly IReadOnlyDictionary<PlanningGridCellId, PlanningGridCell> cellsById;
    private readonly IReadOnlyDictionary<string, EndpointEnvelopeReservation> endpointReservationsById;

    public ArchitectureV6OccupancyAuthority(
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements,
        IEnumerable<PlanningGrid> grids)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (placements is null) throw new ArgumentNullException(nameof(placements));
        if (grids is null) throw new ArgumentNullException(nameof(grids));

        placementsByNode = placements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        endpointReservationsById = new Dictionary<string, EndpointEnvelopeReservation>(StringComparer.Ordinal);
        ownersByCell = placements
            .SelectMany(placement => placement.Footprint.Select(cell => (cell, placement.PhysicalNodeId)))
            .GroupBy(item => item.cell)
            .ToDictionary(group => group.Key, group => group.Select(item => item.PhysicalNodeId).Distinct(StringComparer.Ordinal).ToArray());
        cellsById = grids.SelectMany(grid => grid.Cells)
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
        Diagnostics = BuildDiagnostics(nodes, placements);
    }

    public ArchitectureV6OccupancyAuthority(
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements,
        IEnumerable<PlanningGrid> grids,
        IEnumerable<EndpointEnvelopeReservation>? endpointReservations)
        : this(nodes, placements, grids)
    {
        endpointReservationsById = (endpointReservations ?? Array.Empty<EndpointEnvelopeReservation>())
            .ToDictionary(item => item.ReservationId, StringComparer.Ordinal);
    }

    public IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics { get; }

    public ArchitectureV6OccupancyResolution Resolve(PlanningGridCellId cellId)
    {
        ownersByCell.TryGetValue(cellId, out var owners);
        owners ??= Array.Empty<string>();
        cellsById.TryGetValue(cellId, out var cell);

        if (owners.Length > 1)
            return new(ArchitectureV6OccupancyStatus.Ambiguous, owners,
                "The cell belongs to more than one physical node footprint.");

        if (cell?.FootprintOwnerId is not null && (owners.Length == 0 || owners[0] != cell.FootprintOwnerId))
            return new(ArchitectureV6OccupancyStatus.Inconsistent,
                owners.Length == 0 ? new[] { cell.FootprintOwnerId } : owners,
                "The cell metadata footprint owner disagrees with logical placement.");

        return owners.Length == 0
            ? new(ArchitectureV6OccupancyStatus.Unoccupied, owners, null)
            : new(ArchitectureV6OccupancyStatus.Occupied, owners, null);
    }

    public bool IsExactEndpointCell(PlanningGridCellId cellId, PlannedGridRouteStep step, PlannedPhysicalLink link)
    {
        if (step.Role == RouteStepRole.SourceExit && step.CellId.Equals(cellId) &&
            cellId.Equals(placementsByNode[link.SourcePhysicalNodeId].AnchorCellId))
            return Resolve(cellId).PhysicalNodeIds.SequenceEqual(new[] { link.SourcePhysicalNodeId }, StringComparer.Ordinal);

        if (step.Role == RouteStepRole.DestinationEntry && step.CellId.Equals(cellId) &&
            cellId.Equals(placementsByNode[link.DestinationPhysicalNodeId].AnchorCellId))
            return Resolve(cellId).PhysicalNodeIds.SequenceEqual(new[] { link.DestinationPhysicalNodeId }, StringComparer.Ordinal);

        return false;
    }

    public bool IsEndpointReservedForOtherRoute(PlanningGridCellId cellId, string physicalLinkId)
    {
        if (!cellsById.TryGetValue(cellId, out var cell)) return false;
        return cell.ReservationIds.Any(reservationId => endpointReservationsById.TryGetValue(reservationId, out var reservation) &&
            !reservation.PhysicalLinkIds.Contains(physicalLinkId, StringComparer.Ordinal));
    }

    private IReadOnlyList<ArchitecturePlanningDiagnostic> BuildDiagnostics(
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements)
    {
        var findings = new List<ArchitecturePlanningDiagnostic>();
        foreach (var group in ownersByCell.Where(item => item.Value.Length > 1))
            findings.Add(new ArchitecturePlanningDiagnostic("AmbiguousNodeFootprintOwnership",
                "A cell belongs to multiple physical node footprints.", PlanningDiagnosticSubject.Cell, group.Key.ToString()));

        foreach (var placement in placements)
        {
            if (!ownersByCell.TryGetValue(placement.AnchorCellId, out var anchorOwners) ||
                anchorOwners.Length != 1 || anchorOwners[0] != placement.PhysicalNodeId)
                findings.Add(new ArchitecturePlanningDiagnostic("OrphanedNodeAnchor",
                    "A node anchor does not resolve to its physical node.", PlanningDiagnosticSubject.Cell, placement.AnchorCellId.ToString()));
        }

        foreach (var cell in cellsById.Values)
        {
            if (cell.FootprintOwnerId is not null && !placementsByNode.ContainsKey(cell.FootprintOwnerId))
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownFootprintOwner",
                    "A cell references an unknown physical footprint owner.", PlanningDiagnosticSubject.Cell, cell.Id.ToString()));

            var resolution = Resolve(cell.Id);
            if (cell.Occupancy == CellOccupancy.NodeAnchor && resolution.Status == ArchitectureV6OccupancyStatus.Unoccupied)
                findings.Add(new ArchitecturePlanningDiagnostic("OrphanedNodeAnchor",
                    "A node anchor cell has no resolvable physical owner.", PlanningDiagnosticSubject.Cell, cell.Id.ToString()));
            if (resolution.Status == ArchitectureV6OccupancyStatus.Inconsistent)
                findings.Add(new ArchitecturePlanningDiagnostic("InconsistentFootprintOwnership",
                    resolution.Message!, PlanningDiagnosticSubject.Cell, cell.Id.ToString()));
        }

        foreach (var node in nodes.Where(item => item.ProjectionMode == PhysicalNodeProjectionMode.DuplicateBranch))
            if (node.DuplicationProvenance is null || !placementsByNode.ContainsKey(node.PhysicalNodeId))
                findings.Add(new ArchitecturePlanningDiagnostic("UnresolvedDuplicateFootprint",
                    "A duplicate physical node has no resolvable footprint provenance.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));

        return findings;
    }
}
