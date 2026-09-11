using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>Allocates physical routing resources without changing frozen logical topology.</summary>
public sealed class ArchitectureV7CollectivePostRoutingAllocationStage
{
    // Complete the same collective allocation after node-span coordinates are
    // known. A bijection of the existing vertical lanes preserves every
    // interval conflict, lane count and domain boundary. It adds only the
    // missing compatibility with separately owned endpoint corridors.
    public static ArchitectureV7CollectiveAllocationFreeze AllocateCorridorCompatibility(
        ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7EndpointCorridor> corridors,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns)
    {
        var diagnostics = allocation.Diagnostics.ToList();
        var assigned = allocation.RunAssignments.ToDictionary(a => a.RunId, StringComparer.Ordinal);
        foreach (var domain in allocation.Runs.Where(r => r.Orientation == ArchitectureV7RunOrientation.Vertical).GroupBy(r => r.Cells[0].Column))
        {
            var groups = domain.GroupBy(r => assigned[r.RunId].LaneOrdinal).OrderBy(g => g.Key).ToArray();
            var candidates = new Dictionary<int, int[]>();
            foreach (var group in groups)
                candidates[group.Key] = groups.Select(g => g.Key).Where(lane =>
                    ArchitectureV7EndpointCorridorConflictAudit.Find(allocation, corridors, rows, columns,
                        group.ToDictionary(r => r.RunId, _ => lane, StringComparer.Ordinal)).Count == 0)
                    .OrderBy(lane => lane == group.Key ? 0 : 1).ThenBy(lane => lane).ToArray();
            var owner = new Dictionary<int, int>();
            bool Place(int original, HashSet<int> visited)
            {
                foreach (var lane in candidates[original])
                {
                    if (!visited.Add(lane)) continue;
                    if (!owner.TryGetValue(lane, out var previous) || Place(previous, visited))
                    { owner[lane] = original; return true; }
                }
                return false;
            }
            // Keep already-compatible domains byte-for-byte unchanged.
            if (groups.All(g => candidates[g.Key].Contains(g.Key))) continue;
            var complete = groups.OrderBy(g => candidates[g.Key].Length).ThenBy(g => g.Key)
                .All(g => Place(g.Key, new HashSet<int>()));
            if (!complete)
            {
                diagnostics.Add(new("ENDPOINT-CORRIDOR-LANE-DOMAIN-CONFLICT",
                    "No bijection of existing vertical lanes avoids endpoint corridors in column " + domain.Key + "; candidates=" +
                    string.Join(";", candidates.Select(pair => pair.Key + "->[" + string.Join(",", pair.Value) + "]")), true));
                continue;
            }
            foreach (var group in groups)
            {
                var lane = owner.Single(pair => pair.Value == group.Key).Key;
                foreach (var run in group)
                    assigned[run.RunId] = assigned[run.RunId] with { LaneOrdinal = lane,
                        LaneId = "lane:V:" + domain.Key + ":" + lane, SignedLaneOrdinal = lane - (groups.Length - 1) / 2d };
            }
        }
        var assignments = allocation.RunAssignments.Select(a => assigned[a.RunId]).ToArray();
        var lanes = allocation.Lanes.Select(l => l with { RunIds = assignments.Where(a => a.LaneId == l.LaneId).Select(a => a.RunId).OrderBy(id => id, StringComparer.Ordinal).ToArray() }).ToArray();
        var spacing = allocation.AllocationConfiguration?.ParallelLaneSpacing ?? 0;
        var bends = allocation.Bends.Select(b => b with { IncomingLaneId = assigned[b.IncomingRunId].LaneId, OutgoingLaneId = assigned[b.OutgoingRunId].LaneId,
            RelativePosition = new ArchitectureV7PhysicalRelativePosition(assigned[b.IncomingOrientation == ArchitectureV7RunOrientation.Vertical ? b.IncomingRunId : b.OutgoingRunId].SignedLaneOrdinal * spacing, b.EffectiveRelativePosition.YOffset) }).ToArray();
        var crossings = allocation.Crossings.Select(c => c with { VerticalLaneId = assigned[c.VerticalRunId].LaneId,
            RelativePosition = new ArchitectureV7PhysicalRelativePosition(assigned[c.VerticalRunId].SignedLaneOrdinal * spacing, c.EffectiveRelativePosition.YOffset) }).ToArray();
        var interactions = allocation.CrossingInteractions.Select(c => c with { VerticalLaneId = assigned[c.VerticalRunId].LaneId }).ToArray();
        return new ArchitectureV7CollectiveAllocationFreeze(allocation.Runs, lanes, assignments, allocation.Terminals, allocation.Approaches,
            allocation.Handoffs, bends, crossings, diagnostics, allocation.PlacementFingerprint, allocation.RouteFingerprint,
            allocation.AllocationFingerprint + "|corridor-compatible:" + string.Join(";", assignments.Select(a => a.RunId + "=" + a.LaneId)),
            interactions, allocation.AllocationConfiguration, allocation.EndpointLaneCoordinates, allocation.SharedVerticalRunConstraints, allocation.EndpointZBends);
    }
    public ArchitectureV7CollectiveAllocationFreeze Allocate(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7AllocationConfiguration configuration)
        => Allocate(placement, routes, null, configuration);

    public ArchitectureV7CollectiveAllocationFreeze Allocate(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7RouteCorridorProjectionFreeze? corridorProjection,
        ArchitectureV7AllocationConfiguration configuration)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (routes is null) throw new ArgumentNullException(nameof(routes));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (!string.Equals(routes.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The logical route freeze does not belong to the supplied placement freeze.", nameof(routes));
        if (corridorProjection is not null && (!string.Equals(corridorProjection.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal) ||
            !string.Equals(corridorProjection.RouteFingerprint, routes.RouteFingerprint, StringComparison.Ordinal)))
            throw new ArgumentException("The corridor projection does not belong to the supplied placement and route freezes.", nameof(corridorProjection));
        if (configuration.ParallelLaneSpacing < 0 || configuration.TerminalPortSpacing < 0 || configuration.TerminalInset < 0 || configuration.BaseCellWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(configuration));

        var diagnostics = new List<ArchitectureV7AllocationDiagnostic>();
        var runs = BuildRuns(routes, diagnostics);
        var logicalRuns = runs;
        var sharedVerticalRunConstraints = BuildSharedVerticalRunConstraints(routes, runs);
        runs = ArchitectureV7EndpointCorridorResources.AddTransitions(placement, routes, runs, diagnostics);
        var (lanes, assignments) = AllocateLanes(placement, routes, runs, configuration.ParallelLaneSpacing, corridorProjection);
        ValidateHorizontalLaneConflicts(runs, assignments, diagnostics);
        var terminals = AllocateTerminals(placement, routes, logicalRuns, assignments, configuration, diagnostics);
        var endpointZBends = BuildEndpointZBends(placement, routes, logicalRuns, terminals, configuration);
        ValidateSharedVerticalRunIntersections(placement, sharedVerticalRunConstraints, terminals, endpointZBends, configuration, diagnostics, runs);
        var endpointLaneCoordinates = BuildEndpointLaneCoordinates(placement, routes, runs, terminals, diagnostics);
        var approaches = BuildApproaches(routes, runs, assignments, terminals);
        var handoffs = BuildHandoffs(placement, routes, runs, assignments, terminals, configuration, diagnostics);
        var bends = BuildBendsResources(runs, routes, assignments, configuration, diagnostics);
        var crossingResult = BuildCrossingResources(routes, runs, assignments, bends, configuration, diagnostics);
        var crossings = crossingResult.Resources;
        var crossingInteractions = crossingResult.Interactions;
        var fingerprint = Fingerprint(placement.PlacementFingerprint, routes.RouteFingerprint, runs, assignments, terminals, endpointLaneCoordinates, sharedVerticalRunConstraints, approaches, handoffs, bends, crossings, diagnostics,
            endpointZBends);
        return new ArchitectureV7CollectiveAllocationFreeze(runs, lanes, assignments, terminals, approaches, handoffs, bends, crossings,
            diagnostics, placement.PlacementFingerprint, routes.RouteFingerprint, fingerprint, crossingInteractions, configuration, endpointLaneCoordinates, sharedVerticalRunConstraints, endpointZBends);
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
        AllocateLanes(ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
            IReadOnlyList<ArchitectureV7StraightRun> runs, int spacing, ArchitectureV7RouteCorridorProjectionFreeze? corridorProjection)
    {
        var lanes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var assignments = new List<ArchitectureV7RunLaneAssignment>();
        var used = new Dictionary<string, List<(int Start, int End, int Ordinal, string RunId)>>(StringComparer.Ordinal);
        var runById = runs.ToDictionary(item => item.RunId, StringComparer.Ordinal);
        var orderedRuns = runs.OrderBy(x => x.Orientation).ThenBy(x => FixedCoordinate(x))
            .ThenBy(x => EndpointLaneOrderingKey(x).Primary)
            .ThenBy(x => EndpointLaneOrderingKey(x).Secondary)
            .ThenBy(x => HorizontalPeelOrderingKey(x).Primary)
            .ThenBy(x => HorizontalPeelOrderingKey(x).Secondary)
            .ThenBy(x => HorizontalPeelOrderingKey(x).Direction)
            .ThenBy(x => MinCoordinate(x)).ThenBy(x => x.RunId, StringComparer.Ordinal)
            .ToArray();
        foreach (var run in orderedRuns)
        {
            var usage = corridorProjection?.Usages.FirstOrDefault(item => item.PhysicalLinkId == run.PhysicalLinkId && item.StartRouteIndex == run.StartRouteIndex);
            var domain = run.Orientation + ":" + FixedCoordinate(run);
            var startCoordinate = MinCoordinate(run);
            var endCoordinate = MaxCoordinate(run);
            if (!used.TryGetValue(domain, out var intervals)) used[domain] = intervals = new();
            var ordinal = 0;
            while (intervals.Any(interval => interval.Ordinal == ordinal && interval.RunId != run.RunId &&
                HorizontalIntervalsConflict(run, runById[interval.RunId], startCoordinate, endCoordinate, interval.Start, interval.End))) ordinal++;
            var laneId = "lane:" + (run.Orientation == ArchitectureV7RunOrientation.Horizontal ? "H" : "V") + ":" + FixedCoordinate(run) + ":" + ordinal;
            assignments.Add(new(run.RunId, laneId, ordinal));
            intervals.Add((startCoordinate, endCoordinate, ordinal, run.RunId));
            if (!lanes.TryGetValue(laneId, out var laneRuns)) lanes[laneId] = laneRuns = new();
            laneRuns.Add(run.RunId);
        }
        assignments = assignments.Select(assignment =>
        {
            var domain = runsByAssignmentDomain(assignment.RunId);
            var domainOrdinals = assignments.Where(candidate => runsByAssignmentDomain(candidate.RunId) == domain)
                .Select(candidate => candidate.LaneOrdinal).Distinct().OrderBy(value => value).ToArray();
            var index = Array.IndexOf(domainOrdinals, assignment.LaneOrdinal);
            return assignment with { SignedLaneOrdinal = index - (domainOrdinals.Length - 1) / 2d };
        }).ToList();

        var laneModels = lanes.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ArchitectureV7PhysicalLane(x.Key, x.Key.Contains(":H:", StringComparison.Ordinal) ? ArchitectureV7RunOrientation.Horizontal : ArchitectureV7RunOrientation.Vertical,
                ParseOrdinal(x.Key), spacing, x.Value.OrderBy(v => v, StringComparer.Ordinal).ToArray())).ToArray();
        return (laneModels, assignments);

        string runsByAssignmentDomain(string runId)
        {
            var run = runById[runId];
            return run.Orientation + ":" + FixedCoordinate(run);
        }

        bool HorizontalIntervalsConflict(ArchitectureV7StraightRun left, ArchitectureV7StraightRun right,
            int leftStart, int leftEnd, int rightStart, int rightEnd)
        {
            if (leftStart < rightEnd && rightStart < leftEnd) return true;
            var touches = leftEnd == rightStart || rightEnd == leftStart;
            return touches && (HasPerpendicularTurn(left) || HasPerpendicularTurn(right));
        }

        bool HasPerpendicularTurn(ArchitectureV7StraightRun run)
        {
            var route = routes.Routes.FirstOrDefault(item => item.PhysicalLinkId == run.PhysicalLinkId);
            return route is not null && (run.StartRouteIndex > 0 || run.EndRouteIndex < route.Cells.Count - 1);
        }

        (int Primary, int Secondary, int Direction) HorizontalPeelOrderingKey(ArchitectureV7StraightRun run)
        {
            if (run.Orientation != ArchitectureV7RunOrientation.Horizontal) return (int.MaxValue, int.MaxValue, 0);
            var route = routes.Routes.FirstOrDefault(item => item.PhysicalLinkId == run.PhysicalLinkId);
            if (route is null) return (int.MaxValue, int.MaxValue, 0);
            var direction = Math.Sign(MaxCoordinate(run) - MinCoordinate(run));
            if (direction == 0) return (int.MaxValue, int.MaxValue, 0);
            var nextIndex = run.EndRouteIndex + 1;
            var nextBend = nextIndex < route.Cells.Count ? route.Cells[nextIndex].Column : MaxCoordinate(run);
            var travelOrder = direction > 0 ? nextBend : -nextBend;
            var distance = direction > 0
                ? Math.Abs(nextBend - MaxCoordinate(run))
                : Math.Abs(nextBend - MinCoordinate(run));
            return (travelOrder, distance, direction);
        }

        (int Primary, double Secondary) EndpointLaneOrderingKey(ArchitectureV7StraightRun run)
        {
            if (run.Orientation != ArchitectureV7RunOrientation.Horizontal) return (1, 0d);
            var route = routes.Routes.FirstOrDefault(item => item.PhysicalLinkId == run.PhysicalLinkId);
            if (route is null) return (1, 0d);
            var candidates = new[] { ArchitectureV7EndpointKind.SourceDeparture, ArchitectureV7EndpointKind.DestinationArrival }
                .Select(kind => EndpointLaneCandidate(run, route, kind))
                .Where(candidate => candidate is not null && candidate.Value.DirectionGroup != 1)
                .Select(candidate => candidate!.Value)
                .ToArray();
            if (candidates.Length == 0) return (1, 0d); // direct vertical connections remain outside side-group nesting
            var candidate = candidates
                .OrderByDescending(item => DomainAxisCount(item.EndpointKind, run))
                .ThenBy(item => item.EndpointKind)
                .First();
            var outerToInner = candidate.DirectionGroup == 0 ? candidate.Axis : -candidate.Axis;
            // Terminal order is outer-to-inner. Lane depth is deliberately independent:
            // for bottom departures the first lane is nearest the node; for top arrivals
            // the last lane is nearest the node, so invert the ordering on arrivals.
            return candidate.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? (0, outerToInner)
                : (0, -outerToInner);

            (ArchitectureV7EndpointKind EndpointKind, int DirectionGroup, double Axis)? EndpointLaneCandidate(
                ArchitectureV7StraightRun candidateRun, ArchitectureV7LogicalRoute candidateRoute, ArchitectureV7EndpointKind endpointKind)
            {
                var endpointRun = endpointKind == ArchitectureV7EndpointKind.SourceDeparture
                    ? runs.Where(item => item.PhysicalLinkId == candidateRoute.PhysicalLinkId && item.StartRouteIndex > 0)
                        .OrderBy(item => item.StartRouteIndex).FirstOrDefault(item => item.Orientation == ArchitectureV7RunOrientation.Horizontal)
                    : runs.Where(item => item.PhysicalLinkId == candidateRoute.PhysicalLinkId && item.EndRouteIndex < candidateRoute.Cells.Count - 1)
                        .OrderByDescending(item => item.EndRouteIndex).FirstOrDefault(item => item.Orientation == ArchitectureV7RunOrientation.Horizontal);
                if (endpointRun?.RunId != candidateRun.RunId) return null;
                var node = placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == (endpointKind == ArchitectureV7EndpointKind.SourceDeparture ? candidateRoute.SourcePhysicalNodeId : candidateRoute.DestinationPhysicalNodeId));
                if (node is null) return null;
                var endpoint = endpointKind == ArchitectureV7EndpointKind.SourceDeparture ? candidateRoute.Cells[0] : candidateRoute.Cells[candidateRoute.Cells.Count - 1];
                var near = endpointKind == ArchitectureV7EndpointKind.SourceDeparture ? candidateRoute.Cells[1] : candidateRoute.Cells[candidateRoute.Cells.Count - 2];
                var adjacentRun = endpointKind == ArchitectureV7EndpointKind.SourceDeparture
                    ? runs.FirstOrDefault(item => item.PhysicalLinkId == candidateRoute.PhysicalLinkId && item.StartRouteIndex == 0)
                    : runs.LastOrDefault(item => item.PhysicalLinkId == candidateRoute.PhysicalLinkId && item.EndRouteIndex == candidateRoute.Cells.Count - 1);
                var directionGroup = EndpointDirectionGroup(candidateRoute, endpointKind, near, endpoint, adjacentRun, node, runs);
                return (endpointKind, directionGroup, EndpointPhysicalAxisCoordinate(candidateRoute, endpointKind, near, endpoint, adjacentRun, placement));
            }

            int DomainAxisCount(ArchitectureV7EndpointKind endpointKind, ArchitectureV7StraightRun domainRun) =>
                runs.Where(item => item.Orientation == ArchitectureV7RunOrientation.Horizontal && FixedCoordinate(item) == FixedCoordinate(domainRun))
                    .Select(item => (Run: item, Route: routes.Routes.FirstOrDefault(routeItem => routeItem.PhysicalLinkId == item.PhysicalLinkId)))
                    .Where(item => item.Route is not null)
                    .Select(item => EndpointLaneCandidate(item.Run, item.Route!, endpointKind))
                    .Where(item => item is not null && item.Value.DirectionGroup != 1)
                    .Select(item => item!.Value.Axis).Distinct().Count();

        }

    }

    private static void ValidateHorizontalLaneConflicts(
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var assignmentByRun = assignments.ToDictionary(item => item.RunId, StringComparer.Ordinal);
        foreach (var row in runs.Where(item => item.Orientation == ArchitectureV7RunOrientation.Horizontal)
                     .GroupBy(FixedCoordinate))
        {
            var rowRuns = row.Where(item => assignmentByRun.ContainsKey(item.RunId)).ToArray();
            for (var leftIndex = 0; leftIndex < rowRuns.Length; leftIndex++)
            {
                var left = rowRuns[leftIndex];
                for (var rightIndex = leftIndex + 1; rightIndex < rowRuns.Length; rightIndex++)
                {
                    var right = rowRuns[rightIndex];
                    if (assignmentByRun[left.RunId].LaneOrdinal != assignmentByRun[right.RunId].LaneOrdinal ||
                        MinCoordinate(left) >= MaxCoordinate(right) || MinCoordinate(right) >= MaxCoordinate(left)) continue;
                    diagnostics.Add(new ArchitectureV7AllocationDiagnostic(
                        "HORIZONTAL-LANE-INTERVAL-CONFLICT",
                        "Overlapping horizontal straight runs share one allocated lane.", true,
                        left.PhysicalLinkId, left.RunId,
                        ConflictingPhysicalLinkIds: new[] { left.PhysicalLinkId, right.PhysicalLinkId },
                        ConflictingRunIds: new[] { left.RunId, right.RunId }));
                }
            }
        }
    }

    private static IReadOnlyList<ArchitectureV7TerminalSlotAssignment> AllocateTerminals(
        ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs, IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments,
        ArchitectureV7AllocationConfiguration configuration,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var groups = new Dictionary<(string Node, ArchitectureV7EndpointKind Kind), List<(ArchitectureV7LogicalRoute Route, int Anchor, ArchitectureV7StraightRun? AdjacentRun, double LaneOffset, ArchitectureV7EndpointDirection Direction, int DirectionGroup, double PhysicalAxisCoordinate)>>();
        foreach (var route in routes.Routes.Where(x => x.IsComplete && x.Cells.Count >= 2))
        {
            Add(route, ArchitectureV7EndpointKind.SourceDeparture, route.SourcePhysicalNodeId, route.Cells[1], route.Cells[0], true);
            Add(route, ArchitectureV7EndpointKind.DestinationArrival, route.DestinationPhysicalNodeId, route.Cells[route.Cells.Count - 2], route.Cells[route.Cells.Count - 1], false);
        }
        var result = new List<ArchitectureV7TerminalSlotAssignment>();
        foreach (var group in groups.OrderBy(x => x.Key.Node, StringComparer.Ordinal).ThenBy(x => x.Key.Kind))
        {
            var node = placement.Nodes.FirstOrDefault(x => x.PhysicalNodeId == group.Key.Node);
            if (node is null)
            {
                diagnostics.Add(new("UNKNOWN-ENDPOINT", "A route endpoint is absent from the placement freeze.", true, group.Value[0].Route.PhysicalLinkId));
                continue;
            }
            // The initial order defines the existing terminal-slot geometry.
            // Fixed-X endpoints remain in these exact slots; only flexible
            // turning endpoints are permuted around those anchors.
            var entries = group.Value.OrderBy(x => x.DirectionGroup)
                .ThenBy(x => x.PhysicalAxisCoordinate)
                .ThenBy(x => x.LaneOffset).ThenBy(x => x.Anchor)
                .ThenBy(x => x.Route.PhysicalLinkId, StringComparer.Ordinal).ToArray();
            var occupiedSpan = Math.Max(0, entries.Length - 1) * (double)configuration.TerminalPortSpacing;
            var available = node.LogicalSpan * (double)configuration.BaseCellWidth;
            var usableWidth = Math.Max(0d, available - 2d * configuration.TerminalInset);
            var capacity = 2 * configuration.TerminalInset + occupiedSpan;
            if (occupiedSpan > usableWidth)
            {
                diagnostics.Add(new("NODE-SPAN-CAPACITY", "The frozen node span cannot contain the terminal population at configured spacing; downstream compensation is forbidden.", true,
                    entries[0].Route.PhysicalLinkId, null, node.PhysicalNodeId, group.Key.Kind.ToString(), checked((int)Math.Round(capacity, MidpointRounding.AwayFromZero)), checked((int)Math.Round(available, MidpointRounding.AwayFromZero)), node.LogicalSpan,
                    entries.Select(x => x.Route.PhysicalLinkId).Distinct(StringComparer.Ordinal).ToArray(), null));
                diagnostics.Add(new("TERMINAL-OVERFLOW", "Terminal demand exceeds the frozen physical span; node expansion is forbidden.", true,
                    entries[0].Route.PhysicalLinkId, null, node.PhysicalNodeId, group.Key.Kind.ToString(), checked((int)Math.Round(capacity, MidpointRounding.AwayFromZero)), checked((int)Math.Round(available, MidpointRounding.AwayFromZero)), node.LogicalSpan,
                    entries.Select(x => x.Route.PhysicalLinkId).Distinct(StringComparer.Ordinal).ToArray(), null));
            }
            var direct = entries.Where(x => x.DirectionGroup == 1).ToArray();
            var left = entries.Where(x => x.DirectionGroup == 0).ToArray();
            var right = entries.Where(x => x.DirectionGroup == 2).ToArray();
            var (offsets, directCentred) = PackedTerminalOffsets(left.Length, direct.Length, right.Length,
                configuration.TerminalPortSpacing, usableWidth / 2d);
            // Slot geometry is packed here, but relationship-to-slot ordering
            // is deliberately deferred until actual physical bend coordinates
            // exist in ArchitectureV7EndpointGeometryAllocationStage.
            for (var index = 0; index < entries.Length; index++)
            {
                var offset = offsets[index];
                result.Add(new(entries[index].Route.PhysicalLinkId, node.PhysicalNodeId, group.Key.Kind, entries[index].Direction, index, offset, checked((int)Math.Round(capacity, MidpointRounding.AwayFromZero)),
                    (direct.Length > 0 && directCentred ? "terminal-order=direction-groups;direct-centred" :
                        direct.Length > 0 ? "terminal-order=direction-groups;direct-displaced" : "terminal-order=direction-groups;complete-set-centred") + ";endpoint-order=deferred-to-actual-bend-depth;direction=" + entries[index].Direction + ";direction-group=" + entries[index].DirectionGroup)
                { SignedSlotOrdinal = configuration.TerminalPortSpacing == 0 ? 0 : offset / configuration.TerminalPortSpacing });
            }
        }
        return result;

        void Add(ArchitectureV7LogicalRoute route, ArchitectureV7EndpointKind kind, string nodeId, ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, bool source)
        {
            var direction = Direction(near, endpoint, source);
            var adjacentRun = source
                ? runs.FirstOrDefault(x => x.PhysicalLinkId == route.PhysicalLinkId && x.StartRouteIndex == 0)
                : runs.LastOrDefault(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndRouteIndex == route.Cells.Count - 1);
            var approachRun = source
                ? runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId && x.StartRouteIndex > 0)
                    .OrderBy(x => x.StartRouteIndex).FirstOrDefault(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal)
                : runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndRouteIndex < route.Cells.Count - 1)
                    .OrderByDescending(x => x.EndRouteIndex).FirstOrDefault(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal);
            var laneSource = approachRun ?? adjacentRun;
            var laneOffset = laneSource is not null
                ? LaneAxisOffset(laneSource, assignments.First(x => x.RunId == laneSource.RunId), runs, assignments, configuration.ParallelLaneSpacing)
                : 0;
            var anchor = near.Column;
            var endpointNode = placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == nodeId);
            var directionGroup = EndpointDirectionGroup(route, kind, near, endpoint, adjacentRun, endpointNode, runs);
            var physicalAxisCoordinate = EndpointPhysicalAxisCoordinate(route, kind, near, endpoint, adjacentRun, placement);
            var key = (nodeId, kind);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            list.Add((route, anchor, approachRun, laneOffset, direction, directionGroup, physicalAxisCoordinate));
        }
    }

    private static (IReadOnlyList<double> Offsets, bool DirectCentred) PackedTerminalOffsets(
        int leftCount, int directCount, int rightCount, int spacing, double usableHalfExtent)
    {
        if (leftCount + directCount + rightCount == 1)
            return (new[] { 0d }, directCount > 0);

        var orderedCount = leftCount + directCount + rightCount;
        var occupiedSpan = Math.Max(0, orderedCount - 1) * (double)spacing;
        var centred = Enumerable.Range(0, orderedCount)
            .Select(index => -occupiedSpan / 2d + index * (double)spacing)
            .ToArray();

        // One edge owns one contiguous slot sequence.  Group classification
        // controls relationship order only; it must not reserve a separate
        // negative/zero/positive coordinate band and introduce a hole at a
        // group boundary.
        // Without a genuine Direct/fixed-X anchor, the complete edge
        // population owns one centred slot block.  Left/Right are ordering
        // classifications only; neither group may shift the block toward its
        // physical approach side.
        if (directCount == 0)
            return (centred, false);

        // A direct relationship may stay at the midpoint when the complete
        // sequence fits around that fixed slot.  The side counts determine
        // the slot index, but every adjacent slot remains exactly one spacing
        // apart.
        var directCentred = Enumerable.Range(0, orderedCount)
            .Select(index => (index - leftCount - (directCount - 1) / 2d) * (double)spacing)
            .ToArray();
        if (directCentred.All(offset => offset >= -usableHalfExtent && offset <= usableHalfExtent))
            return (directCentred, true);

        // Displaced-direct mode shifts the complete sequence as one unit,
        // retaining configured spacing and equal outer margins.
        return (centred, false);
    }

    private static IReadOnlyList<double> MidpointTerminalOffsets(int leftCount, int directCount, int rightCount, int spacing)
    {
        var directOffsets = Enumerable.Range(0, directCount)
            .Select(index => (index - (directCount - 1) / 2d) * spacing).ToArray();
        var leftOffsets = Enumerable.Range(1, leftCount).Select(index => -index * (double)spacing).Reverse().ToArray();
        var rightOffsets = Enumerable.Range(1, rightCount).Select(index => index * (double)spacing).Reverse().ToArray();
        return leftOffsets.Concat(directOffsets).Concat(rightOffsets).ToArray();
    }

    private static int EndpointDirectionGroup(ArchitectureV7LogicalRoute route, ArchitectureV7EndpointKind endpointKind,
        ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, ArchitectureV7StraightRun? run,
        ArchitectureV7FrozenNodePlacement? node, IReadOnlyList<ArchitectureV7StraightRun> runs)
    {
        // A maximal vertical relationship has no horizontal approach from
        // which a Left/Right group can be inferred.  Its vertical run is the
        // fixed-X authority, so it is the Direct group even when the frozen
        // endpoint cell lies away from the node centre.  Classifying it from
        // the endpoint column would make the fixed relationship a side-group
        // anchor; endpoint packing would then leave a hole when flexible side
        // relationships are reordered around it.
        var routeRuns = runs.Where(item => item.PhysicalLinkId == route.PhysicalLinkId)
            .OrderBy(item => item.StartRouteIndex).ToArray();
        if (routeRuns.Length == 1 && routeRuns[0].Orientation == ArchitectureV7RunOrientation.Vertical &&
            routeRuns[0].StartRouteIndex == 0 && routeRuns[0].EndRouteIndex == route.Cells.Count - 1)
            return 1;

        var endpointRun = endpointKind == ArchitectureV7EndpointKind.SourceDeparture
            ? runs.Where(item => item.PhysicalLinkId == route.PhysicalLinkId && item.StartRouteIndex > 0)
                .OrderBy(item => item.StartRouteIndex).FirstOrDefault(item => item.Orientation == ArchitectureV7RunOrientation.Horizontal)
            : runs.Where(item => item.PhysicalLinkId == route.PhysicalLinkId && item.EndRouteIndex < route.Cells.Count - 1)
                .OrderByDescending(item => item.EndRouteIndex).FirstOrDefault(item => item.Orientation == ArchitectureV7RunOrientation.Horizontal);
        if (endpointRun is not null)
        {
            var first = endpointRun.Cells[0];
            var last = endpointRun.Cells[endpointRun.Cells.Count - 1];
            var side = endpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? Math.Sign(last.Column - endpoint.Column)
                : Math.Sign(first.Column - endpoint.Column);
            if (side < 0) return 0;
            if (side > 0) return 2;
            return 1;
        }
        if (node is not null && run?.Orientation == ArchitectureV7RunOrientation.Vertical)
            return endpoint.Column < node.CentreCell ? 0 : endpoint.Column > node.CentreCell ? 2 : 1;
        if (node is not null && run?.Orientation == ArchitectureV7RunOrientation.Horizontal)
        {
            var centreRow = node.LogicalFootprint.Count == 0 ? node.DiagramRow : node.LogicalFootprint.Select(cell => cell.Row).Distinct().OrderBy(row => row).ElementAt(node.LogicalFootprint.Select(cell => cell.Row).Distinct().Count() / 2);
            return endpoint.Row < centreRow ? 0 : endpoint.Row > centreRow ? 2 : 1;
        }
        if (run?.Orientation == ArchitectureV7RunOrientation.Vertical)
            return near.Column == endpoint.Column ? 1 : near.Column < endpoint.Column ? 0 : 2;
        if (run?.Orientation == ArchitectureV7RunOrientation.Horizontal)
            return near.Row == endpoint.Row ? 1 : near.Row < endpoint.Row ? 0 : 2;
        if (near.Column != endpoint.Column)
            return near.Column < endpoint.Column ? 0 : 2;
        return near.Row == endpoint.Row ? 1 : near.Row < endpoint.Row ? 0 : 2;
    }

    private static double EndpointPhysicalAxisCoordinate(ArchitectureV7LogicalRoute route, ArchitectureV7EndpointKind endpointKind,
        ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, ArchitectureV7StraightRun? run,
        ArchitectureV7PlacementFreeze placement) =>
        endpointKind == ArchitectureV7EndpointKind.SourceDeparture
            ? placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == route.DestinationPhysicalNodeId)?.CentreCell ?? near.Column
            : endpointKind == ArchitectureV7EndpointKind.DestinationArrival
                ? placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == route.SourcePhysicalNodeId)?.CentreCell ?? near.Column
                : run?.Orientation == ArchitectureV7RunOrientation.Vertical
                    ? near.Column
                    : run?.Orientation == ArchitectureV7RunOrientation.Horizontal
                        ? near.Row
                        : near.Column != endpoint.Column ? near.Column : near.Row;

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

    private static IReadOnlyList<ArchitectureV7SharedVerticalRunConstraint> BuildSharedVerticalRunConstraints(
        ArchitectureV7LogicalRouteFreeze routes, IReadOnlyList<ArchitectureV7StraightRun> runs)
    {
        return routes.Routes
            .Where(route => route.IsComplete && route.Cells.Count >= 2)
            .Select(route =>
            {
                var routeRuns = runs.Where(run => run.PhysicalLinkId == route.PhysicalLinkId).OrderBy(run => run.StartRouteIndex).ToArray();
                return (Route: route, Runs: routeRuns);
            })
            .Where(item => item.Runs.Length == 1 && item.Runs[0].Orientation == ArchitectureV7RunOrientation.Vertical
                && item.Runs[0].StartRouteIndex == 0 && item.Runs[0].EndRouteIndex == item.Route.Cells.Count - 1)
            .Select(item => new ArchitectureV7SharedVerticalRunConstraint(
                item.Route.PhysicalLinkId,
                item.Runs[0].RunId,
                item.Route.SourcePhysicalNodeId,
                item.Route.DestinationPhysicalNodeId,
                "shared-maximal-vertical-run;source-terminal=run-X;destination-terminal=run-X;logical-topology-unchanged"))
            .OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateSharedVerticalRunIntersections(
        ArchitectureV7PlacementFreeze placement,
        IReadOnlyList<ArchitectureV7SharedVerticalRunConstraint> constraints,
        IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        IReadOnlyList<ArchitectureV7EndpointZBend> endpointZBends,
        ArchitectureV7AllocationConfiguration configuration,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics,
        IReadOnlyList<ArchitectureV7StraightRun> runs)
    {
        foreach (var constraint in constraints)
        {
            var endpointIntervals = new List<(double Lower, double Upper)>();
            foreach (var endpoint in terminals.Where(item => item.PhysicalLinkId == constraint.PhysicalLinkId))
            {
                // A displaced fixed-X endpoint deliberately no longer asks the
                // terminal slot to equal the shared run X. Its final drop is
                // terminal-anchored and the allocator owns the local Z bend.
                if (endpointZBends.Any(z => z.PhysicalLinkId == endpoint.PhysicalLinkId && z.EndpointKind == endpoint.EndpointKind))
                    continue;
                var node = placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
                if (node is null) continue;
                var run = runs.First(item => item.RunId == constraint.RunId);
                var cell = endpoint.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? run.Cells[0] : run.Cells[run.Cells.Count - 1];
                if (node.LogicalFootprint.Any(occupied => occupied.Row == cell.Row && occupied.Column == cell.Column) &&
                    runs.Any(item => item.PhysicalLinkId == endpoint.PhysicalLinkId && item.EndpointContext == "endpoint-transition:" + endpoint.EndpointKind))
                    continue; // Node-span corridor, not a shared terminal-X constraint.
                var centre = node.CentreCell * configuration.BaseCellWidth;
                var half = Math.Max(0d, node.LogicalSpan * configuration.BaseCellWidth / 2d - configuration.TerminalInset);
                var lower = centre - half;
                var upper = centre + half;
                var peers = terminals.Where(item => item.PhysicalNodeId == endpoint.PhysicalNodeId &&
                    item.EndpointKind == endpoint.EndpointKind && item.PhysicalLinkId != endpoint.PhysicalLinkId).ToArray();
                lower = Math.Max(lower, peers.Where(peer => peer.SlotOrdinal < endpoint.SlotOrdinal)
                    .Select(peer => centre + peer.RelativeOffset + configuration.TerminalPortSpacing).DefaultIfEmpty(lower).Max());
                upper = Math.Min(upper, peers.Where(peer => peer.SlotOrdinal > endpoint.SlotOrdinal)
                    .Select(peer => centre + peer.RelativeOffset - configuration.TerminalPortSpacing).DefaultIfEmpty(upper).Min());
                endpointIntervals.Add((lower, upper));
            }
            if (endpointIntervals.Count == 2)
            {
                var lower = endpointIntervals.Max(item => item.Lower);
                var upper = endpointIntervals.Min(item => item.Upper);
                if (lower > upper)
                    diagnostics.Add(new("SHARED-VERTICAL-RUN-CONSTRAINT-EMPTY-INTERSECTION",
                        $"No legal shared X exists for run {constraint.RunId}; endpoint legal intervals intersect as [{lower:R},{upper:R}].", true,
                        constraint.PhysicalLinkId, constraint.RunId));
            }
        }
    }

    private static IReadOnlyList<ArchitectureV7EndpointZBend> BuildEndpointZBends(
        ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        ArchitectureV7AllocationConfiguration configuration)
    {
        var result = new List<ArchitectureV7EndpointZBend>();
        foreach (var terminal in terminals)
        {
            // Only a true maximal vertical relationship has fixed incoming-X
            // authority. Ordinary endpoint turns already own a normal bend.
            var route = routes.Routes.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
            if (route is null || route.Cells.Count < 2) continue;
            var routeRuns = runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId).OrderBy(x => x.StartRouteIndex).ToArray();
            var fixedRun = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? routeRuns.FirstOrDefault(x => x.StartRouteIndex == 0)
                : routeRuns.LastOrDefault(x => x.EndRouteIndex == route.Cells.Count - 1);
            var direct = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? terminal.Direction == ArchitectureV7EndpointDirection.Down
                : terminal.Direction == ArchitectureV7EndpointDirection.Up;
            var node = placement.Nodes.FirstOrDefault(x => x.PhysicalNodeId == terminal.PhysicalNodeId);
            if (node is null) continue;
            if (!direct || routeRuns.Length != 1 || fixedRun?.Orientation != ArchitectureV7RunOrientation.Vertical ||
                fixedRun.StartRouteIndex != 0 || fixedRun.EndRouteIndex != route.Cells.Count - 1 ||
                !terminal.Provenance.Contains("direct-displaced", StringComparison.Ordinal) ||
                terminal.TerminalCapacityRequirement > node.LogicalSpan * configuration.BaseCellWidth) continue;
            var endpointCell = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Cells[0] : route.Cells[route.Cells.Count - 1];
            // The local Z resource is legal only when the frozen endpoint cell
            // actually belongs to the node footprint. This keeps malformed
            // shared-run fixtures as explicit allocation failures.
            if (!node.LogicalFootprint.Any(cell => cell.Row == endpointCell.Row && cell.Column == endpointCell.Column)) continue;
            var fixedOffset = (endpointCell.Column - node.CentreCell) * (double)configuration.BaseCellWidth;
            if (Math.Abs(fixedOffset - terminal.RelativeOffset) <= 0.001) continue;
            result.Add(new ArchitectureV7EndpointZBend(terminal.PhysicalLinkId, terminal.PhysicalNodeId,
                terminal.EndpointKind, fixedRun.RunId,
                "endpoint-z-bend;fixed-incoming-x;fit-first-packed-terminal;final-drop-terminal-x"));
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

            // Ordinary horizontal-to-vertical endpoint turns are represented by
            // the allocated bend. A terminal/lane offset is no longer an
            // endpoint handoff reason; endpoint-local lane coordinates own it.
            // Centre-cell compensation is diagnosed only by a concrete failure
            // to represent the frozen route, never by terminal offset alone.
            if (run.Orientation == ArchitectureV7RunOrientation.Vertical) continue;
            diagnostics.Add(new("ENDPOINT-FINAL-VERTICAL-RUN-MISSING",
                "The endpoint contract requires a final vertical approach lane on the top/bottom node edge.", true,
                terminal.PhysicalLinkId, run.RunId, terminal.PhysicalNodeId, terminal.EndpointKind.ToString()));
            continue;
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7EndpointLaneCoordinate> BuildEndpointLaneCoordinates(
        ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7EndpointLaneCoordinate>();
        foreach (var terminal in terminals)
        {
            var route = routes.Routes.FirstOrDefault(item => item.PhysicalLinkId == terminal.PhysicalLinkId);
            if (route is null) continue;
            var run = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? runs.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.StartRouteIndex == 0)
                : runs.LastOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.EndRouteIndex == route.Cells.Count - 1);
            var node = placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == terminal.PhysicalNodeId);
            if (run is null || node is null) continue;
            if (run.Orientation != ArchitectureV7RunOrientation.Vertical)
            {
                diagnostics.Add(new("ENDPOINT-FINAL-VERTICAL-RUN-MISSING",
                    "A top/bottom endpoint has no final vertical approach run to receive its terminal-anchored X coordinate.", true,
                    terminal.PhysicalLinkId, run.RunId, terminal.PhysicalNodeId, terminal.EndpointKind.ToString()));
                continue;
            }
            result.Add(new(terminal.PhysicalLinkId, terminal.PhysicalNodeId, terminal.EndpointKind, run.RunId,
                terminal.RelativeOffset, "endpoint-local;terminal-authoritative;final-vertical-lane;source=bottom,destination=top")
            { SignedLaneOrdinal = terminal.SignedSlotOrdinal });
        }
        return result;
    }

    private static double LaneAxisOffset(ArchitectureV7StraightRun run, ArchitectureV7RunLaneAssignment assignment,
        IReadOnlyList<ArchitectureV7StraightRun> runs, IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments, int spacing)
    {
        return assignment.SignedLaneOrdinal * spacing;
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
        IEnumerable<ArchitectureV7TerminalSlotAssignment> terminals, IEnumerable<ArchitectureV7EndpointLaneCoordinate> endpointLaneCoordinates,
        IEnumerable<ArchitectureV7SharedVerticalRunConstraint> sharedVerticalRunConstraints, IEnumerable<ArchitectureV7EndpointApproachReservation> approaches,
        IEnumerable<ArchitectureV7EndpointHandoff> handoffs, IEnumerable<ArchitectureV7BendAllocation> bends, IEnumerable<ArchitectureV7CrossingAllocation> crossings,
        IEnumerable<ArchitectureV7AllocationDiagnostic> diagnostics, IEnumerable<ArchitectureV7EndpointZBend> endpointZBends) => placementFingerprint + "|" + routeFingerprint + "|" + string.Join(";", runs.Select(x => x.RunId + ":" + string.Join(",", x.Cells.Select(c => c.Row + "/" + c.Column))).Concat(assignments.Select(x => x.RunId + "=" + x.LaneId)).Concat(terminals.Select(x => x.PhysicalLinkId + ":" + x.EndpointKind + ":" + x.SlotOrdinal)).Concat(endpointLaneCoordinates.Select(x => x.PhysicalLinkId + ":e" + x.EndpointKind + ":" + x.RunId + ":" + x.RelativeXOffset)).Concat(sharedVerticalRunConstraints.Select(x => x.PhysicalLinkId + ":shared=" + x.RunId)).Concat(endpointZBends.Select(x => x.PhysicalLinkId + ":z" + x.EndpointKind + ":" + x.FixedRunId)).Concat(approaches.Select(x => x.PhysicalLinkId + ":a" + x.TerminalSlotOrdinal)).Concat(handoffs.Select(x => x.ResourceId + ":h" + x.RelativePhysicalOffset + ":" + x.RequiredClearance)).Concat(bends.Select(x => x.BendId + ":b" + x.EffectiveRelativePosition.XOffset + "/" + x.EffectiveRelativePosition.YOffset)).Concat(crossings.Select(x => x.CrossingId + ":c" + x.EffectiveRelativePosition.XOffset + "/" + x.EffectiveRelativePosition.YOffset + ":" + x.Classification)).Concat(diagnostics.Select(x => x.Code)));

    private static ArchitectureV7RunOrientation Orientation(ArchitectureV7RouteCell a, ArchitectureV7RouteCell b) => a.Row == b.Row ? ArchitectureV7RunOrientation.Horizontal : ArchitectureV7RunOrientation.Vertical;
    private static int FixedCoordinate(ArchitectureV7StraightRun run) => run.Orientation == ArchitectureV7RunOrientation.Horizontal ? run.Cells[0].Row : run.Cells[0].Column;
    private static int MinCoordinate(ArchitectureV7StraightRun run) => run.ConflictStart ?? run.Cells.Min(x => run.Orientation == ArchitectureV7RunOrientation.Horizontal ? x.Column : x.Row);
    private static int MaxCoordinate(ArchitectureV7StraightRun run) => run.ConflictEnd ?? run.Cells.Max(x => run.Orientation == ArchitectureV7RunOrientation.Horizontal ? x.Column : x.Row);
    private static int ParseOrdinal(string laneId) => int.Parse(laneId.Substring(laneId.LastIndexOf(':') + 1));
    private static int DirectionOrder(ArchitectureV7EndpointDirection direction) => direction switch { ArchitectureV7EndpointDirection.Left => 0, ArchitectureV7EndpointDirection.Down => 1, ArchitectureV7EndpointDirection.Right => 2, _ => 3 };
    private static ArchitectureV7EndpointDirection Direction(ArchitectureV7RouteCell near, ArchitectureV7RouteCell endpoint, bool source)
    {
        // Horizontal endpoint direction is the side of the node edge being
        // attached to; it is not the direction of travel.  The previous source
        // inversion classified a source leaving to the right as a left-origin,
        // corrupting endpoint grouping and handoff allocation.
        if (near.Column < endpoint.Column) return ArchitectureV7EndpointDirection.Left;
        if (near.Column > endpoint.Column) return ArchitectureV7EndpointDirection.Right;
        return source
            ? (near.Row < endpoint.Row ? ArchitectureV7EndpointDirection.Up : ArchitectureV7EndpointDirection.Down)
            : (near.Row < endpoint.Row ? ArchitectureV7EndpointDirection.Up : ArchitectureV7EndpointDirection.Down);
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
