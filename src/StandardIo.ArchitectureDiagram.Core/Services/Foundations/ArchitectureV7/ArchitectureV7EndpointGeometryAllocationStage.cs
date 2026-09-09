using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>
/// Assigns endpoint slots after physical row/lane coordinates exist.  This
/// stage owns the relationship-to-terminal mapping; scene compilation only
/// materialises its result.
/// </summary>
public sealed class ArchitectureV7EndpointGeometryAllocationStage
{
    public ArchitectureV7CollectiveAllocationFreeze Allocate(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (routes is null) throw new ArgumentNullException(nameof(routes));
        if (allocation is null) throw new ArgumentNullException(nameof(allocation));
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));

        // Physical compilation may be invoked once to establish dimensions and
        // again to materialise the endpoint-adjusted freeze.  Endpoint geometry
        // is authoritative for a freeze, so applying this stage a second time
        // must be a no-op rather than creating a new fingerprint or remapping
        // an already ordered slot set.
        if (allocation.AllocationFingerprint.IndexOf("|endpoint-geometry:", StringComparison.Ordinal) >= 0)
            return allocation;

        var runs = allocation.Runs;
        var assignments = allocation.RunAssignments;
        var remapped = allocation.Terminals.ToDictionary(
            terminal => (terminal.PhysicalLinkId, terminal.EndpointKind), StringTupleComparer.Instance);

        foreach (var group in allocation.Terminals.GroupBy(x => (x.PhysicalNodeId, x.EndpointKind)))
        {
            var node = nodes.FirstOrDefault(x => x.PhysicalNodeId == group.Key.PhysicalNodeId);
            if (node is null) continue;

            foreach (var directionGroup in new[] { 0, 2 })
            {
                var groupTerminals = group.Where(terminal => TerminalGroup(terminal) == directionGroup).ToArray();
                var flexible = groupTerminals.Where(terminal => !IsFixedX(routes, runs, terminal)).ToArray();
                if (flexible.Length < 2) continue;

                var targetSlots = flexible
                    .OrderBy(terminal => directionGroup == 0 ? terminal.RelativeOffset : -terminal.RelativeOffset)
                    .ToArray();
                var ordered = flexible
                    .Select(terminal => (Terminal: terminal, Depth: ApproachDepth(routes, runs, assignments, rows, nodes, terminal)))
                    .OrderBy(x => x.Depth)
                    .ThenBy(x => x.Terminal.PhysicalLinkId, StringComparer.Ordinal)
                    .ToArray();

                for (var index = 0; index < ordered.Length; index++)
                {
                    var source = ordered[index].Terminal;
                    var target = targetSlots[index];
                    remapped[(source.PhysicalLinkId, source.EndpointKind)] = source with
                    {
                        SlotOrdinal = target.SlotOrdinal,
                        RelativeOffset = target.RelativeOffset,
                        SignedSlotOrdinal = target.SignedSlotOrdinal,
                        Provenance = source.Provenance + ";endpoint-remap=actual-bend-depth"
                    };
                }
            }
        }

        var terminals = remapped.Values.ToArray();
        var terminalByKey = terminals.ToDictionary(x => (x.PhysicalLinkId, x.EndpointKind), StringTupleComparer.Instance);
        var approaches = allocation.Approaches.Select(approach =>
        {
            return terminalByKey.TryGetValue((approach.PhysicalLinkId, approach.EndpointKind), out var terminal)
                ? approach with { TerminalSlotOrdinal = terminal.SlotOrdinal }
                : approach;
        }).ToArray();
        var endpointCoordinates = allocation.EndpointLaneCoordinates.Select(coordinate =>
        {
            return terminalByKey.TryGetValue((coordinate.PhysicalLinkId, coordinate.EndpointKind), out var terminal)
                ? coordinate with
                {
                    RelativeXOffset = terminal.RelativeOffset,
                    SignedLaneOrdinal = terminal.SignedSlotOrdinal,
                    Provenance = coordinate.Provenance + ";endpoint-geometry=actual-bend-depth"
                }
                : coordinate;
        }).ToArray();

        // Node-span corridors and column lanes have distinct coordinate
        // origins. Finalise their collective compatibility before freezing
        // geometry; the compiler never chooses between these authorities.
        var corridors = AllocateCorridors(allocation, routes, terminals, rows, columns, nodes);
        allocation = ArchitectureV7CollectivePostRoutingAllocationStage.AllocateCorridorCompatibility(allocation, corridors, rows, columns);
        corridors = AllocateCorridors(allocation, routes, terminals, rows, columns, nodes);
        var endpointZBends = allocation.EndpointZBends.Where(z => !corridors.Any(c =>
            c.PhysicalLinkId == z.PhysicalLinkId && c.EndpointKind == z.EndpointKind)).ToList();
        foreach (var constraint in allocation.SharedVerticalRunConstraints)
        {
            var source = terminals.FirstOrDefault(item => item.PhysicalLinkId == constraint.PhysicalLinkId &&
                item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var sourceNode = source is null ? null : nodes.FirstOrDefault(item => item.PhysicalNodeId == source.PhysicalNodeId);
            if (source is null || sourceNode is null) continue;
            // The shared run's physical lane is the fixed-X authority.  The
            // node midpoint plus terminal offset is only the packed-terminal
            // candidate; using it as the fixed coordinate misses displaced
            // maximal vertical relationships when column centres and node
            // bounds are not identical.
            var fixedX = SharedRunPhysicalX(constraint, allocation, columns);
            if (double.IsNaN(fixedX)) continue;
            foreach (var endpoint in terminals.Where(item => item.PhysicalLinkId == constraint.PhysicalLinkId))
            {
                if (corridors.Any(c => c.PhysicalLinkId == endpoint.PhysicalLinkId && c.EndpointKind == endpoint.EndpointKind)) continue;
                var node = nodes.FirstOrDefault(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
                if (node is null) continue;
                var packedX = (node.Bounds.Left + node.Bounds.Right) / 2d + endpoint.RelativeOffset;
                if (Math.Abs(fixedX - packedX) <= 0.001) continue;
                var edgePopulation = terminals.Count(item => item.PhysicalNodeId == endpoint.PhysicalNodeId &&
                    item.EndpointKind == endpoint.EndpointKind);
                if (!CanAllocateEndpointLocalZ(endpoint, constraint, node, edgePopulation)) continue;
                if (endpointZBends.Any(item => item.PhysicalLinkId == endpoint.PhysicalLinkId && item.EndpointKind == endpoint.EndpointKind)) continue;
                endpointZBends.Add(new ArchitectureV7EndpointZBend(endpoint.PhysicalLinkId, endpoint.PhysicalNodeId,
                    endpoint.EndpointKind, constraint.RunId,
                    "endpoint-z-bend;fixed-incoming-x;fit-first-packed-terminal;physical-shared-run-anchor;final-drop-terminal-x"));
            }
        }

        var diagnostics = allocation.Diagnostics.ToList();
        var grid = placement.DiagramGrid.Cells.ToDictionary(cell => (cell.Row, cell.Column));
        foreach (var conflict in ArchitectureV7EndpointCorridorConflictAudit.Find(allocation, corridors, rows, columns))
            diagnostics.Add(new("ENDPOINT-CORRIDOR-ORDINARY-LANE-OVERLAP", $"{conflict.CorridorId} overlaps {conflict.OrdinaryRunId} at X={conflict.X}, Y={conflict.StartY}..{conflict.EndY}.",
                true, conflict.PhysicalLinkId, conflict.OrdinaryRunId));
        foreach (var corridor in corridors)
        {
            if (grid.Count > 0 && corridor.X != corridor.OrdinaryX)
                for (var column = 0; column < columns.Count; column++)
                {
                    if (columns[column].End <= Math.Min(corridor.X, corridor.OrdinaryX) || columns[column].Start >= Math.Max(corridor.X, corridor.OrdinaryX)) continue;
                    if (!grid.TryGetValue((corridor.RoutingRow, column), out var cell) ||
                        (cell.Capabilities & ArchitectureV7CellCapability.GeneralRouting) == 0 ||
                        (cell.Capabilities & (ArchitectureV7CellCapability.Blocked | ArchitectureV7CellCapability.HeaderBlocked)) != 0)
                        diagnostics.Add(new("ENDPOINT-CORRIDOR-ROUTING-ROW-CONFLICT", $"Allocated transition crosses unavailable GeneralRouting cell ({corridor.RoutingRow},{column}); capabilities={cell?.Capabilities}; owner={cell?.OccupantId}.", true, corridor.PhysicalLinkId, corridor.HorizontalRunId));
                }
            if (corridor.X < corridor.SpanLeft || corridor.X > corridor.SpanRight)
                diagnostics.Add(new("ENDPOINT-CORRIDOR-SPAN-VIOLATION", "Endpoint corridor lies outside its owning node span.", true, corridor.PhysicalLinkId, corridor.ResourceId));
            foreach (var other in corridors.Where(c => string.CompareOrdinal(c.ResourceId, corridor.ResourceId) > 0 && c.PhysicalLinkId != corridor.PhysicalLinkId && c.X == corridor.X))
                if (Math.Min(Math.Max(corridor.NodeEdgeY, corridor.RoutingY), Math.Max(other.NodeEdgeY, other.RoutingY)) >
                    Math.Max(Math.Min(corridor.NodeEdgeY, corridor.RoutingY), Math.Min(other.NodeEdgeY, other.RoutingY)))
                    diagnostics.Add(new("ENDPOINT-CORRIDOR-OVERLAP", "Unrelated endpoint corridors overlap: " + other.ResourceId, true, corridor.PhysicalLinkId, corridor.ResourceId));
            foreach (var node in nodes.Where(n => n.PhysicalNodeId != corridor.PhysicalNodeId && corridor.X > n.Bounds.Left && corridor.X < n.Bounds.Right))
                if (Math.Min(Math.Max(corridor.NodeEdgeY, corridor.RoutingY), node.Bounds.Bottom) > Math.Max(Math.Min(corridor.NodeEdgeY, corridor.RoutingY), node.Bounds.Top))
                    diagnostics.Add(new("ENDPOINT-CORRIDOR-NODE-CROSSING", "Endpoint corridor crosses unrelated node " + node.PhysicalNodeId, true, corridor.PhysicalLinkId, corridor.ResourceId));
        }
        return allocation.WithEndpointGeometry(terminals, approaches, endpointCoordinates, endpointZBends, corridors, diagnostics);
    }

    private static IReadOnlyList<ArchitectureV7EndpointCorridor> AllocateCorridors(
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes)
    {
        var result = new List<ArchitectureV7EndpointCorridor>();
        foreach (var terminal in terminals)
        {
            var route = routes.Routes.First(r => r.PhysicalLinkId == terminal.PhysicalLinkId);
            var source = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture;
            var vertical = allocation.Runs.FirstOrDefault(r => r.PhysicalLinkId == terminal.PhysicalLinkId &&
                r.Orientation == ArchitectureV7RunOrientation.Vertical &&
                (source ? r.StartRouteIndex == 0 : r.EndRouteIndex == route.Cells.Count - 1));
            if (vertical is null) continue;
            var index = source ? 1 : route.Cells.Count - 2;
            var horizontal = allocation.Runs.FirstOrDefault(r => r.PhysicalLinkId == terminal.PhysicalLinkId &&
                r.EndpointContext == "endpoint-transition:" + terminal.EndpointKind);
            if (horizontal is null && vertical.EndRouteIndex - vertical.StartRouteIndex == 1)
                horizontal = allocation.Runs.FirstOrDefault(r => r.PhysicalLinkId == terminal.PhysicalLinkId &&
                    r.Orientation == ArchitectureV7RunOrientation.Horizontal &&
                    (source ? r.StartRouteIndex == index : r.EndRouteIndex == index));
            if (horizontal is null) continue;
            var node = nodes.First(n => n.PhysicalNodeId == terminal.PhysicalNodeId);
            var horizontalLane = allocation.RunAssignments.First(a => a.RunId == horizontal.RunId);
            var verticalLane = allocation.RunAssignments.First(a => a.RunId == vertical.RunId);
            var x = Math.Round((node.Bounds.Left + node.Bounds.Right) / 2d + terminal.RelativeOffset, MidpointRounding.AwayFromZero);
            result.Add(new ArchitectureV7EndpointCorridor("endpoint-corridor:" + terminal.PhysicalLinkId + ":" + terminal.EndpointKind,
                terminal.PhysicalLinkId, terminal.PhysicalNodeId, terminal.EndpointKind, index, horizontal.Cells[0].Row,
                vertical.RunId, horizontal.RunId, terminal.SignedSlotOrdinal, x,
                source ? node.Bounds.Bottom : node.Bounds.Top,
                rows[horizontal.Cells[0].Row].LaneCoordinates[horizontalLane.LaneOrdinal],
                columns[vertical.Cells[0].Column].LaneCoordinates[verticalLane.LaneOrdinal],
                node.Bounds.Left, node.Bounds.Right, "node-span;signed-terminal-slot;collective-horizontal-resource"));
        }
        foreach (var group in result.GroupBy(c => (c.PhysicalLinkId, c.RouteIndex)).Where(g => g.Count() == 2).ToArray())
        {
            var pair = group.ToArray();
            if (pair[0].X != pair[1].X || pair.Any(c => c.X != c.OrdinaryX)) continue;
            // Both resources meet in the same routing cell with no horizontal
            // movement. There is one straight shared coordinate, not two turns.
            var y = (rows[pair[0].RoutingRow].Start + rows[pair[0].RoutingRow].End) / 2d;
            foreach (var corridor in pair) result[result.IndexOf(corridor)] = corridor with { RoutingY = y };
        }
        return result;
    }

    private static double SharedRunPhysicalX(
        ArchitectureV7SharedVerticalRunConstraint constraint,
        ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns)
    {
        var run = allocation.Runs.FirstOrDefault(item => item.RunId == constraint.RunId);
        var assignment = run is null ? null : allocation.RunAssignments.FirstOrDefault(item => item.RunId == run.RunId);
        if (run is null || assignment is null || (uint)run.Cells[0].Column >= (uint)columns.Count ||
            (uint)assignment.LaneOrdinal >= (uint)columns[run.Cells[0].Column].LaneCoordinates.Count)
            return double.NaN;
        return columns[run.Cells[0].Column].LaneCoordinates[assignment.LaneOrdinal];
    }

    private static bool CanAllocateEndpointLocalZ(
        ArchitectureV7TerminalSlotAssignment terminal,
        ArchitectureV7SharedVerticalRunConstraint constraint,
        ArchitectureV7PhysicalSceneNode node,
        int edgePopulation)
    {
        // Endpoint-local Z is an exceptional displaced-Direct resource. A
        // coordinate mismatch by itself is never sufficient authority.
        if (!terminal.Provenance.Contains("direct-displaced", StringComparison.Ordinal) ||
            terminal.PhysicalLinkId != constraint.PhysicalLinkId || TerminalGroup(terminal) != 1 ||
            terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture && terminal.Direction != ArchitectureV7EndpointDirection.Down ||
            terminal.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival && terminal.Direction != ArchitectureV7EndpointDirection.Up)
            return false;

        // A single shared vertical endpoint already has a legal fixed-X
        // terminal.  The exceptional local Z resource exists only to let a
        // displaced Direct relationship participate in a real multi-terminal
        // fit-first pack.
        if (edgePopulation < 2) return false;

        // The fixed run may be physically displaced from the node midpoint by
        // non-uniform column geometry even when the initial logical packing
        // marker says that the Direct slot was centred.  The actual shared
        // lane/terminal mismatch is the authority here; the capacity check
        // below is what keeps this exceptional local Z resource bounded.
        var available = node.Bounds.Right - node.Bounds.Left;
        return terminal.TerminalCapacityRequirement <= available + 0.001;
    }

    private static double ApproachDepth(
        ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        ArchitectureV7TerminalSlotAssignment terminal)
    {
        var route = routes.Routes.First(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
        var horizontal = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
            ? runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId && x.StartRouteIndex > 0)
                .OrderBy(x => x.StartRouteIndex).FirstOrDefault(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal)
            : runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndRouteIndex < route.Cells.Count - 1)
                .OrderByDescending(x => x.EndRouteIndex).FirstOrDefault(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal);
        var node = nodes.First(x => x.PhysicalNodeId == terminal.PhysicalNodeId);
        if (horizontal is null || (uint)horizontal.Cells[0].Row >= (uint)rows.Count) return double.MaxValue;
        var assignment = assignments.First(x => x.RunId == horizontal.RunId);
        var laneY = rows[horizontal.Cells[0].Row].LaneCoordinates.ElementAtOrDefault(assignment.LaneOrdinal);
        return terminal.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival
            ? Math.Abs(node.Bounds.Top - laneY)
            : Math.Abs(laneY - node.Bounds.Bottom);
    }

    private static bool IsFixedX(
        ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        ArchitectureV7TerminalSlotAssignment terminal)
    {
        var route = routes.Routes.First(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
        var routeRuns = runs.Where(x => x.PhysicalLinkId == terminal.PhysicalLinkId && !x.IsEndpointTransition)
            .OrderBy(x => x.StartRouteIndex).ToArray();
        return routeRuns.Length == 1 && routeRuns[0].Orientation == ArchitectureV7RunOrientation.Vertical
            && routeRuns[0].StartRouteIndex == 0 && routeRuns[0].EndRouteIndex == route.Cells.Count - 1;
    }

    private static int TerminalGroup(ArchitectureV7TerminalSlotAssignment terminal)
    {
        const string marker = ";direction-group=";
        var start = terminal.Provenance.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return 1;
        start += marker.Length;
        var end = terminal.Provenance.IndexOf(';', start);
        var value = end < 0 ? terminal.Provenance.Substring(start) : terminal.Provenance.Substring(start, end - start);
        return int.TryParse(value, out var group) ? group : 1;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind) x, (string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind) y) =>
            x.EndpointKind == y.EndpointKind && string.Equals(x.PhysicalLinkId, y.PhysicalLinkId, StringComparison.Ordinal);
        public int GetHashCode((string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind) obj) =>
            StringComparer.Ordinal.GetHashCode(obj.PhysicalLinkId) * 397 ^ (int)obj.EndpointKind;
    }
}
