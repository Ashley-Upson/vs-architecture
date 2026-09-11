using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6LaneAllocator
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly Dictionary<string, PhysicalNodePlacementMetadata> metadata;
    private readonly IReadOnlyList<PlannedGridRoute> routes;
    private readonly IReadOnlyList<PlannedStraightRun> runs;
    private readonly IReadOnlyList<NodeEndpointDemand> endpointDemands;
    private readonly IReadOnlyList<DestinationApproachReservation> approaches;
    private readonly IReadOnlyList<TerminalOrder> terminalOrders;
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();
    private readonly List<LaneAllocationConflict> conflicts = new();
    private long intervalComparisons;

    public ArchitectureV6LaneAllocator(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalLink> links,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<PlannedGridRoute> routes,
        IReadOnlyList<PlannedStraightRun> runs,
        IReadOnlyList<NodeEndpointDemand> endpointDemands,
        IReadOnlyList<DestinationApproachReservation> approaches,
        IReadOnlyList<TerminalOrder>? terminalOrders = null)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.links = links;
        this.placements = placements;
        this.metadata = metadata.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        this.routes = routes;
        this.runs = runs;
        this.endpointDemands = endpointDemands;
        this.approaches = approaches;
        this.terminalOrders = terminalOrders ?? Array.Empty<TerminalOrder>();
    }

    public ArchitectureLaneAllocationResult Build()
    {
        var timer = Stopwatch.StartNew();
        var horizontal = new List<PlannedLaneAllocation>();
        var vertical = new List<PlannedLaneAllocation>();
        var allocatedRuns = new List<PlannedStraightRun>();
        var runIds = new Dictionary<PlannedStraightRun, string>();
        var runAllocations = new Dictionary<PlannedStraightRun, PlannedLaneAllocation>();
        var groupedRuns = runs.GroupBy(run => run.Axis).ToArray();
        var domainCount = 0;
        var orderingEdgeCount = 0;
        foreach (var group in groupedRuns)
        {
            var domains = group.GroupBy(run => Domain(run)).OrderBy(group => group.Key, StringComparer.Ordinal);
            foreach (var domain in domains)
            {
                domainCount++;
                var orderedRuns = domain.OrderBy(run => RouteOrder(run.RouteId)).ThenBy(run => run.RouteId, StringComparer.Ordinal).ToArray();
                for (var left = 0; left < orderedRuns.Length; left++)
                    for (var right = left + 1; right < orderedRuns.Length; right++)
                        if (RawIntervalsOverlap(Interval(orderedRuns[left]), Interval(orderedRuns[right]))) orderingEdgeCount++;
                var domainAllocations = new List<PlannedLaneAllocation>();
                foreach (var run in orderedRuns)
                {
                    var runId = RunId(run);
                    runIds[run] = runId;
                    var interval = Interval(run);
                    var ordinal = 0;
                    while (domainAllocations.Any(existing => existing.Ordinal == ordinal && Overlaps(existing, interval))) ordinal++;
                    var lane = new LaneId($"{domain.Key}:lane:{ordinal}");
                    var allocation = new PlannedLaneAllocation(runId, run.RouteId, run.GridId, run.Axis, domain.Key, ordinal, lane,
                        run.Cells, interval.Start, interval.End, Topology(run.RouteId), Ownership(run.RouteId),
                        $"collective:{run.Axis}:{domain.Key}:{run.RouteId}");
                    domainAllocations.Add(allocation);
                    runAllocations[run] = allocation;
                    allocatedRuns.Add(run with { Lane = lane });
                    if (run.Axis == RouteAxis.Horizontal) horizontal.Add(allocation); else vertical.Add(allocation);
                }
            }
        }

        var endpointAllocations = AllocateEndpoints(runAllocations);
        var endpointHandoff = AllocateEndpointHandoffs(endpointAllocations, horizontal, vertical);
        endpointAllocations = endpointHandoff.UpdatedEndpoints;
        horizontal.AddRange(endpointHandoff.Horizontal);
        vertical.AddRange(endpointHandoff.Vertical);
        var approachAllocations = AllocateApproaches();
        var updatedRoutes = ApplyRunLanes(runAllocations);
        var turns = AllocateTurns(updatedRoutes, runAllocations);
        var crossings = AllocateCrossings(updatedRoutes, runAllocations);
        var transitions = AllocateTransitions(updatedRoutes);
        var expansion = BuildExpansionRequirements(endpointAllocations, approachAllocations);
        var constraints = BuildConstraints(horizontal, vertical, endpointAllocations, approachAllocations, turns, transitions, expansion);
        Validate(horizontal, vertical, endpointAllocations, approachAllocations, turns, crossings, transitions, updatedRoutes);
        foreach (var conflict in conflicts)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LaneAllocationConflict", conflict.Message, PlanningDiagnosticSubject.Lane, conflict.OwnerId));
        timer.Stop();
        var turnCellGroups = updatedRoutes.SelectMany(route => route.Steps.Where(step => step.Role == RouteStepRole.Turn)
            .Select(step => step.CellId)).GroupBy(cell => cell).ToArray();
        var rows = updatedRoutes.SelectMany(route => route.Steps).Select(step => step.CellId.RowId).Distinct().Select(id => new PlanningGridRow(id, 0, 1, 1, 1, 0, 0)).ToArray();
        var columns = updatedRoutes.SelectMany(route => route.Steps).Select(step => step.CellId.ColumnId).Distinct().Select(id => new PlanningGridColumn(id, 0, 1, 1, 1, 0, 0)).ToArray();
        var sizing = new GridTrackSizingPlan(rows, columns, constraints, null);
        return new ArchitectureLaneAllocationResult(updatedRoutes, allocatedRuns, horizontal, vertical, endpointAllocations,
            approachAllocations, turns, crossings, transitions, conflicts, expansion, sizing, diagnostics,
            new LaneAllocationPerformance(timer.ElapsedMilliseconds, domainCount, runs.Count, orderingEdgeCount, 0,
                turnCellGroups.Length, turnCellGroups.Select(group => group.Count()).DefaultIfEmpty(0).Max(),
                intervalComparisons, turns.Count));
    }

    private (IReadOnlyList<PlannedEndpointAllocation> UpdatedEndpoints,
        IReadOnlyList<PlannedLaneAllocation> Horizontal,
        IReadOnlyList<PlannedLaneAllocation> Vertical) AllocateEndpointHandoffs(
        IReadOnlyList<PlannedEndpointAllocation> endpoints,
        IReadOnlyList<PlannedLaneAllocation> existingHorizontal,
        IReadOnlyList<PlannedLaneAllocation> existingVertical)
    {
        var demands = endpoints.Select(endpoint =>
        {
            var route = routes.Single(item => item.PhysicalLinkId == endpoint.PhysicalLinkId);
            var steps = route.Steps
                .Where(step => step.Role is not RouteStepRole.SourceExit and not RouteStepRole.DestinationEntry)
                .OrderBy(step => step.Order).ToArray();
            var step = endpoint.Side == GridSide.Bottom ? steps.FirstOrDefault() : steps.LastOrDefault();
            if (step is null) return new EndpointHandoffDemand(endpoint, null, null, null, 0, 0, 0, 0);
            var node = placements.Single(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
            var endpointColumn = endpoint.TerminalColumnId is null
                ? ParseValue(node.CentreColumnId.Value)
                : ParseValue(endpoint.TerminalColumnId.Value.Value);
            var stepColumn = ParseValue(step.CellId.ColumnId.Value);
            var stepRow = ParseValue(step.CellId.RowId.Value);
            var nodeRow = ParseValue(node.AnchorCellId.RowId.Value);
            return new EndpointHandoffDemand(endpoint, step, endpointColumn, stepColumn,
                Math.Min(endpointColumn, stepColumn), Math.Max(endpointColumn, stepColumn),
                Math.Min(nodeRow, stepRow), Math.Max(nodeRow, stepRow));
        }).Where(item => item.Step is not null).ToArray();

        var horizontal = AllocateEndpointAxis(demands, RouteAxis.Horizontal, item => EndpointDomain(item.Step!, RouteAxis.Horizontal),
            item => (item.IntervalStart, item.IntervalEnd), existingHorizontal);
        var vertical = AllocateEndpointAxis(demands, RouteAxis.Vertical, item => EndpointDomain(item.Step!, RouteAxis.Vertical),
            item => (item.RowStart, item.RowEnd), existingVertical);
        var updated = endpoints.Select(endpoint =>
        {
            var route = routes.Single(item => item.PhysicalLinkId == endpoint.PhysicalLinkId);
            var orderedSteps = route.Steps
                .Where(item => item.Role is not RouteStepRole.SourceExit and not RouteStepRole.DestinationEntry)
                .OrderBy(item => item.Order)
                .ToArray();
            var step = endpoint.Side == GridSide.Bottom ? orderedSteps.FirstOrDefault() : orderedSteps.LastOrDefault();
            if (step is null) return endpoint;
            var lanes = horizontal.Concat(vertical)
                .Where(item => item.DomainId == EndpointDomain(step, item.Axis))
                .ToArray();
            return endpoint with
            {
                HandoffHorizontalLane = (endpoint.Side == GridSide.Bottom
                    ? lanes.Where(item => item.Axis == RouteAxis.Horizontal && item.RouteId == endpoint.PhysicalLinkId).OrderBy(item => item.Ordinal).FirstOrDefault()
                    : lanes.Where(item => item.Axis == RouteAxis.Horizontal && item.RouteId == endpoint.PhysicalLinkId).OrderByDescending(item => item.Ordinal).FirstOrDefault())?.Lane,
                HandoffVerticalLane = (endpoint.Side == GridSide.Bottom
                    ? lanes.Where(item => item.Axis == RouteAxis.Vertical && item.RouteId == endpoint.PhysicalLinkId).OrderBy(item => item.Ordinal).FirstOrDefault()
                    : lanes.Where(item => item.Axis == RouteAxis.Vertical && item.RouteId == endpoint.PhysicalLinkId).OrderByDescending(item => item.Ordinal).FirstOrDefault())?.Lane
            };
        }).ToArray();
        return (updated, horizontal, vertical);
    }

    private static string EndpointDomain(PlannedGridRouteStep step, RouteAxis axis) =>
        step.GridId.Value + ":" + axis + ":" +
        (axis == RouteAxis.Horizontal ? step.CellId.RowId.Value : step.CellId.ColumnId.Value);

    private IReadOnlyList<PlannedLaneAllocation> AllocateEndpointAxis(
        IReadOnlyList<EndpointHandoffDemand> demands, RouteAxis axis,
        Func<EndpointHandoffDemand, string> domainKey,
        Func<EndpointHandoffDemand, (int Start, int End)> interval,
        IReadOnlyList<PlannedLaneAllocation> existing)
    {
        var result = new List<PlannedLaneAllocation>();
        foreach (var domain in demands.GroupBy(domainKey, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var allocated = new List<PlannedLaneAllocation>();
            var baseOrdinal = existing.Where(item => item.Axis == axis && item.DomainId == domain.Key)
                .Select(item => item.Ordinal).DefaultIfEmpty(-1).Max() + 1 + (axis == RouteAxis.Vertical ? 1 : 0);
            foreach (var demand in domain.OrderBy(item => RouteOrder(item.Endpoint.PhysicalLinkId)).ThenBy(item => item.Endpoint.PhysicalLinkId, StringComparer.Ordinal))
            {
                var range = interval(demand);
                // Endpoint handoffs are separate physical demands even when
                // their logical intervals do not overlap. Keeping one
                // deterministic lane per demand prevents later terminal
                // geometry from collapsing distinct handoffs onto one track.
                var ordinal = baseOrdinal + allocated.Count;
                var lane = new LaneId($"endpoint:{axis}:{domain.Key}:lane:{ordinal}");
                var cell = demand.Step!.CellId;
                var allocation = new PlannedLaneAllocation(
                    $"endpoint:{axis}:{demand.Endpoint.PhysicalLinkId}:{demand.Endpoint.Side}",
                    demand.Endpoint.PhysicalLinkId, cell.GridId, axis, domain.Key, ordinal, lane,
                    new[] { cell }, range.Start, range.End, Topology(demand.Endpoint.PhysicalLinkId),
                    Ownership(demand.Endpoint.PhysicalLinkId), "collective:endpoint-handoff");
                allocated.Add(allocation);
                result.Add(allocation);
            }
        }
        return result;
    }

    private sealed record EndpointHandoffDemand(
        PlannedEndpointAllocation Endpoint,
        PlannedGridRouteStep? Step,
        int? EndpointColumn,
        int? StepColumn,
        int IntervalStart,
        int IntervalEnd,
        int RowStart,
        int RowEnd);

    private IReadOnlyList<PlannedEndpointAllocation> AllocateEndpoints(
        IReadOnlyDictionary<PlannedStraightRun, PlannedLaneAllocation> runAllocations)
    {
        var result = new List<PlannedEndpointAllocation>();
        foreach (var group in endpointDemands.GroupBy(demand => demand.Endpoint.PhysicalNodeId + ":" + demand.Endpoint.Side, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderBy(demand => terminalOrders.SingleOrDefault(item => item.PhysicalLinkId == demand.PhysicalLinkId &&
                    item.PhysicalNodeId == demand.Endpoint.PhysicalNodeId && item.Side == demand.Endpoint.Side)?.GlobalOrder ?? int.MaxValue)
                .ThenBy(demand => EndpointColumn(demand))
                .ThenBy(demand => EndpointPriority(demand))
                .ThenBy(demand => demand.PhysicalLinkId, StringComparer.Ordinal)
                .ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var offset = PortOffset(index, ordered.Length);
                var route = routes.Single(item => item.PhysicalLinkId == ordered[index].PhysicalLinkId);
                var verticalRunAllocations = runAllocations
                    .Where(item => item.Key.RouteId == route.PhysicalLinkId && item.Key.Axis == RouteAxis.Vertical)
                    .OrderBy(item => item.Key.Cells.Min(cell => route.Steps.First(step => step.CellId.Equals(cell)).Order))
                    .ToArray();
                var terminalRun = ordered[index].Endpoint.Side == GridSide.Bottom
                    ? verticalRunAllocations.FirstOrDefault()
                    : verticalRunAllocations.LastOrDefault();
                var terminalLane = terminalRun.Value?.Lane;
                PlanningGridColumnId? terminalColumn = terminalRun.Value is null
                    ? null
                    : (ordered[index].Endpoint.Side == GridSide.Bottom
                        ? terminalRun.Value.Cells.FirstOrDefault().ColumnId
                        : terminalRun.Value.Cells.LastOrDefault().ColumnId);
                result.Add(new PlannedEndpointAllocation(ordered[index].PhysicalLinkId, ordered[index].Endpoint.PhysicalNodeId,
                    ordered[index].Endpoint.Side, ordered[index].Endpoint, index, offset, group.Key,
                    $"endpoint:{group.Key}:{index}", terminalLane, terminalColumn));
            }
        }
        return result;
    }

    private IReadOnlyList<PlannedDestinationApproachAllocation> AllocateApproaches()
    {
        var result = new List<PlannedDestinationApproachAllocation>();
        foreach (var approach in approaches.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
        {
            var ordered = approach.PhysicalLinkIds ?? Array.Empty<string>();
            var linksForNode = ordered
                .OrderBy(id => OtherEndpointColumn(id, GridSide.Top))
                .ThenBy(id => IsDirectDownward(id) ? 0 : 1)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray();
            for (var index = 0; index < linksForNode.Length; index++)
            {
                var offset = PortOffset(index, linksForNode.Length);
                result.Add(new PlannedDestinationApproachAllocation(approach.ReservationId, approach.PhysicalNodeId, linksForNode[index],
                    approach.GridId, index, offset, $"approach:{approach.PhysicalNodeId}", $"approach:{approach.ReservationId}:{index}"));
            }
        }
        return result;
    }

    private IReadOnlyList<PlannedGridRoute> ApplyRunLanes(IReadOnlyDictionary<PlannedStraightRun, PlannedLaneAllocation> runAllocations)
    {
        return routes.Select(route => route with
        {
            Steps = route.Steps.Select(step =>
            {
                // A turn cell is intentionally shared by the horizontal and
                // vertical runs that meet there. Ordinary steps must bind to
                // their traversal axis; selecting the first containing run
                // can silently bind a horizontal step to a vertical lane (or
                // vice versa). Turns retain both authoritative identities in
                // PlannedTurnAllocation; the single step lane is only the
                // deterministic lane for the side represented by the step.
                var candidates = runs.Where(candidate => candidate.RouteId == route.PhysicalLinkId &&
                    candidate.GridId.Equals(step.GridId) && candidate.Cells.Contains(step.CellId));
                var run = step.Role == RouteStepRole.Turn
                    // The turn's complete horizontal/vertical identity is
                    // retained by AllocateTurns. Keep the existing primary
                    // lane field deterministic for consumers that only
                    // understand one lane on a route step.
                    ? candidates.FirstOrDefault()
                    : candidates
                        .Where(candidate => candidate.Axis == StepAxis(step))
                        .OrderBy(candidate => candidate.StartTransition, StringComparer.Ordinal)
                        .ThenBy(candidate => candidate.EndTransition, StringComparer.Ordinal)
                        .FirstOrDefault();
                if (run is null || !runAllocations.TryGetValue(run, out var allocation)) return step;
                return step with { AllocatedLane = allocation.Lane, StraightRunId = allocation.RunId };
            }).ToArray()
        }).ToArray();
    }

    private static RouteAxis StepAxis(PlannedGridRouteStep step) =>
        step.Role == RouteStepRole.Turn
            ? step.ExitSide is GridSide.Left or GridSide.Right ? RouteAxis.Horizontal : RouteAxis.Vertical
            : step.EntrySide is GridSide.Left or GridSide.Right || step.ExitSide is GridSide.Left or GridSide.Right
                ? RouteAxis.Horizontal
                : RouteAxis.Vertical;

    private IReadOnlyList<PlannedTurnAllocation> AllocateTurns(IReadOnlyList<PlannedGridRoute> updatedRoutes, IReadOnlyDictionary<PlannedStraightRun, PlannedLaneAllocation> runAllocations)
    {
        var result = new List<PlannedTurnAllocation>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in updatedRoutes)
            foreach (var indexedStep in route.Steps.Select((step, index) => (step, index)).Where(item => item.step.Role == RouteStepRole.Turn))
            {
                var step = indexedStep.step;
                var horizontal = FindTurnRun(route, indexedStep.index, RouteAxis.Horizontal);
                var vertical = FindTurnRun(route, indexedStep.index, RouteAxis.Vertical);
                var horizontalId = horizontal is null ? "none" : runAllocations[horizontal].Lane.Value;
                var verticalId = vertical is null ? "none" : runAllocations[vertical].Lane.Value;
                var identity = step.GridId.Value + ":" + step.CellId + ":" + horizontalId + ":" + verticalId;
                if (!identities.Add(identity))
                {
                    conflicts.Add(new LaneAllocationConflict(LaneAllocationConflictKind.TurnCongestion, identity, 2,
                        "Two turns require the same logical bend identity.", new[] { route.PhysicalLinkId }));
                    continue;
                }
                result.Add(new PlannedTurnAllocation(route.PhysicalLinkId, step.CellId.ToString(), step.EntrySide, step.ExitSide,
                    horizontal is null ? null : runAllocations[horizontal].RunId, vertical is null ? null : runAllocations[vertical].RunId, identity, 0,
                    $"turn:{identity}:{route.PhysicalLinkId}"));
            }
        foreach (var route in updatedRoutes)
        {
            var turnSteps = route.Steps.Where(step => step.Role == RouteStepRole.Turn).OrderBy(step => step.Order).ToArray();
            var routeTurns = result.Where(turn => turn.RouteId == route.PhysicalLinkId).ToArray();
            for (var index = 0; index + 1 < Math.Min(turnSteps.Length, routeTurns.Length); index++)
            {
                if (turnSteps[index + 1].Order != turnSteps[index].Order + 1) continue;
                var firstIsHorizontal = turnSteps[index].ExitSide is GridSide.Left or GridSide.Right;
                var secondIsHorizontal = turnSteps[index + 1].EntrySide is GridSide.Left or GridSide.Right;
                if (firstIsHorizontal != secondIsHorizontal) continue;

                var first = routeTurns[index];
                var second = routeTurns[index + 1];
                var normalized = firstIsHorizontal
                    ? second with { HorizontalRunId = first.HorizontalRunId }
                    : second with { VerticalRunId = first.VerticalRunId };
                var resultIndex = result.FindIndex(item => ReferenceEquals(item, second));
                if (resultIndex >= 0) result[resultIndex] = normalized;
            }
        }
        return result
            .GroupBy(turn => turn.RouteId + ":" + turn.CellId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(turn => turn.BendIdentity, StringComparer.Ordinal).First())
            .ToList();
    }

    private PlannedStraightRun? FindTurnRun(PlannedGridRoute route, int turnIndex, RouteAxis axis)
    {
        var candidates = runs.Where(run => run.RouteId == route.PhysicalLinkId && run.Axis == axis).ToArray();
        var adjacentTurn = turnIndex > 0 && route.Steps[turnIndex - 1].Role == RouteStepRole.Turn
            ? route.Steps[turnIndex - 1]
            : turnIndex + 1 < route.Steps.Count && route.Steps[turnIndex + 1].Role == RouteStepRole.Turn
                ? route.Steps[turnIndex + 1]
                : null;
        if (adjacentTurn is not null)
        {
            var shared = candidates.FirstOrDefault(run => run.Cells.Count == 2 &&
                run.Cells.Contains(route.Steps[turnIndex].CellId) && run.Cells.Contains(adjacentTurn.CellId));
            if (shared is not null) return shared;
        }
        var containing = candidates.FirstOrDefault(run => run.Cells.Contains(route.Steps[turnIndex].CellId));
        if (containing is not null) return containing;

        var orderedCells = route.Steps.Select((step, index) => (step.CellId, index)).ToArray();
        return candidates
            .Select(run => new
            {
                Run = run,
                Distance = run.Cells
                    .Select(cell => orderedCells.Where(item => item.CellId.Equals(cell)).Select(item => Math.Abs(item.index - turnIndex)).DefaultIfEmpty(int.MaxValue).Min())
                    .DefaultIfEmpty(int.MaxValue)
                    .Min()
            })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Run.Lane.Value, StringComparer.Ordinal)
            .Select(item => item.Run)
            .FirstOrDefault();
    }

    private IReadOnlyList<PlannedCleanCrossing> AllocateCrossings(IReadOnlyList<PlannedGridRoute> updatedRoutes, IReadOnlyDictionary<PlannedStraightRun, PlannedLaneAllocation> runAllocations)
    {
        var result = new List<PlannedCleanCrossing>();
        var cells = runs.SelectMany(run => run.Cells.Select(cell => (run, cell))).GroupBy(item => item.cell);
        foreach (var cell in cells)
        {
            var horizontal = cell.Where(item => item.run.Axis == RouteAxis.Horizontal).Select(item => item.run).Distinct().ToArray();
            var vertical = cell.Where(item => item.run.Axis == RouteAxis.Vertical).Select(item => item.run).Distinct().ToArray();
            if (horizontal.Length == 0 || vertical.Length == 0) continue;
            foreach (var h in horizontal)
                foreach (var v in vertical)
                {
                    var hStep = updatedRoutes.Single(route => route.PhysicalLinkId == h.RouteId).Steps
                        .FirstOrDefault(step => step.CellId.Equals(cell.Key));
                    var vStep = updatedRoutes.Single(route => route.PhysicalLinkId == v.RouteId).Steps
                        .FirstOrDefault(step => step.CellId.Equals(cell.Key));
                    if (hStep is not null && vStep is not null && hStep.Role != RouteStepRole.Turn && vStep.Role != RouteStepRole.Turn)
                        result.Add(new PlannedCleanCrossing(cell.Key.ToString(), runAllocations[h].RunId, runAllocations[v].RunId,
                            $"crossing:{cell.Key}", h.RouteId, v.RouteId));
                }
        }
        return result;
    }

    private IReadOnlyList<PlannedProjectTransitionAllocation> AllocateTransitions(IReadOnlyList<PlannedGridRoute> updatedRoutes)
    {
        return updatedRoutes.SelectMany(route => route.Transitions.Select(transition => (route, transition)))
            .GroupBy(item => item.transition.SourceGridId.Value + ":" + item.transition.DestinationGridId.Value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(item => item.route.PhysicalLinkId, StringComparer.Ordinal).Select((item, index) =>
                new PlannedProjectTransitionAllocation(item.route.PhysicalLinkId, item.transition.SourceGridId, item.transition.DestinationGridId,
                    group.Key, index, new LaneId($"transition:{group.Key}:lane:{index}"), $"transition:{group.Key}:{item.route.PhysicalLinkId}")))
            .ToArray();
    }

    private IReadOnlyList<NodeFootprintExpansionRequirement> BuildExpansionRequirements(
        IReadOnlyList<PlannedEndpointAllocation> endpoints,
        IReadOnlyList<PlannedDestinationApproachAllocation> approachesAllocated)
    {
        var result = new List<NodeFootprintExpansionRequirement>();
        foreach (var group in endpoints.GroupBy(item => item.PhysicalNodeId + ":" + item.Side, StringComparer.Ordinal))
        {
            var nodeId = group.First().PhysicalNodeId;
            var placement = placements.Single(item => item.PhysicalNodeId == nodeId);
            var required = ArchitectureV6TerminalCapacity.RequiredOddSpan(request, group.Count());
            if (required > placement.ColumnSpan)
            {
                var linksForNode = group.Select(item => item.PhysicalLinkId).Distinct(StringComparer.Ordinal).ToArray();
                result.Add(new NodeFootprintExpansionRequirement(nodeId, placement.ColumnSpan, required, group.Count(),
                    "Endpoint demand exceeds the current logical footprint span.", linksForNode));
                conflicts.Add(new LaneAllocationConflict(LaneAllocationConflictKind.InsufficientEndpointSpan, nodeId, group.Count(),
                    "Endpoint demand requires a larger odd logical footprint in a later reconciliation stage.", linksForNode));
            }
        }
        return result;
    }

    private IReadOnlyList<GridTrackConstraint> BuildConstraints(
        IReadOnlyList<PlannedLaneAllocation> horizontal,
        IReadOnlyList<PlannedLaneAllocation> vertical,
        IReadOnlyList<PlannedEndpointAllocation> endpoints,
        IReadOnlyList<PlannedDestinationApproachAllocation> approachesAllocated,
        IReadOnlyList<PlannedTurnAllocation> turns,
        IReadOnlyList<PlannedProjectTransitionAllocation> transitions,
        IReadOnlyList<NodeFootprintExpansionRequirement> expansion)
    {
        var constraints = new List<GridTrackConstraint>();
        foreach (var group in horizontal.GroupBy(item => item.DomainId, StringComparer.Ordinal))
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.HorizontalLaneEnvelope, group.First().GridId,
                group.SelectMany(item => item.Cells).Select(cell => cell.RowId).Distinct().ToArray(), Array.Empty<PlanningGridColumnId>(),
                group.Max(item => item.Ordinal) + 1, $"horizontal lanes:{string.Join(",", group.Select(item => item.RouteId).Distinct(StringComparer.Ordinal))};spacing={request.NodePlacement.HorizontalSpacing}", group.Key));
        foreach (var group in vertical.GroupBy(item => item.DomainId, StringComparer.Ordinal))
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.VerticalLaneEnvelope, group.First().GridId, Array.Empty<PlanningGridRowId>(),
                group.SelectMany(item => item.Cells).Select(cell => cell.ColumnId).Distinct().ToArray(), group.Max(item => item.Ordinal) + 1,
                $"vertical lanes:{string.Join(",", group.Select(item => item.RouteId).Distinct(StringComparer.Ordinal))};spacing={request.NodePlacement.VerticalSpacing}", group.Key));
        foreach (var group in endpoints.GroupBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.MultiColumnSpanMinimum, placements.Single(item => item.PhysicalNodeId == group.Key).GridId,
                Array.Empty<PlanningGridRowId>(), placements.Single(item => item.PhysicalNodeId == group.Key).Footprint.Select(cell => cell.ColumnId).ToArray(),
                group.Count(), $"endpoint slots:{string.Join(",", group.Select(item => item.PhysicalLinkId))}", group.Key));
        foreach (var group in approachesAllocated.GroupBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.VerticalLaneEnvelope, group.First().GridId, group.SelectMany(item => approaches.Single(item2 => item2.ReservationId == item.ReservationId).Cells).Select(cell => cell.RowId).Distinct().ToArray(), Array.Empty<PlanningGridColumnId>(),
                group.Count(), $"destination approach lanes:{string.Join(",", group.Select(item => item.PhysicalLinkId))}", group.Key));
        foreach (var group in turns.Select(turn => routes.Single(route => route.PhysicalLinkId == turn.RouteId).Steps
                     .First(step => step.CellId.ToString() == turn.CellId))
                 .GroupBy(step => step.GridId.Value + ":" + step.CellId, StringComparer.Ordinal))
        {
            var turnStep = group.First();
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.TurnClearance, turnStep.GridId,
                new[] { turnStep.CellId.RowId }, new[] { turnStep.CellId.ColumnId }, group.Count(),
                $"turn capacity:{turnStep.CellId};turns={group.Count()}", group.Key));
        }
        foreach (var transition in transitions)
        {
            var transitionCells = routes.Single(route => route.PhysicalLinkId == transition.PhysicalLinkId).Steps
                .Where(step => step.GridId.Equals(transition.SourceGridId)).Select(step => step.CellId).ToArray();
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, transition.SourceGridId,
                transitionCells.Select(cell => cell.RowId).Distinct().ToArray(), transitionCells.Select(cell => cell.ColumnId).Distinct().ToArray(),
                transition.Ordinal + 1, transition.Provenance, transition.PhysicalLinkId));
        }
        foreach (var item in expansion)
            constraints.Add(new GridTrackConstraint(TrackConstraintKind.MultiColumnSpanMinimum, placements.Single(placement => placement.PhysicalNodeId == item.PhysicalNodeId).GridId,
                Array.Empty<PlanningGridRowId>(), placements.Single(placement => placement.PhysicalNodeId == item.PhysicalNodeId).Footprint.Select(cell => cell.ColumnId).ToArray(), item.RequiredOddSpan, item.Reason, item.PhysicalNodeId));
        return constraints;
    }

    private void Validate(
        IReadOnlyList<PlannedLaneAllocation> horizontal,
        IReadOnlyList<PlannedLaneAllocation> vertical,
        IReadOnlyList<PlannedEndpointAllocation> endpoints,
        IReadOnlyList<PlannedDestinationApproachAllocation> approachesAllocated,
        IReadOnlyList<PlannedTurnAllocation> turns,
        IReadOnlyList<PlannedCleanCrossing> crossings,
        IReadOnlyList<PlannedProjectTransitionAllocation> transitions,
        IReadOnlyList<PlannedGridRoute> updatedRoutes)
    {
        if (horizontal.Concat(vertical).GroupBy(item => item.RunId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LaneAllocationRunCardinality", "Every straight run must receive exactly one lane allocation.", PlanningDiagnosticSubject.Lane, null));
        if (endpoints.GroupBy(item => item.DomainId + ":" + item.LogicalOrder, StringComparer.Ordinal).Any(group => group.Count() != 1))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("EndpointAllocationCollision", "Endpoint allocations must be unique within a node-side domain.", PlanningDiagnosticSubject.PhysicalNode, null));
        if (turns.GroupBy(item => item.BendIdentity, StringComparer.Ordinal).Any(group => group.Count() != 1))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("TurnAllocationCollision", "Turn bend identities must be unique.", PlanningDiagnosticSubject.Cell, null));
        if (crossings.Any(crossing => turns.Any(turn => turn.CellId == crossing.CellId)))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("CleanCrossingTurnConflict", "A clean crossing cannot share a cell with a turn.", PlanningDiagnosticSubject.Cell, null));
        if (updatedRoutes.Count != links.Count)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LaneAllocationRouteAccounting", "Lane allocation must preserve every abstract route.", PlanningDiagnosticSubject.PhysicalLink, null));
    }

    private string Domain(PlannedStraightRun run) => run.GridId.Value + ":" + run.Axis + ":" + (run.Axis == RouteAxis.Horizontal ? run.Cells.First().RowId.Value : run.Cells.First().ColumnId.Value);
    private string RunId(PlannedStraightRun run) => run.RouteId + ":" + run.Axis + ":" + run.Cells.First().ToString();
    private string Ownership(string routeId) => metadata.TryGetValue(routes.Single(route => route.PhysicalLinkId == routeId).Source.PhysicalNodeId, out var item) ? item.OwnershipGroup : "";
    private RouteTopologyFamily Topology(string routeId) => routes.Single(route => route.PhysicalLinkId == routeId).TopologyFamily;
    private int RouteOrder(string routeId) => links.Select((link, index) => (link.PhysicalLinkId, index)).FirstOrDefault(item => item.PhysicalLinkId == routeId).index;
    private bool IsDirectDownward(string physicalLinkId) => Topology(physicalLinkId) == RouteTopologyFamily.AdjacentDownward;
    private int EndpointPriority(NodeEndpointDemand demand) => IsDirectDownward(demand.PhysicalLinkId) ? 0 : Topology(demand.PhysicalLinkId) == RouteTopologyFamily.External ? 1 : 2;

    private int EndpointColumn(NodeEndpointDemand demand)
    {
        return RouteColumn(demand.PhysicalLinkId, demand.Endpoint.Side);
    }

    private int OtherEndpointColumn(string physicalLinkId, GridSide side)
    {
        return RouteColumn(physicalLinkId, side);
    }

    private int RouteColumn(string physicalLinkId, GridSide side)
    {
        var route = routes.SingleOrDefault(item => item.PhysicalLinkId == physicalLinkId);
        if (route is null) return int.MaxValue;
        var steps = route.Steps.Where(step => step.Role is RouteStepRole.HorizontalPassThrough or
            RouteStepRole.VerticalPassThrough or RouteStepRole.Turn or RouteStepRole.ProjectExit or
            RouteStepRole.ProjectEntry).OrderBy(step => step.Order).ToArray();
        var step = side == GridSide.Bottom ? steps.FirstOrDefault() : steps.LastOrDefault();
        return step is null ? int.MaxValue : ParseValue(step.CellId.ColumnId.Value);
    }
    private static int PortOffset(int index, int count)
    {
        if (count <= 1) return 0;
        var half = count / 2;
        return count % 2 == 1
            ? index - half
            : index < half ? index - half : index - half + 1;
    }
    private static (int Start, int End) Interval(PlannedStraightRun run)
    {
        var values = run.Cells.Select(cell => run.Axis == RouteAxis.Horizontal ? cell.ColumnId.Value : cell.RowId.Value).Select(ParseValue).ToArray();
        return (values.Min(), values.Max());
    }
    private static int ParseValue(string value)
    {
        if (int.TryParse(value.Substring(value.LastIndexOf(':') + 1), out var result)) return result;
        unchecked
        {
            var hash = 17;
            foreach (var character in value) hash = hash * 31 + character;
            return hash & int.MaxValue;
        }
    }
    private bool Overlaps(PlannedLaneAllocation existing, (int Start, int End) interval)
    {
        intervalComparisons++;
        return RawIntervalsOverlap((existing.IntervalStart, existing.IntervalEnd), interval);
    }
    private static bool RawIntervalsOverlap((int Start, int End) left, (int Start, int End) right) =>
        left.Start <= right.End + 1 && right.Start <= left.End + 1;
}
