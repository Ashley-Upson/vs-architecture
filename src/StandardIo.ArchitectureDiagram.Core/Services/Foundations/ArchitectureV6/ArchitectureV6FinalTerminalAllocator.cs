using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

/// <summary>
/// Assigns final edge coordinates after track sizing. This stage is the only
/// owner of final terminal X; ordinary run lanes remain route targets only.
/// </summary>
internal sealed class ArchitectureV6FinalTerminalAllocator
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;

    public ArchitectureV6FinalTerminalAllocator(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalLink> links,
        IReadOnlyList<PlannedPhysicalNode> nodes)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.links = links ?? throw new ArgumentNullException(nameof(links));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    public ArchitectureLaneAllocationResult Build(
        ArchitectureLaneAllocationResult allocation,
        PlannedArchitectureRelativeGeometry relative,
        ArchitectureEndpointPlanningResult endpointPlanning,
        int iteration)
    {
        var slots = new List<FinalTerminalSlot>();
        var requirements = new List<ArchitectureConvergenceRequirement>();
        var diagnostics = allocation.Diagnostics.ToList();

        foreach (var group in endpointPlanning.Orders.GroupBy(item => item.PhysicalNodeId + ":" + item.Side, StringComparer.Ordinal))
        {
            var first = group.First();
            var geometry = relative.Nodes.SingleOrDefault(item => item.PhysicalNodeId == first.PhysicalNodeId);
            var node = nodes.SingleOrDefault(item => item.PhysicalNodeId == first.PhysicalNodeId);
            if (geometry is null || node is null) continue;
            var bounds = geometry.VisibleBounds ?? geometry.Bounds;

            var ordered = group.OrderBy(item => item.GlobalOrder).ThenBy(item => item.PhysicalLinkId, StringComparer.Ordinal).ToArray();
            var spacing = Math.Max(1, request.RoutePlanning.MinimumPortSpacing);
            var inset = Math.Max(spacing, request.GridSizing.NodeToRouteClearance);
            var span = Math.Max(0, ordered.Length - 1) * spacing;
            var left = bounds.X + bounds.Width / 2d - span / 2d;
            var right = left + span;
            var minimum = bounds.X + inset;
            var maximum = bounds.X + bounds.Width - inset;
            if (left < minimum || right > maximum)
            {
                var requiredWidth = (int)Math.Ceiling(Math.Max((double)bounds.Width, span + inset * 2d));
                requirements.Add(new ArchitectureConvergenceRequirement(
                    ArchitectureConvergenceRequirementKind.EndpointEnvelopeExpansion,
                    first.PhysicalNodeId,
                    bounds.Width,
                    requiredWidth,
                    nameof(ArchitectureV6FinalTerminalAllocator),
                    $"Final terminal group requires {requiredWidth}px within the final visible node edge.",
                    ordered.Select(item => item.PhysicalLinkId).ToArray(), iteration));
                diagnostics.Add(new ArchitecturePlanningDiagnostic("FinalTerminalCapacityInsufficient",
                    $"Final terminal group does not fit within the final visible edge; required width={requiredWidth}.",
                    PlanningDiagnosticSubject.PhysicalNode, first.PhysicalNodeId));
            }

            for (var index = 0; index < ordered.Length; index++)
            {
                var y = first.Side == GridSide.Bottom
                    ? bounds.Y + bounds.Height
                    : bounds.Y;
                slots.Add(new FinalTerminalSlot(ordered[index].PhysicalLinkId, first.PhysicalNodeId, first.Side,
                    new RelativePoint((int)Math.Round(left + index * spacing), y), ordered[index].GlobalOrder,
                    $"final-edge-slot:{first.PhysicalNodeId}:{first.Side}:{index}"));
            }
        }

        var handoffs = links.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal).Select(link =>
        {
            var route = allocation.Routes.SingleOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId);
            var cells = route?.Steps.Select(step => step.CellId).ToArray() ?? Array.Empty<PlanningGridCellId>();
            var ordinary = route?.Steps
                .Where(step => step.Role is RouteStepRole.HorizontalPassThrough or RouteStepRole.VerticalPassThrough or RouteStepRole.Turn)
                .OrderBy(step => step.Order)
                .ToArray() ?? Array.Empty<PlannedGridRouteStep>();
            var first = ordinary.FirstOrDefault();
            var last = ordinary.LastOrDefault();
            var sourceBoundarySide = first is not null && IsVertical(first)
                ? first.ExitSide
                : first?.EntrySide ?? GridSide.Bottom;
            return new EndpointHandoff(link.PhysicalLinkId, GridSide.Bottom, cells, cells,
                $"planner-owned-handoff:{link.PhysicalLinkId}",
                first is null ? null : HandoffPoint(link.PhysicalLinkId, first, sourceBoundarySide, relative, allocation),
                last is null ? null : HandoffPoint(link.PhysicalLinkId, last, last.ExitSide, relative, allocation));
        }).ToArray();

        return allocation with
        {
            Diagnostics = diagnostics,
            FinalTerminalSlots = slots,
            EndpointHandoffs = handoffs,
            ConvergenceRequirements = requirements
        };
    }

    private RelativePoint RoutePoint(PlannedGridRouteStep step, PlannedArchitectureRelativeGeometry relative,
        ArchitectureLaneAllocationResult allocation)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null) return new RelativePoint(0, 0);
        var x = column.RelativeOffset + column.FinalExtent / 2;
        var y = row.RelativeOffset + row.FinalExtent / 2;
        var horizontal = step.EntrySide is GridSide.Left or GridSide.Right || step.ExitSide is GridSide.Left or GridSide.Right;
        if (horizontal && step.AllocatedLane is not null)
            y = ArchitectureV6LaneGeometry.Coordinate(row.RelativeOffset, allocation.HorizontalLanes.FirstOrDefault(item => item.Lane == step.AllocatedLane)?.Ordinal ?? 0,
                request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
        if (!horizontal && step.AllocatedLane is not null)
            x = ArchitectureV6LaneGeometry.Coordinate(column.RelativeOffset, allocation.VerticalLanes.FirstOrDefault(item => item.Lane == step.AllocatedLane)?.Ordinal ?? 0,
                request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
        return new RelativePoint(x, y);
    }

    private RelativePoint BoundaryPoint(PlannedGridRouteStep step, GridSide side,
        PlannedArchitectureRelativeGeometry relative, ArchitectureLaneAllocationResult allocation)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null) return new RelativePoint(0, 0);

        var x = column.RelativeOffset + column.FinalExtent / 2;
        var y = row.RelativeOffset + row.FinalExtent / 2;
        if (side is GridSide.Left or GridSide.Right)
        {
            x = side == GridSide.Left ? column.RelativeOffset : column.RelativeOffset + column.FinalExtent;
            if (step.AllocatedLane is not null)
            {
                var lane = allocation.HorizontalLanes.FirstOrDefault(item => item.Lane == step.AllocatedLane);
                y = ArchitectureV6LaneGeometry.Coordinate(row.RelativeOffset, lane?.Ordinal ?? 0,
                    request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
            }
        }
        else
        {
            y = side == GridSide.Top ? row.RelativeOffset : row.RelativeOffset + row.FinalExtent;
            if (step.AllocatedLane is not null)
            {
                var lane = allocation.VerticalLanes.FirstOrDefault(item => item.Lane == step.AllocatedLane);
                x = ArchitectureV6LaneGeometry.Coordinate(column.RelativeOffset, lane?.Ordinal ?? 0,
                    request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
            }
        }
        return new RelativePoint(x, y);
    }

    private RelativePoint HandoffPoint(string physicalLinkId, PlannedGridRouteStep step, GridSide side,
        PlannedArchitectureRelativeGeometry relative, ArchitectureLaneAllocationResult allocation)
    {
        if (step.Role == RouteStepRole.Turn)
        {
            var turn = allocation.Turns
                .Where(item => item.RouteId == physicalLinkId && item.CellId == step.CellId.ToString())
                .OrderBy(item => item.BendIdentity, StringComparer.Ordinal)
                .FirstOrDefault();
            var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
            var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
            var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
            if (turn is not null && row is not null && column is not null)
            {
                var horizontal = allocation.HorizontalLanes.FirstOrDefault(item => item.RunId == turn.HorizontalRunId);
                var vertical = allocation.VerticalLanes.FirstOrDefault(item => item.RunId == turn.VerticalRunId);
                var x = vertical is null
                    ? column.RelativeOffset + column.FinalExtent / 2
                    : ArchitectureV6LaneGeometry.Coordinate(column.RelativeOffset, vertical.Ordinal,
                        request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
                var y = horizontal is null
                    ? row.RelativeOffset + row.FinalExtent / 2
                    : ArchitectureV6LaneGeometry.Coordinate(row.RelativeOffset, horizontal.Ordinal,
                        request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
                return new RelativePoint(x, y);
            }
        }
        return BoundaryPoint(step, side, relative, allocation);
    }

    private static bool IsVertical(PlannedGridRouteStep step) =>
        step.EntrySide is GridSide.Top or GridSide.Bottom &&
        step.ExitSide is GridSide.Top or GridSide.Bottom;
}
