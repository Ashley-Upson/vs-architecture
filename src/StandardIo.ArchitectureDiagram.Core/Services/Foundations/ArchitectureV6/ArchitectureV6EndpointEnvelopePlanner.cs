using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

/// <summary>
/// Builds the bounded, planner-owned endpoint space that must exist before
/// ordinary route topology is selected. It deliberately produces logical
/// reservations only; physical terminal coordinates are assigned later.
/// </summary>
internal sealed class ArchitectureV6EndpointEnvelopePlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;

    public ArchitectureV6EndpointEnvelopePlanner(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedPhysicalLink> links,
        IReadOnlyList<PlannedNodePlacement> placements)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.links = links ?? throw new ArgumentNullException(nameof(links));
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements));
    }

    public ArchitectureEndpointPlanningResult Build(
        IReadOnlyList<ProjectRoutingGrid> projectGrids,
        DiagramRoutingGrid diagramGrid,
        int iteration)
    {
        var diagnostics = new List<ArchitecturePlanningDiagnostic>();
        var demands = new List<TerminalDemand>();
        var groups = new List<TerminalDirectionGroup>();
        var orders = new List<TerminalOrder>();
        var envelopes = new List<LogicalEndpointEnvelope>();
        var reservations = new List<EndpointEnvelopeReservation>();
        var gridsByProject = projectGrids.ToDictionary(item => item.ProjectId, StringComparer.Ordinal);

        foreach (var node in nodes.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
        {
            var placement = placements.SingleOrDefault(item => item.PhysicalNodeId == node.PhysicalNodeId);
            if (placement is null || node.ProjectId is null || !gridsByProject.TryGetValue(node.ProjectId, out var project))
                continue;

            BuildSide(node, placement, project, GridSide.Bottom, links.Where(link => link.SourcePhysicalNodeId == node.PhysicalNodeId).ToArray(),
                iteration, demands, groups, orders, envelopes, reservations, diagnostics);
            BuildSide(node, placement, project, GridSide.Top, links.Where(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId).ToArray(),
                iteration, demands, groups, orders, envelopes, reservations, diagnostics);
        }

        var updatedProjects = projectGrids.Select(project =>
        {
            var owned = reservations.Where(item => item.Cells.Any(cell => cell.GridId.Equals(project.Grid.Id))).ToArray();
            var cells = project.Grid.Cells.ToDictionary(item => item.Key, item => item.Value);
            foreach (var reservation in owned)
            foreach (var cellId in reservation.Cells)
            {
                if (!cells.TryGetValue(cellId, out var cell))
                    cells[cellId] = new PlanningGridCell(cellId, CellCapability.RoutingAllowed, CellOccupancy.Empty,
                        new[] { reservation.ReservationId });
                else if (cell.Occupancy == CellOccupancy.Empty)
                    cells[cellId] = cell with { ReservationIds = cell.ReservationIds.Concat(new[] { reservation.ReservationId }).Distinct(StringComparer.Ordinal).ToArray() };
            }
            return project with { Grid = project.Grid with { Cells = cells }, EndpointReservations = owned };
        }).ToArray();

        return new ArchitectureEndpointPlanningResult(demands, groups, orders, envelopes, reservations, diagnostics,
            updatedProjects, diagramGrid, iteration);
    }

    private void BuildSide(
        PlannedPhysicalNode node,
        PlannedNodePlacement placement,
        ProjectRoutingGrid project,
        GridSide side,
        IReadOnlyList<PlannedPhysicalLink> sideLinks,
        int iteration,
        ICollection<TerminalDemand> demands,
        ICollection<TerminalDirectionGroup> groups,
        ICollection<TerminalOrder> orders,
        ICollection<LogicalEndpointEnvelope> envelopes,
        ICollection<EndpointEnvelopeReservation> reservations,
        ICollection<ArchitecturePlanningDiagnostic> diagnostics)
    {
        if (sideLinks.Count == 0) return;
        var spacing = Math.Max(1, request.RoutePlanning.MinimumPortSpacing);
        var inset = Math.Max(spacing, request.GridSizing.NodeToRouteClearance);
        var width = inset * 2 + Math.Max(0, sideLinks.Count - 1) * spacing;
        var ordered = sideLinks.Select(link => (link, direction: Direction(node, project, link, side)))
            .GroupBy(item => item.direction)
            .OrderBy(group => (int)group.Key)
            .SelectMany(group =>
            {
                var ids = group.OrderBy(item => OtherNodeColumn(project, item.link, node, side))
                    .ThenBy(item => item.link.PhysicalLinkId, StringComparer.Ordinal)
                    .Select(item => item.link.PhysicalLinkId).ToArray();
                var groupRecord = new TerminalDirectionGroup(group.Key, ids, (int)group.Key,
                    $"endpoint-direction:{node.PhysicalNodeId}:{side}:{group.Key}");
                groups.Add(groupRecord);
                return ids.Select((id, index) => (id, groupRecord, index));
            }).ToArray();

        var demand = new TerminalDemand(node.PhysicalNodeId, side, ordered.Select(item => item.id).ToArray(), ordered.Length,
            spacing, inset, width, $"terminal-capacity:{node.PhysicalNodeId}:{side}");
        demands.Add(demand);
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            orders.Add(new TerminalOrder(item.id, node.PhysicalNodeId, side, item.groupRecord.Direction,
                item.groupRecord.LogicalOrder, index, $"direction-order:{node.PhysicalNodeId}:{side}"));
        }

        var placementRows = placement.Footprint.Select(cell => cell.RowId).Distinct().ToArray();
        var rows = project.Grid.Rows.OrderBy(item => item.LogicalOrder).ToArray();
        var minRow = placementRows.Select(row => rows.Single(item => item.Id.Equals(row)).LogicalOrder).Min();
        var maxRow = placementRows.Select(row => rows.Single(item => item.Id.Equals(row)).LogicalOrder).Max();
        var envelopeRow = side == GridSide.Bottom
            ? rows.FirstOrDefault(row => row.LogicalOrder > maxRow)
            : rows.LastOrDefault(row => row.LogicalOrder < minRow);
        if (envelopeRow is null)
        {
            diagnostics.Add(new ArchitecturePlanningDiagnostic("EndpointEnvelopeMissingRow",
                $"No routing row exists immediately {side.ToString().ToLowerInvariant()} of the complete node footprint.",
                PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            return;
        }

        var anchorColumn = project.Grid.Columns.Single(column => column.Id.Equals(placement.AnchorCellId.ColumnId)).LogicalOrder;
        // The envelope reserves endpoint demand, not the node footprint again.
        // Node width is owned by placement; duplicating it here can consume the
        // entire routing band and falsely trigger monotonic node expansion.
        var requestedColumnCount = Math.Max(1, ordered.Length);
        var occupiedByOtherReservation = new HashSet<PlanningGridColumnId>(reservations
            .Where(item => item.Cells.Any(cell => cell.RowId.Equals(envelopeRow.Id)))
            .SelectMany(item => item.Cells)
            .Select(cell => cell.ColumnId));
        var preferredColumns = project.Grid.Columns
            .OrderBy(column => occupiedByOtherReservation.Contains(column.Id) ? 1 : 0)
            .ThenBy(column => Math.Abs(column.LogicalOrder - anchorColumn))
            .ThenBy(column => column.LogicalOrder)
            .Take(requestedColumnCount)
            .Select(column => column.Id).ToArray();
        if (preferredColumns.Length < requestedColumnCount)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("EndpointEnvelopeCapacityInsufficient",
                $"The endpoint envelope for {node.PhysicalNodeId} on {side} requires {requestedColumnCount} columns but the existing row exposes only {preferredColumns.Length} available columns after reservation separation.",
                PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        var cells = preferredColumns.Select(column => new PlanningGridCellId(project.Grid.Id, envelopeRow.Id, column)).ToArray();
        var reservationId = $"endpoint-envelope:{node.PhysicalNodeId}:{side}";
        var reservation = new EndpointEnvelopeReservation(reservationId, node.PhysicalNodeId, side,
            demand.PhysicalLinkIds, cells,
            side == GridSide.Bottom ? EndpointReservationRole.SourceEndpointEnvelope : EndpointReservationRole.DestinationEndpointEnvelope,
            iteration, $"bounded-envelope:{node.PhysicalNodeId}:{side}:links={demand.TerminalCount}:spacing={spacing}:inset={inset}");
        reservations.Add(reservation);
        envelopes.Add(new LogicalEndpointEnvelope(reservationId, node.PhysicalNodeId, side, demand.PhysicalLinkIds,
            cells, width, Math.Max(1, request.GridSizing.RoutingRowMinimum), groups.Where(group => demand.PhysicalLinkIds.Intersect(group.PhysicalLinkIds, StringComparer.Ordinal).Any()).ToArray(), reservation.Provenance));
    }

    private TerminalDirectionGroupKind Direction(PlannedPhysicalNode node, ProjectRoutingGrid project, PlannedPhysicalLink link, GridSide side)
    {
        var otherId = side == GridSide.Bottom ? link.DestinationPhysicalNodeId : link.SourcePhysicalNodeId;
        var other = placements.SingleOrDefault(item => item.PhysicalNodeId == otherId);
        var current = placements.Single(item => item.PhysicalNodeId == node.PhysicalNodeId);
        if (other is null) return TerminalDirectionGroupKind.Down;
        var currentColumn = project.Grid.Columns.Single(column => column.Id.Equals(current.AnchorCellId.ColumnId)).LogicalOrder;
        var otherColumn = project.Grid.Columns.Single(column => column.Id.Equals(other.AnchorCellId.ColumnId)).LogicalOrder;
        var comparison = otherColumn.CompareTo(currentColumn);
        return comparison < 0 ? TerminalDirectionGroupKind.Left : comparison > 0 ? TerminalDirectionGroupKind.Right : TerminalDirectionGroupKind.Down;
    }

    private int OtherNodeColumn(ProjectRoutingGrid project, PlannedPhysicalLink link, PlannedPhysicalNode node, GridSide side)
    {
        var otherId = side == GridSide.Bottom ? link.DestinationPhysicalNodeId : link.SourcePhysicalNodeId;
        var other = placements.SingleOrDefault(item => item.PhysicalNodeId == otherId);
        return other is null ? int.MaxValue : project.Grid.Columns.Single(column => column.Id.Equals(other.AnchorCellId.ColumnId)).LogicalOrder;
    }
}
