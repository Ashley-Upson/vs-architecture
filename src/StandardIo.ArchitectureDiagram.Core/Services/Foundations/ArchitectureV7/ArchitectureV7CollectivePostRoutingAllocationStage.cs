using System;
using System.Collections.Generic;
using System.Globalization;
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
        var terminals = AllocateTerminals(placement, routes, runs, assignments, configuration, diagnostics);
        var approaches = BuildApproaches(routes, runs, assignments, terminals);
        var handoffs = BuildHandoffs(placement, routes, runs, assignments, terminals, configuration, diagnostics);
        var bends = BuildBendsResources(runs, routes, assignments, configuration, diagnostics);
        var crossingResult = BuildCrossingResources(routes, runs, assignments, bends, configuration, diagnostics);
        var crossings = crossingResult.Resources;
        var crossingInteractions = crossingResult.Interactions;
        var fingerprint = Fingerprint(placement.PlacementFingerprint, routes.RouteFingerprint, runs, assignments, terminals, approaches, handoffs, bends, crossings, diagnostics);
        return new ArchitectureV7CollectiveAllocationFreeze(runs, lanes, assignments, terminals, approaches, handoffs, bends, crossings,
            diagnostics, placement.PlacementFingerprint, routes.RouteFingerprint, fingerprint, crossingInteractions, configuration);
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
        IReadOnlyList<ArchitectureV7StraightRun> runs, IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments,
        ArchitectureV7AllocationConfiguration configuration,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var groups = new Dictionary<(string Node, ArchitectureV7EndpointKind Kind, ArchitectureV7EndpointDirection Direction), List<(ArchitectureV7LogicalRoute Route, int Anchor, ArchitectureV7StraightRun? AdjacentRun, double LaneOffset)>>();
        foreach (var route in routes.Routes.Where(x => x.IsComplete && x.Cells.Count >= 2))
        {
            Add(route, ArchitectureV7EndpointKind.SourceDeparture, route.SourcePhysicalNodeId, route.Cells[1], route.Cells[0], true);
            Add(route, ArchitectureV7EndpointKind.DestinationArrival, route.DestinationPhysicalNodeId, route.Cells[route.Cells.Count - 2], route.Cells[route.Cells.Count - 1], false);
        }
        var result = new List<ArchitectureV7TerminalSlotAssignment>();
        foreach (var group in groups.OrderBy(x => x.Key.Node, StringComparer.Ordinal).ThenBy(x => x.Key.Kind).ThenBy(x => DirectionOrder(x.Key.Direction)))
        {
            var entries = group.Value.OrderBy(x => x.AdjacentRun is null ? int.MaxValue : FixedCoordinate(x.AdjacentRun))
                .ThenBy(x => x.LaneOffset).ThenBy(x => x.Anchor).ThenBy(x => x.Route.PhysicalLinkId, StringComparer.Ordinal).ToArray();
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
            var usableHalfExtent = available / 2d - configuration.TerminalInset;
            var desiredOffsets = entries.Select(x => x.AdjacentRun is { } run
                ? TerminalLaneOffset(node, x.Route, group.Key.Kind, run, assignments, runs, configuration)
                : 0).ToArray();
            var laneAligned = desiredOffsets.All(offset => Math.Abs(offset) <= usableHalfExtent + 0.0001)
                && desiredOffsets.Zip(desiredOffsets.Skip(1), (left, right) => right - left)
                    .All(delta => delta >= configuration.TerminalPortSpacing - 0.0001);
            for (var index = 0; index < entries.Length; index++)
            {
                var offset = laneAligned
                    ? desiredOffsets[index]
                    : (index - (entries.Length - 1) / 2.0) * configuration.TerminalPortSpacing;
                result.Add(new(entries[index].Route.PhysicalLinkId, node.PhysicalNodeId, group.Key.Kind, group.Key.Direction, index, offset, capacity,
                    laneAligned ? "terminal-order=adjacent-run-lane;exact-lane-alignment" : "terminal-order=adjacent-run-lane;capacity-constrained"));
            }
        }
        return result;

        void Add(ArchitectureV7LogicalRoute route, ArchitectureV7EndpointKind kind, string nodeId, ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, bool source)
        {
            var direction = Direction(near, endpoint, source);
            var adjacentRun = source
                ? runs.FirstOrDefault(x => x.PhysicalLinkId == route.PhysicalLinkId && x.StartRouteIndex == 0)
                : runs.LastOrDefault(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndRouteIndex == route.Cells.Count - 1);
            var laneOffset = adjacentRun is not null
                ? LaneAxisOffset(adjacentRun, assignments.First(x => x.RunId == adjacentRun.RunId), runs, assignments, configuration.ParallelLaneSpacing)
                : 0;
            var anchor = near.Column;
            var key = (nodeId, kind, direction);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            list.Add((route, anchor, adjacentRun, laneOffset));
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
        ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes, IReadOnlyList<ArchitectureV7StraightRun> runs,
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

            var endpointNode = placement.Nodes.FirstOrDefault(x => x.PhysicalNodeId == terminal.PhysicalNodeId);
            var laneOffset = endpointNode is null
                ? LaneAxisOffset(run, assignment, runs, assignments, configuration.ParallelLaneSpacing)
                : TerminalLaneOffset(endpointNode, route, terminal.EndpointKind, run, assignments, runs, configuration);
            var terminalOffset = terminal.RelativeOffset;
            var relativeOffset = laneOffset - terminalOffset;
            var sideEndpoint = terminal.Direction is ArchitectureV7EndpointDirection.Left or ArchitectureV7EndpointDirection.Right;
            var requiresOrthogonalSideAttachment = run.Orientation == ArchitectureV7RunOrientation.Horizontal && sideEndpoint;
            var endpointCell = source ? route.Cells[0] : route.Cells[route.Cells.Count - 1];
            var multiSpanCentreAttachment = endpointNode is not null && endpointNode.LogicalSpan > 1 &&
                endpointCell.Row == endpointNode.DiagramRow && endpointCell.Column == endpointNode.CentreCell;
            if (Math.Abs(relativeOffset) < 0.0001 && !requiresOrthogonalSideAttachment && !multiSpanCentreAttachment) continue;

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
                Math.Max(Math.Abs(relativeOffset), configuration.ResourceClearance),
                run.Orientation == ArchitectureV7RunOrientation.Vertical
                    ? new ArchitectureV7PhysicalRelativePosition(relativeOffset, 0)
                    : new ArchitectureV7PhysicalRelativePosition(0, relativeOffset)));
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

    private static double TerminalLaneOffset(ArchitectureV7FrozenNodePlacement node, ArchitectureV7LogicalRoute route,
        ArchitectureV7EndpointKind endpointKind, ArchitectureV7StraightRun run,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, IReadOnlyList<ArchitectureV7StraightRun> runs,
        ArchitectureV7AllocationConfiguration configuration)
    {
        var assignment = assignments.First(x => x.RunId == run.RunId);
        var offset = LaneAxisOffset(run, assignment, runs, assignments, configuration.ParallelLaneSpacing);
        var endpoint = endpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Cells[0] : route.Cells[route.Cells.Count - 1];
        if (run.Orientation == ArchitectureV7RunOrientation.Vertical && endpoint.Column != node.CentreCell
            && node.LogicalFootprint.Any(cell => cell.Row == endpoint.Row && cell.Column == endpoint.Column))
            offset += (endpoint.Column - node.CentreCell) * configuration.BaseCellWidth;
        return offset;
    }

    private static IReadOnlyList<ArchitectureV7BendAllocation> BuildBendsResources(
        IReadOnlyList<ArchitectureV7StraightRun> runs, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, ArchitectureV7AllocationConfiguration configuration,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var candidates = new List<(ArchitectureV7LogicalRoute Route, int Index, ArchitectureV7StraightRun Incoming, ArchitectureV7StraightRun Outgoing, ArchitectureV7PhysicalRelativePosition Position)>();
        foreach (var route in routes.Routes.Where(x => x.IsComplete))
        {
            var routeRuns = runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId).OrderBy(x => x.StartRouteIndex).ToArray();
            for (var index = 1; index < route.Cells.Count - 1; index++)
            {
                var incoming = routeRuns.FirstOrDefault(x => x.StartRouteIndex <= index - 1 && x.EndRouteIndex >= index);
                var outgoing = routeRuns.FirstOrDefault(x => x.StartRouteIndex <= index && x.EndRouteIndex >= index + 1);
                if (incoming is null || outgoing is null)
                {
                    diagnostics.Add(new("BEND-RESOURCE-MISSING", "A frozen turn has no complete incoming or outgoing run allocation.", true, route.PhysicalLinkId));
                    continue;
                }
                if (incoming.Orientation == outgoing.Orientation) continue;
                candidates.Add((route, index, incoming, outgoing, BendPosition(incoming, outgoing, assignments, runs, configuration.ParallelLaneSpacing)));
            }
        }

        var result = new List<ArchitectureV7BendAllocation>();
        foreach (var group in candidates.GroupBy(x => x.Route.Cells[x.Index]).OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column))
        {
            var ordered = group.OrderBy(x => x.Incoming.Orientation).ThenBy(x => x.Outgoing.Orientation)
                .ThenBy(x => LaneOrdinal(x.Incoming, assignments)).ThenBy(x => LaneOrdinal(x.Outgoing, assignments))
                .ThenBy(x => x.Route.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.Index).ToArray();
            for (var slot = 0; slot < ordered.Length; slot++)
            {
                var item = ordered[slot];
                // A bend is the intersection of its frozen incoming/outgoing
                // lanes.  Resource-slot separation must not move that point
                // off either authoritative lane.
                var position = item.Position;
                var incomingAssignment = assignments.First(x => x.RunId == item.Incoming.RunId);
                var outgoingAssignment = assignments.First(x => x.RunId == item.Outgoing.RunId);
                result.Add(new("bend:" + item.Route.PhysicalLinkId + ":" + item.Index, item.Route.PhysicalLinkId, item.Index,
                    item.Route.Cells[item.Index], item.Incoming.Orientation, item.Outgoing.Orientation, item.Incoming.RunId, item.Outgoing.RunId,
                    "allocated-bend-resource;cell-centre-relative-offset", incomingAssignment.LaneId, outgoingAssignment.LaneId,
                    0, configuration.ResourceClearance, position));
            }
        }
        return result;
    }

    private static CrossingBuildResult BuildCrossingResources(
        ArchitectureV7LogicalRouteFreeze routes, IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, IReadOnlyList<ArchitectureV7BendAllocation> bends,
        ArchitectureV7AllocationConfiguration configuration, ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var cells = routes.Routes.Where(x => x.IsComplete).SelectMany(route => route.Cells.Select((cell, index) => (route, cell, index)))
            .GroupBy(x => x.cell).OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column);
        var result = new List<ArchitectureV7CrossingAllocation>();
        var interactions = new List<ArchitectureV7CrossingInteraction>();
        foreach (var cell in cells)
        {
            var entries = cell.ToArray();
            var horizontalPasses = entries.Where(x => IsPass(x.route.Cells, x.index, ArchitectureV7RunOrientation.Horizontal)).ToArray();
            var verticalPasses = entries.Where(x => IsPass(x.route.Cells, x.index, ArchitectureV7RunOrientation.Vertical)).ToArray();
            var turns = entries.Where(x => IsTurn(x.route.Cells, x.index)).ToArray();
            var candidates = new List<CrossingCandidateValue>();
            foreach (var h in horizontalPasses)
                foreach (var v in verticalPasses)
                    if (!string.Equals(h.route.PhysicalLinkId, v.route.PhysicalLinkId, StringComparison.Ordinal))
                        candidates.Add(CreateCrossingCandidate(h.route, h.index, v.route, v.index, runs, assignments, configuration, "clean-crossing"));
            foreach (var turn in turns)
            {
                foreach (var h in horizontalPasses.Where(x => x.route.PhysicalLinkId != turn.route.PhysicalLinkId))
                    candidates.Add(CreateCrossingCandidate(h.route, h.index, turn.route, turn.index, runs, assignments, configuration, "turn-pass"));
                foreach (var v in verticalPasses.Where(x => x.route.PhysicalLinkId != turn.route.PhysicalLinkId))
                    candidates.Add(CreateCrossingCandidate(turn.route, turn.index, v.route, v.index, runs, assignments, configuration, "turn-pass"));
            }
            if (candidates.Count == 0) continue;
            var ordered = candidates.OrderBy(x => x.HLaneId, StringComparer.Ordinal).ThenBy(x => x.VLaneId, StringComparer.Ordinal)
                .ThenBy(x => x.HLink, StringComparer.Ordinal).ThenBy(x => x.VLink, StringComparer.Ordinal).ToArray();
            var resources = ordered.GroupBy(x => new CrossingResourceGeometryKey(cell.Key, x.Classification, x.HLaneId, x.VLaneId,
                    x.Position.XOffset, x.Position.YOffset, configuration.ResourceClearance))
                .OrderBy(x => x.Key.Classification, StringComparer.Ordinal)
                .ThenBy(x => x.Key.HorizontalLaneId, StringComparer.Ordinal)
                .ThenBy(x => x.Key.VerticalLaneId, StringComparer.Ordinal)
                .ThenBy(x => x.Key.XOffset)
                .ThenBy(x => x.Key.YOffset)
                .ToArray();
            for (var slot = 0; slot < resources.Length; slot++)
            {
                var group = resources[slot];
                var item = group.First();
                // A crossing describes interaction between the already
                // allocated horizontal and vertical lanes.  It must remain at
                // their exact frozen intersection; it is not a bend-slot
                // placement opportunity.
                var position = new ArchitectureV7PhysicalRelativePosition(group.Key.XOffset, group.Key.YOffset);
                var resourceId = CrossingResourceId(group.Key.Cell, group.Key.Classification, group.Key.HorizontalLaneId,
                    group.Key.VerticalLaneId, position, configuration.ResourceClearance);
                result.Add(new(resourceId, cell.Key, item.HLink, item.VLink, "allocated-crossing-resource;geometry-keyed", item.HRunId, item.VRunId,
                    item.HLaneId, item.VLaneId, 0, configuration.ResourceClearance, item.Classification,
                    item.HIndex, item.VIndex, position, group.Select(x => x.InteractionId).OrderBy(x => x, StringComparer.Ordinal).ToArray()));
                foreach (var candidate in group)
                {
                    var bendId = BendFor(candidate, bends);
                    var bend = string.IsNullOrEmpty(bendId) ? null : bends.FirstOrDefault(value => value.BendId == bendId);
                    if (bend is not null && SamePhysicalPoint(bend.EffectiveRelativePosition, candidate.Position))
                        diagnostics.Add(new("CROSSING-TURN-PHYSICAL-CONFLICT", "A turn/pass interaction occupies the bend's allocated physical point.", true,
                            candidate.HLink, candidate.HRunId, null, null, null, null, null, new[] { candidate.HLink, candidate.VLink }, new[] { candidate.HRunId, candidate.VRunId }));
                    interactions.Add(new(candidate.InteractionId, cell.Key, candidate.Classification, candidate.HLink, candidate.VLink,
                        candidate.HRunId, candidate.VRunId, candidate.HLaneId, candidate.VLaneId, candidate.HIndex, candidate.VIndex,
                        "crossing-interaction;cell-and-lane-geometry", resourceId, bendId));
                }
            }
        }
        return new(result, interactions);
    }

    private sealed record CrossingCandidateValue(string HLink, string VLink, int HIndex, int VIndex, string HRunId, string VRunId,
        string HLaneId, string VLaneId, ArchitectureV7PhysicalRelativePosition Position, string Classification, string InteractionId);

    private sealed record CrossingResourceGeometryKey(ArchitectureV7RouteCell Cell, string Classification, string HorizontalLaneId,
        string VerticalLaneId, double XOffset, double YOffset, double Clearance);

    private sealed record CrossingBuildResult(IReadOnlyList<ArchitectureV7CrossingAllocation> Resources,
        IReadOnlyList<ArchitectureV7CrossingInteraction> Interactions);

    private static CrossingCandidateValue CreateCrossingCandidate(ArchitectureV7LogicalRoute hRoute, int hIndex,
        ArchitectureV7LogicalRoute vRoute, int vIndex, IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, ArchitectureV7AllocationConfiguration configuration, string classification)
    {
        var hRun = RunAt(hRoute.PhysicalLinkId, hIndex, ArchitectureV7RunOrientation.Horizontal, runs);
        var vRun = RunAt(vRoute.PhysicalLinkId, vIndex, ArchitectureV7RunOrientation.Vertical, runs);
        if (hRun is null || vRun is null) throw new InvalidOperationException("A crossing candidate requires both perpendicular run allocations.");
        var hAssignment = assignments.First(x => x.RunId == hRun.RunId);
        var vAssignment = assignments.First(x => x.RunId == vRun.RunId);
        var position = new ArchitectureV7PhysicalRelativePosition(LaneAxisOffset(vRun, vAssignment, runs, assignments, configuration.ParallelLaneSpacing),
            LaneAxisOffset(hRun, hAssignment, runs, assignments, configuration.ParallelLaneSpacing));
        var interactionId = "crossing-interaction:" + hRoute.PhysicalLinkId + ":" + hIndex + ":" + vRoute.PhysicalLinkId + ":" + vIndex;
        return new(hRoute.PhysicalLinkId, vRoute.PhysicalLinkId, hIndex, vIndex, hRun.RunId, vRun.RunId, hAssignment.LaneId, vAssignment.LaneId,
            position, classification, interactionId);
    }

    private static string CrossingResourceId(ArchitectureV7RouteCell cell, string classification, string horizontalLaneId,
        string verticalLaneId, ArchitectureV7PhysicalRelativePosition position, double clearance) =>
        "crossing-resource:" + cell.Row + ":" + cell.Column + ":" + classification + ":" + horizontalLaneId + ":" + verticalLaneId
        + ":" + Number(position.XOffset) + ":" + Number(position.YOffset) + ":" + Number(clearance);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static bool SamePhysicalPoint(ArchitectureV7PhysicalRelativePosition left, ArchitectureV7PhysicalRelativePosition right) =>
        Math.Abs(left.XOffset - right.XOffset) < 0.0001 && Math.Abs(left.YOffset - right.YOffset) < 0.0001;

    private static string BendFor(CrossingCandidateValue candidate, IReadOnlyList<ArchitectureV7BendAllocation> bends)
    {
        if (!string.Equals(candidate.Classification, "turn-pass", StringComparison.Ordinal)) return "";
        return bends.FirstOrDefault(bend =>
            (bend.PhysicalLinkId == candidate.HLink && bend.RouteIndex == candidate.HIndex) ||
            (bend.PhysicalLinkId == candidate.VLink && bend.RouteIndex == candidate.VIndex))?.BendId ?? "";
    }

    private static ArchitectureV7StraightRun? RunAt(string physicalLinkId, int index, ArchitectureV7RunOrientation orientation, IReadOnlyList<ArchitectureV7StraightRun> runs) =>
        runs.FirstOrDefault(x => x.PhysicalLinkId == physicalLinkId && x.Orientation == orientation && x.StartRouteIndex <= index && x.EndRouteIndex >= index);

    private static ArchitectureV7PhysicalRelativePosition BendPosition(ArchitectureV7StraightRun incoming, ArchitectureV7StraightRun outgoing,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, IReadOnlyList<ArchitectureV7StraightRun> runs, int spacing)
    {
        var horizontal = incoming.Orientation == ArchitectureV7RunOrientation.Horizontal ? incoming : outgoing;
        var vertical = incoming.Orientation == ArchitectureV7RunOrientation.Vertical ? incoming : outgoing;
        return new(LaneAxisOffset(vertical, assignments.First(x => x.RunId == vertical.RunId), runs, assignments, spacing),
            LaneAxisOffset(horizontal, assignments.First(x => x.RunId == horizontal.RunId), runs, assignments, spacing));
    }

    private static double SlotOffset(int slot, int count, int spacing) => (slot - (count - 1) / 2d) * spacing;
    private static int LaneOrdinal(ArchitectureV7StraightRun run, IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments) => assignments.First(x => x.RunId == run.RunId).LaneOrdinal;

    private static string Fingerprint(string placementFingerprint, string routeFingerprint, IEnumerable<ArchitectureV7StraightRun> runs, IEnumerable<ArchitectureV7RunLaneAssignment> assignments,
        IEnumerable<ArchitectureV7TerminalSlotAssignment> terminals, IEnumerable<ArchitectureV7EndpointApproachReservation> approaches,
        IEnumerable<ArchitectureV7EndpointHandoff> handoffs, IEnumerable<ArchitectureV7BendAllocation> bends, IEnumerable<ArchitectureV7CrossingAllocation> crossings,
        IEnumerable<ArchitectureV7AllocationDiagnostic> diagnostics) => placementFingerprint + "|" + routeFingerprint + "|" + string.Join(";", runs.Select(x => x.RunId + ":" + string.Join(",", x.Cells.Select(c => c.Row + "/" + c.Column))).Concat(assignments.Select(x => x.RunId + "=" + x.LaneId)).Concat(terminals.Select(x => x.PhysicalLinkId + ":" + x.EndpointKind + ":" + x.SlotOrdinal)).Concat(approaches.Select(x => x.PhysicalLinkId + ":a" + x.TerminalSlotOrdinal)).Concat(handoffs.Select(x => x.ResourceId + ":h" + x.RelativePhysicalOffset + ":" + x.RequiredClearance)).Concat(bends.Select(x => x.BendId + ":b" + x.EffectiveRelativePosition.XOffset + "/" + x.EffectiveRelativePosition.YOffset)).Concat(crossings.Select(x => x.CrossingId + ":c" + x.EffectiveRelativePosition.XOffset + "/" + x.EffectiveRelativePosition.YOffset + ":" + x.Classification)).Concat(diagnostics.Select(x => x.Code)));

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
