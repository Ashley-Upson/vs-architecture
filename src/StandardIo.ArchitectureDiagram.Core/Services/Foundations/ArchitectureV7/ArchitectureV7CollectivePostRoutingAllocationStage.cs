using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>Allocates physical routing resources without changing frozen logical topology.</summary>
public sealed class ArchitectureV7CollectivePostRoutingAllocationStage
{
    public ArchitectureV7CollectiveAllocationFreeze Allocate(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7AllocationConfiguration configuration)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (routes is null) throw new ArgumentNullException(nameof(routes));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (!string.Equals(routes.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The logical route freeze does not belong to the supplied placement freeze.", nameof(routes));
        if (configuration.ParallelLaneSpacing < 0 || configuration.TerminalPortSpacing < 0 || configuration.TerminalInset < 0 || configuration.BaseCellWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(configuration));

        var diagnostics = new List<ArchitectureV7AllocationDiagnostic>();
        var runs = BuildRuns(routes, diagnostics);
        var (lanes, assignments) = AllocateLanes(runs, configuration.ParallelLaneSpacing);
        var terminals = AllocateTerminals(placement, routes, runs, configuration, diagnostics);
        var approaches = BuildApproaches(routes, runs, assignments, terminals);
        var handoffs = BuildHandoffs(routes, runs, assignments, terminals, configuration, diagnostics);
        var bends = BuildBends(runs, routes, diagnostics);
        var crossings = BuildCrossings(routes, diagnostics);
        var fingerprint = Fingerprint(placement.PlacementFingerprint, routes.RouteFingerprint, runs, assignments, terminals, approaches, handoffs, bends, crossings, diagnostics);
        return new ArchitectureV7CollectiveAllocationFreeze(runs, lanes, assignments, terminals, approaches, handoffs, bends, crossings,
            diagnostics, placement.PlacementFingerprint, routes.RouteFingerprint, fingerprint);
    }

    private static IReadOnlyList<ArchitectureV7StraightRun> BuildRuns(
        ArchitectureV7LogicalRouteFreeze routes,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7StraightRun>();
        foreach (var route in routes.Routes.OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal))
        {
            if (!route.IsComplete || route.Cells.Count < 2)
            {
                diagnostics.Add(new("INCOMPLETE-ROUTE", "A physical allocation requires a complete frozen logical route.", true, route.PhysicalLinkId));
                continue;
            }
            var start = 0;
            while (start < route.Cells.Count - 1)
            {
                var orientation = Orientation(route.Cells[start], route.Cells[start + 1]);
                var end = start + 1;
                while (end + 1 < route.Cells.Count && Orientation(route.Cells[end], route.Cells[end + 1]) == orientation) end++;
                var cells = route.Cells.Skip(start).Take(end - start + 1).ToArray();
                var context = (start == 0 ? "source" : "internal") + ":" + (end == route.Cells.Count - 1 ? "destination" : "bend");
                result.Add(new ArchitectureV7StraightRun("run:" + route.PhysicalLinkId + ":" + start,
                    route.PhysicalLinkId, orientation, cells, start, end, context, route.Provenance));
                start = end;
            }
        }
        return result.OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray();
    }

    private static (IReadOnlyList<ArchitectureV7PhysicalLane> Lanes, IReadOnlyList<ArchitectureV7RunLaneAssignment> Assignments)
        AllocateLanes(IReadOnlyList<ArchitectureV7StraightRun> runs, int spacing)
    {
        var assignments = new List<ArchitectureV7RunLaneAssignment>();
        var lanes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var used = new Dictionary<string, List<(int Start, int End, int Ordinal, string Link)>>(StringComparer.Ordinal);
        foreach (var run in runs.OrderBy(x => x.Orientation).ThenBy(x => FixedCoordinate(x)).ThenBy(x => MinCoordinate(x)).ThenBy(x => x.RunId, StringComparer.Ordinal))
        {
            var domain = run.Orientation + ":" + FixedCoordinate(run);
            if (!used.TryGetValue(domain, out var intervals)) used[domain] = intervals = new();
            var ordinal = 0;
            while (intervals.Any(interval => interval.Ordinal == ordinal && interval.Link != run.PhysicalLinkId
                && interval.Start <= MaxCoordinate(run) && interval.End >= MinCoordinate(run))) ordinal++;
            var laneId = "lane:" + (run.Orientation == ArchitectureV7RunOrientation.Horizontal ? "H" : "V") + ":" + FixedCoordinate(run) + ":" + ordinal;
            assignments.Add(new(run.RunId, laneId, ordinal));
            intervals.Add((MinCoordinate(run), MaxCoordinate(run), ordinal, run.PhysicalLinkId));
            if (!lanes.TryGetValue(laneId, out var laneRuns)) lanes[laneId] = laneRuns = new();
            laneRuns.Add(run.RunId);
        }
        var laneModels = lanes.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ArchitectureV7PhysicalLane(x.Key, x.Key.Contains(":H:", StringComparison.Ordinal) ? ArchitectureV7RunOrientation.Horizontal : ArchitectureV7RunOrientation.Vertical,
                ParseOrdinal(x.Key), spacing, x.Value.OrderBy(v => v, StringComparer.Ordinal).ToArray())).ToArray();
        return (laneModels, assignments);

    }

    private static IReadOnlyList<ArchitectureV7TerminalSlotAssignment> AllocateTerminals(
        ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs, ArchitectureV7AllocationConfiguration configuration,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var groups = new Dictionary<(string Node, ArchitectureV7EndpointKind Kind, ArchitectureV7EndpointDirection Direction), List<(ArchitectureV7LogicalRoute Route, int Anchor)>>();
        foreach (var route in routes.Routes.Where(x => x.IsComplete && x.Cells.Count >= 2))
        {
            Add(route, ArchitectureV7EndpointKind.SourceDeparture, route.SourcePhysicalNodeId, route.Cells[1], route.Cells[0], true);
            Add(route, ArchitectureV7EndpointKind.DestinationArrival, route.DestinationPhysicalNodeId, route.Cells[route.Cells.Count - 2], route.Cells[route.Cells.Count - 1], false);
        }
        var result = new List<ArchitectureV7TerminalSlotAssignment>();
        foreach (var group in groups.OrderBy(x => x.Key.Node, StringComparer.Ordinal).ThenBy(x => x.Key.Kind).ThenBy(x => DirectionOrder(x.Key.Direction)))
        {
            var entries = group.Value.OrderBy(x => x.Anchor).ThenBy(x => x.Route.PhysicalLinkId, StringComparer.Ordinal).ToArray();
            var node = placement.Nodes.FirstOrDefault(x => x.PhysicalNodeId == group.Key.Node);
            if (node is null)
            {
                diagnostics.Add(new("UNKNOWN-ENDPOINT", "A route endpoint is absent from the placement freeze.", true, entries[0].Route.PhysicalLinkId));
                continue;
            }
            var capacity = entries.Length == 0 ? 0 : 2 * configuration.TerminalInset + Math.Max(0, entries.Length - 1) * configuration.TerminalPortSpacing;
            var available = node.LogicalSpan * configuration.BaseCellWidth;
            if (capacity > available)
                diagnostics.Add(new("TERMINAL-OVERFLOW", "Terminal demand exceeds the frozen physical span; node expansion is forbidden.", true,
                    entries[0].Route.PhysicalLinkId, null, node.PhysicalNodeId, group.Key.Kind.ToString(), capacity, available, node.LogicalSpan,
                    entries.Select(x => x.Route.PhysicalLinkId).Distinct(StringComparer.Ordinal).ToArray(), null));
            for (var index = 0; index < entries.Length; index++)
            {
                var offset = (index - (entries.Length - 1) / 2.0) * configuration.TerminalPortSpacing;
                result.Add(new(entries[index].Route.PhysicalLinkId, node.PhysicalNodeId, group.Key.Kind, group.Key.Direction, index, offset, capacity, "terminal-order=direction,position,relationship"));
            }
        }
        return result;

        void Add(ArchitectureV7LogicalRoute route, ArchitectureV7EndpointKind kind, string nodeId, ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, bool source)
        {
            var direction = Direction(near, endpoint, source);
            var anchor = source ? near.Column : near.Column;
            var key = (nodeId, kind, direction);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            list.Add((route, anchor));
        }
    }

    private static IReadOnlyList<ArchitectureV7EndpointApproachReservation> BuildApproaches(
        ArchitectureV7LogicalRouteFreeze routes, IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals)
    {
        var result = new List<ArchitectureV7EndpointApproachReservation>();
        foreach (var terminal in terminals)
        {
            var route = routes.Routes.First(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
            var run = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? runs.First(x => x.PhysicalLinkId == route.PhysicalLinkId && x.StartRouteIndex == 0)
                : runs.Last(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndRouteIndex == route.Cells.Count - 1);
            var assignment = assignments.First(x => x.RunId == run.RunId);
            var cells = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Cells.Take(2).ToArray() : route.Cells.Skip(Math.Max(0, route.Cells.Count - 2)).ToArray();
            result.Add(new(terminal.PhysicalLinkId, terminal.PhysicalNodeId, terminal.EndpointKind, terminal.SlotOrdinal, assignment.LaneOrdinal,
                run.Orientation == ArchitectureV7RunOrientation.Vertical ? assignment.LaneOrdinal : -1,
                run.Orientation == ArchitectureV7RunOrientation.Horizontal ? assignment.LaneOrdinal : -1, cells,
                "endpoint-local;source=bottom,destination=top"));
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7EndpointHandoff> BuildHandoffs(
        ArchitectureV7LogicalRouteFreeze routes, IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        ArchitectureV7AllocationConfiguration configuration, ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7EndpointHandoff>();
        foreach (var terminal in terminals)
        {
            var route = routes.Routes.First(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
            if (route.Cells.Count < 2)
            {
                diagnostics.Add(new("HANDOFF-CAPACITY-UNREPRESENTABLE", "An endpoint handoff has no authoritative adjacent logical cell.", true,
                    route.PhysicalLinkId, null, terminal.PhysicalNodeId, terminal.EndpointKind.ToString()));
                continue;
            }

            var source = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture;
            var run = source
                ? runs.FirstOrDefault(x => x.PhysicalLinkId == route.PhysicalLinkId && x.StartRouteIndex == 0)
                : runs.LastOrDefault(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndRouteIndex == route.Cells.Count - 1);
            if (run is null)
            {
                diagnostics.Add(new("HANDOFF-CAPACITY-UNREPRESENTABLE", "An endpoint handoff has no adjacent maximal run allocation.", true,
                    route.PhysicalLinkId, null, terminal.PhysicalNodeId, terminal.EndpointKind.ToString()));
                continue;
            }

            var assignment = assignments.FirstOrDefault(x => x.RunId == run.RunId);
            if (assignment is null)
            {
                diagnostics.Add(new("HANDOFF-CAPACITY-UNREPRESENTABLE", "An endpoint handoff has no adjacent lane allocation.", true,
                    route.PhysicalLinkId, run.RunId, terminal.PhysicalNodeId, terminal.EndpointKind.ToString()));
                continue;
            }

            var laneOffset = LaneAxisOffset(run, assignment, runs, assignments, configuration.ParallelLaneSpacing);
            var terminalOffset = terminal.RelativeOffset;
            var relativeOffset = laneOffset - terminalOffset;
            var sideEndpoint = terminal.Direction is ArchitectureV7EndpointDirection.Left or ArchitectureV7EndpointDirection.Right;
            var requiresOrthogonalSideAttachment = run.Orientation == ArchitectureV7RunOrientation.Horizontal && sideEndpoint;
            if (Math.Abs(relativeOffset) < 0.0001 && !requiresOrthogonalSideAttachment) continue;

            var startIndex = source ? 0 : route.Cells.Count - 2;
            var endIndex = source ? 1 : route.Cells.Count - 1;
            var cells = source ? route.Cells.Take(2).ToArray() : route.Cells.Skip(route.Cells.Count - 2).ToArray();
            result.Add(new(
                terminal.PhysicalLinkId,
                terminal.PhysicalNodeId,
                terminal.EndpointKind,
                terminal.SlotOrdinal,
                cells,
                "terminal-slot/lane-offset mismatch;allocated orthogonal endpoint handoff",
                "explicit-orthogonal-handoff;frozen-route-index=" + startIndex + ":" + endIndex,
                $"handoff:{terminal.PhysicalLinkId}:{terminal.EndpointKind}:{terminal.SlotOrdinal}",
                source ? route.Cells[1] : route.Cells[route.Cells.Count - 2],
                startIndex,
                endIndex,
                run.RunId,
                assignment.LaneId,
                run.Orientation == ArchitectureV7RunOrientation.Vertical ? ArchitectureV7RunOrientation.Horizontal : ArchitectureV7RunOrientation.Vertical,
                terminal.Direction,
                terminalOffset,
                laneOffset,
                relativeOffset,
                Math.Abs(relativeOffset)));
        }
        return result;
    }

    private static double LaneAxisOffset(ArchitectureV7StraightRun run, ArchitectureV7RunLaneAssignment assignment,
        IReadOnlyList<ArchitectureV7StraightRun> runs, IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, int spacing)
    {
        var domain = runs.Where(x => x.Orientation == run.Orientation && FixedCoordinate(x) == FixedCoordinate(run))
            .Select(x => assignments.First(a => a.RunId == x.RunId).LaneOrdinal)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        var ordinalIndex = Array.IndexOf(domain, assignment.LaneOrdinal);
        return (ordinalIndex - (domain.Length - 1) / 2d) * spacing;
    }

    private static IReadOnlyList<ArchitectureV7BendAllocation> BuildBends(
        IReadOnlyList<ArchitectureV7StraightRun> runs, ArchitectureV7LogicalRouteFreeze routes,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7BendAllocation>();
        foreach (var route in routes.Routes.Where(x => x.IsComplete))
        {
            var routeRuns = runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId).OrderBy(x => x.StartRouteIndex).ToArray();
            for (var index = 1; index < route.Cells.Count - 1; index++)
            {
                var incoming = routeRuns.First(x => x.StartRouteIndex <= index - 1 && x.EndRouteIndex >= index);
                var outgoing = routeRuns.First(x => x.StartRouteIndex <= index && x.EndRouteIndex >= index + 1);
                if (incoming.Orientation == outgoing.Orientation) continue;
                result.Add(new("bend:" + route.PhysicalLinkId + ":" + index, route.PhysicalLinkId, index, route.Cells[index], incoming.Orientation, outgoing.Orientation, incoming.RunId, outgoing.RunId, "transition-between-frozen-runs"));
            }
        }
        foreach (var cell in result.GroupBy(x => x.Cell).Where(x => x.Select(y => y.PhysicalLinkId).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            var bends = cell.ToArray();
            diagnostics.Add(new("SHARED-BEND-CONFLICT", "Unrelated relationships require a shared bend cell; rerouting is forbidden.", true,
                bends[0].PhysicalLinkId, bends[0].IncomingRunId, null, null, null, null, null,
                bends.Select(x => x.PhysicalLinkId).Distinct(StringComparer.Ordinal).ToArray(), bends.SelectMany(x => new[] { x.IncomingRunId, x.OutgoingRunId }).Distinct(StringComparer.Ordinal).ToArray()));
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7CrossingAllocation> BuildCrossings(
        ArchitectureV7LogicalRouteFreeze routes, ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var orientations = routes.Routes.Where(x => x.IsComplete).SelectMany(route => route.Cells.Select((cell, index) => (route, cell, index)))
            .GroupBy(x => x.cell).ToDictionary(x => x.Key, x => x.ToArray());
        var result = new List<ArchitectureV7CrossingAllocation>();
        foreach (var cell in orientations.OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column))
        {
            var horizontal = cell.Value.Where(x => IsPass(x.route.Cells, x.index, ArchitectureV7RunOrientation.Horizontal)).Select(x => x.route.PhysicalLinkId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var vertical = cell.Value.Where(x => IsPass(x.route.Cells, x.index, ArchitectureV7RunOrientation.Vertical)).Select(x => x.route.PhysicalLinkId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (cell.Value.Any(x => IsTurn(x.route.Cells, x.index)) && horizontal.Length > 0 && vertical.Length > 0)
            {
                var participants = cell.Value.Select(x => x.route.PhysicalLinkId).Distinct(StringComparer.Ordinal).ToArray();
                diagnostics.Add(new("CROSSING-TURN-CONFLICT", "A route turns at a perpendicular crossing cell; clean crossing is impossible.", true,
                    participants[0], null, null, null, null, null, null, participants, null));
            }
            foreach (var h in horizontal)
                foreach (var v in vertical)
                    if (!string.Equals(h, v, StringComparison.Ordinal)) result.Add(new("crossing:" + cell.Key.Row + ":" + cell.Key.Column + ":" + h + ":" + v, cell.Key, h, v, "clean-perpendicular-crossing"));
        }
        return result;
    }

    private static string Fingerprint(string placementFingerprint, string routeFingerprint, IEnumerable<ArchitectureV7StraightRun> runs, IEnumerable<ArchitectureV7RunLaneAssignment> assignments,
        IEnumerable<ArchitectureV7TerminalSlotAssignment> terminals, IEnumerable<ArchitectureV7EndpointApproachReservation> approaches,
        IEnumerable<ArchitectureV7EndpointHandoff> handoffs, IEnumerable<ArchitectureV7BendAllocation> bends, IEnumerable<ArchitectureV7CrossingAllocation> crossings,
        IEnumerable<ArchitectureV7AllocationDiagnostic> diagnostics) => placementFingerprint + "|" + routeFingerprint + "|" + string.Join(";", runs.Select(x => x.RunId + ":" + string.Join(",", x.Cells.Select(c => c.Row + "/" + c.Column))).Concat(assignments.Select(x => x.RunId + "=" + x.LaneId)).Concat(terminals.Select(x => x.PhysicalLinkId + ":" + x.EndpointKind + ":" + x.SlotOrdinal)).Concat(approaches.Select(x => x.PhysicalLinkId + ":a" + x.TerminalSlotOrdinal)).Concat(handoffs.Select(x => x.PhysicalLinkId + ":h" + x.TerminalSlotOrdinal)).Concat(bends.Select(x => x.BendId)).Concat(crossings.Select(x => x.CrossingId)).Concat(diagnostics.Select(x => x.Code)));

    private static ArchitectureV7RunOrientation Orientation(ArchitectureV7RouteCell a, ArchitectureV7RouteCell b) => a.Row == b.Row ? ArchitectureV7RunOrientation.Horizontal : ArchitectureV7RunOrientation.Vertical;
    private static int FixedCoordinate(ArchitectureV7StraightRun run) => run.Orientation == ArchitectureV7RunOrientation.Horizontal ? run.Cells[0].Row : run.Cells[0].Column;
    private static int MinCoordinate(ArchitectureV7StraightRun run) => run.Cells.Min(x => run.Orientation == ArchitectureV7RunOrientation.Horizontal ? x.Column : x.Row);
    private static int MaxCoordinate(ArchitectureV7StraightRun run) => run.Cells.Max(x => run.Orientation == ArchitectureV7RunOrientation.Horizontal ? x.Column : x.Row);
    private static int ParseOrdinal(string laneId) => int.Parse(laneId.Substring(laneId.LastIndexOf(':') + 1));
    private static int DirectionOrder(ArchitectureV7EndpointDirection direction) => direction switch { ArchitectureV7EndpointDirection.Left => 0, ArchitectureV7EndpointDirection.Down => 1, ArchitectureV7EndpointDirection.Right => 2, _ => 3 };
    private static ArchitectureV7EndpointDirection Direction(ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, bool source)
    {
        if (near.Column < endpoint.Column) return source ? ArchitectureV7EndpointDirection.Right : ArchitectureV7EndpointDirection.Left;
        if (near.Column > endpoint.Column) return source ? ArchitectureV7EndpointDirection.Left : ArchitectureV7EndpointDirection.Right;
        return source ? ArchitectureV7EndpointDirection.Down : ArchitectureV7EndpointDirection.Up;
    }
    private static bool IsPass(IReadOnlyList<ArchitectureV7RouteCell> cells, int index, ArchitectureV7RunOrientation orientation)
    {
        if (index == 0 || index == cells.Count - 1) return false;
        var incoming = Orientation(cells[index - 1], cells[index]);
        var outgoing = Orientation(cells[index], cells[index + 1]);
        return incoming == orientation && outgoing == orientation;
    }
    private static bool IsTurn(IReadOnlyList<ArchitectureV7RouteCell> cells, int index) => index > 0 && index + 1 < cells.Count && Orientation(cells[index - 1], cells[index]) != Orientation(cells[index], cells[index + 1]);
}
