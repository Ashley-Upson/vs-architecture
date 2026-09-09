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
                    Provenance = coordinate.Provenance + ";endpoint-geometry=actual-bend-depth"
                }
                : coordinate;
        }).ToArray();

        // A maximal vertical relationship owns one physical incoming X.  The
        // endpoint slot sequence is packed against the actual node bounds, so
        // a source and destination node may legitimately have different
        // midpoint coordinates even though the relationship's run X is fixed.
        // Allocate the local Z resource here, where both authorities are
        // available; compilation only materialises it.
        var endpointZBends = allocation.EndpointZBends.ToList();
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

        // Long ordinary endpoint runs keep the allocated vertical lane for
        // their full route. If the packed terminal is at another X, allocate
        // the local orthogonal boundary Z here rather than allowing physical
        // compilation to move the whole run onto the terminal coordinate.
        foreach (var coordinate in endpointCoordinates)
        {
            if (allocation.SharedVerticalRunConstraints.Any(item => item.PhysicalLinkId == coordinate.PhysicalLinkId))
                continue;
            var terminal = terminals.FirstOrDefault(item => item.PhysicalLinkId == coordinate.PhysicalLinkId && item.EndpointKind == coordinate.EndpointKind);
            var route = routes.Routes.FirstOrDefault(item => item.PhysicalLinkId == coordinate.PhysicalLinkId);
            var run = route is null ? null : runs.FirstOrDefault(item => item.RunId == coordinate.RunId &&
                (coordinate.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? item.StartRouteIndex == 0 : item.EndRouteIndex == route.Cells.Count - 1));
            var assignment = run is null ? null : assignments.FirstOrDefault(item => item.RunId == run.RunId);
            var node = terminal is null ? null : nodes.FirstOrDefault(item => item.PhysicalNodeId == terminal.PhysicalNodeId);
            if (terminal is null || run is null || assignment is null || node is null || run.Cells.Count <= 2 ||
                (uint)run.Cells[0].Column >= (uint)columns.Count ||
                (uint)assignment.LaneOrdinal >= (uint)columns[run.Cells[0].Column].LaneCoordinates.Count)
                continue;
            var runX = columns[run.Cells[0].Column].LaneCoordinates[assignment.LaneOrdinal];
            var terminalX = (node.Bounds.Left + node.Bounds.Right) / 2d + terminal.RelativeOffset;
            if (Math.Abs(runX - terminalX) <= 0.001 || endpointZBends.Any(item => item.PhysicalLinkId == terminal.PhysicalLinkId && item.EndpointKind == terminal.EndpointKind))
                continue;
            endpointZBends.Add(new ArchitectureV7EndpointZBend(terminal.PhysicalLinkId, terminal.PhysicalNodeId,
                terminal.EndpointKind, run.RunId,
                "endpoint-z-bend;fixed-incoming-lane-x;long-endpoint-run;terminal-anchored-boundary-transition"));
        }

        return allocation.WithEndpointGeometry(terminals, approaches, endpointCoordinates, endpointZBends);
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
        if (terminal.PhysicalLinkId != constraint.PhysicalLinkId || TerminalGroup(terminal) != 1 ||
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
        var routeRuns = runs.Where(x => x.PhysicalLinkId == terminal.PhysicalLinkId)
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
