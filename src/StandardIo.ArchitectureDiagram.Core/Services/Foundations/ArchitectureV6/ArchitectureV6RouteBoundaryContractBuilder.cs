using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6RouteBoundaryContractBuilder
{
    private readonly ArchitectureLaneAllocationResult allocation;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;

    public ArchitectureV6RouteBoundaryContractBuilder(
        ArchitectureLaneAllocationResult allocation,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements)
    {
        this.allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements));
    }

    public ArchitectureRouteBoundaryValidationResult Build()
    {
        var contracts = new List<PlannedRouteBoundaryContract>();
        var findings = new List<RouteBoundaryContractFinding>();
        foreach (var route in allocation.Routes.OrderBy(route => route.PhysicalLinkId, StringComparer.Ordinal))
        {
            var contract = BuildRoute(route);
            contracts.Add(contract);
            findings.AddRange(contract.Findings);
        }
        return new ArchitectureRouteBoundaryValidationResult(contracts, findings);
    }

    private PlannedRouteBoundaryContract BuildRoute(PlannedGridRoute route)
    {
        var components = new List<PlannedRouteComponentContract>();
        var routeFindings = new List<RouteBoundaryContractFinding>();
        var ordered = route.Steps.OrderBy(step => step.Order).ToArray();
        var sourceBoundary = EndpointBoundary(route, route.Source, GridSide.Bottom, "source-terminal");
        var destinationBoundary = EndpointBoundary(route, route.Destination, GridSide.Top, "destination-terminal");

        var sourceTerminalId = route.PhysicalLinkId + ":source-terminal";
        components.Add(new PlannedRouteComponentContract(sourceTerminalId, route.PhysicalLinkId,
            PlannedRouteComponentKind.SourceTerminal, -1, Array.Empty<PlanningGridCellId>(), sourceBoundary, sourceBoundary,
            null, GridSide.Bottom, null, null, null, Ownership(route.Source), route.PhysicalLinkId + ":source-node-anchor", null,
            "allocated source endpoint"));

        var routeComponents = ordered.Where(IsOrdinaryRouteStep).ToArray();
        var first = routeComponents.FirstOrDefault();
        var firstEntry = first is null ? null : StepBoundary(first, first.EntrySide, first.AllocatedLane, "source-departure-entry");
        var sourceAnchor = ordered.FirstOrDefault(step => step.Role == RouteStepRole.SourceExit &&
            step.CellId.Equals(placements.SingleOrDefault(item => item.PhysicalNodeId == route.Source.PhysicalNodeId)?.AnchorCellId));
        var sourceAnchorId = route.PhysicalLinkId + ":source-node-anchor";
        components.Add(new PlannedRouteComponentContract(sourceAnchorId, route.PhysicalLinkId,
            PlannedRouteComponentKind.SourceNodeAnchor, -0,
            sourceAnchor is null ? Array.Empty<PlanningGridCellId>() : new[] { sourceAnchor.CellId },
            sourceBoundary, sourceBoundary, GridSide.Bottom, GridSide.Bottom, null, null, null,
            Ownership(route.Source), sourceTerminalId, route.PhysicalLinkId + ":source-departure",
            "source physical node anchor endpoint cell"));
        if (sourceAnchor is null)
            Add(routeFindings, route, "MissingSourceNodeAnchor", sourceAnchorId, null,
                "A route must explicitly contain its source physical node anchor cell.", sourceBoundary, null);
        var departureId = route.PhysicalLinkId + ":source-departure";
        components.Add(new PlannedRouteComponentContract(departureId, route.PhysicalLinkId,
            PlannedRouteComponentKind.SourceDeparture, 1, first is null ? Array.Empty<PlanningGridCellId>() : new[] { first.CellId },
            sourceBoundary, firstEntry, GridSide.Bottom, first?.EntrySide, first?.AllocatedLane, null, null,
            Ownership(route.Source), sourceAnchorId, null, "source exterior departure from node anchor to first route cell"));
        if (first is null)
            Add(routeFindings, route, "MissingFirstRouteStep", departureId, null, "A source departure has no first routing component.", null, null);
        else if (sourceBoundary is null || sourceBoundary.Side != GridSide.Bottom)
            Add(routeFindings, route, "SourceDepartureNotBottomFacing", departureId, null,
                "The source departure does not expose the required bottom-facing node boundary.", sourceBoundary, firstEntry);

        var groups = GroupSteps(routeComponents);
        PlannedRouteComponentContract? previousRun = null;
        foreach (var group in groups)
        {
            var component = BuildStepComponent(route, group, routeFindings, previousRun);
            components.Add(component);
            if (component.Kind is PlannedRouteComponentKind.HorizontalStraightRun or PlannedRouteComponentKind.VerticalStraightRun)
                previousRun = component;
        }

        InsertAdjacentTurnRuns(route, components, routeFindings);

        var last = routeComponents.LastOrDefault();
        var lastComponent = components.LastOrDefault();
        var lastExit = lastComponent?.ExitBoundary;
        var approachId = route.PhysicalLinkId + ":destination-approach";
        components.Add(new PlannedRouteComponentContract(approachId, route.PhysicalLinkId,
            PlannedRouteComponentKind.DestinationApproach, int.MaxValue - 1,
            last is null ? Array.Empty<PlanningGridCellId>() : new[] { last.CellId }, lastExit, destinationBoundary,
            lastComponent?.ExitSide, GridSide.Top, lastComponent?.Lane, lastComponent?.RunId, null,
            Ownership(route.Destination), components.LastOrDefault()?.ComponentId, route.PhysicalLinkId + ":destination-terminal",
            "derived from destination approach and terminal endpoint"));
        if (last is null)
            Add(routeFindings, route, "MissingDestinationApproach", approachId, null, "A destination approach has no final route step.", null, null);
        else if (destinationBoundary is null || destinationBoundary.Side != GridSide.Top)
            Add(routeFindings, route, "DestinationApproachNotTopFacing", approachId, null,
                "The destination approach does not expose the required top-facing node boundary.", lastExit, destinationBoundary);

        var destinationAnchor = ordered.LastOrDefault(step => step.Role == RouteStepRole.DestinationEntry &&
            step.CellId.Equals(placements.SingleOrDefault(item => item.PhysicalNodeId == route.Destination.PhysicalNodeId)?.AnchorCellId));
        var destinationAnchorId = route.PhysicalLinkId + ":destination-node-anchor";
        components.Add(new PlannedRouteComponentContract(destinationAnchorId, route.PhysicalLinkId,
            PlannedRouteComponentKind.DestinationNodeAnchor, int.MaxValue,
            destinationAnchor is null ? Array.Empty<PlanningGridCellId>() : new[] { destinationAnchor.CellId },
            destinationBoundary, destinationBoundary, GridSide.Top, GridSide.Top, null, null, null,
            Ownership(route.Destination), approachId, route.PhysicalLinkId + ":destination-terminal",
            "destination physical node anchor endpoint cell"));
        if (destinationAnchor is null)
            Add(routeFindings, route, "MissingDestinationNodeAnchor", destinationAnchorId, null,
                "A route must explicitly contain its destination physical node anchor cell.", destinationBoundary, null);

        var destinationTerminalId = route.PhysicalLinkId + ":destination-terminal";
        components.Add(new PlannedRouteComponentContract(destinationTerminalId, route.PhysicalLinkId,
            PlannedRouteComponentKind.DestinationTerminal, int.MaxValue, Array.Empty<PlanningGridCellId>(), destinationBoundary, destinationBoundary,
            GridSide.Top, null, null, null, null, Ownership(route.Destination), approachId, null,
            "allocated destination endpoint"));

        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            components[index] = component with
            {
                PrecedingComponentId = index == 0 ? null : components[index - 1].ComponentId,
                FollowingComponentId = index == components.Count - 1 ? null : components[index + 1].ComponentId
            };
        }

        for (var index = 0; index < components.Count - 1; index++)
        {
            var before = components[index];
            var after = components[index + 1];
            if (before.ExitBoundary is null || after.EntryBoundary is null)
            {
                Add(routeFindings, route, "MissingComponentBoundary", before.ComponentId, after.ComponentId,
                    "Adjacent components do not both expose an authoritative boundary.", before.ExitBoundary, after.EntryBoundary);
            }
            else if (TryCanonicalize(route, before, after, out var canonical))
            {
                components[index] = before with { ExitBoundary = canonical };
                components[index + 1] = after with { EntryBoundary = canonical };
            }
            else if (route.Transitions.Any(transition =>
                transition.SourceGridId == before.ExitBoundary!.GridId &&
                transition.DestinationGridId == after.EntryBoundary!.GridId))
            {
                // Cross-grid continuity is materialised by the physical scene
                // compiler from one shared transition boundary. The ordinary
                // component contract cannot canonicalise two grid authorities
                // into a single cell boundary.
            }
            else if (route.Transitions.Count >= 2 &&
                route.Transitions[0].SourceGridId == before.ExitBoundary!.GridId &&
                route.Transitions.Last().DestinationGridId == after.EntryBoundary!.GridId)
            {
                // The physical compiler materialises the complete chained
                // handoff through the diagram grid. There is no direct
                // project-to-project cell boundary to canonicalise here.
            }
            else
            {
                Add(routeFindings, route, "GenuineBoundaryDiscontinuity", before.ComponentId, after.ComponentId,
                    "Adjacent components do not expose adjacent cells with compatible sides, lanes and ownership.", before.ExitBoundary, after.EntryBoundary);
            }
        }

        ValidateComponentGrammar(route, components, routeFindings);

        return new PlannedRouteBoundaryContract(route.PhysicalLinkId, route.TopologyFamily, components,
            routeFindings, routeFindings.Count == 0, "boundary contract derived from allocated cells, sides and lanes");
    }

    private void InsertAdjacentTurnRuns(PlannedGridRoute route,
        List<PlannedRouteComponentContract> components,
        List<RouteBoundaryContractFinding> routeFindings)
    {
        for (var index = 1; index < components.Count; index++)
        {
            var first = components[index - 1];
            var second = components[index];
            if (first.Kind != PlannedRouteComponentKind.Turn || second.Kind != PlannedRouteComponentKind.Turn)
                continue;

            var firstCell = first.Cells.Single();
            var secondCell = second.Cells.Single();
            var run = allocation.StraightRuns.FirstOrDefault(candidate => candidate.RouteId == route.PhysicalLinkId &&
                candidate.Cells.Count == 2 && candidate.Cells.Contains(firstCell) && candidate.Cells.Contains(secondCell));
            if (run is null)
            {
                Add(routeFindings, route, "MissingAdjacentTurnRun", first.ComponentId, second.ComponentId,
                    "Adjacent turn cells have no allocated one-cell straight run.", first.ExitBoundary, second.EntryBoundary);
                continue;
            }

            var allocationForLane = allocation.HorizontalLanes.Concat(allocation.VerticalLanes)
                .SingleOrDefault(item => item.Lane == run.Lane && item.RouteId == route.PhysicalLinkId);
            var entry = first.ExitBoundary is null ? null : new GridBoundaryIdentity(
                first.ExitBoundary.GridId, first.ExitBoundary.CellId, first.ExitBoundary.Side,
                run.Lane, first.ExitBoundary.OwnershipScope, "one-cell-run-entry");
            var exit = second.EntryBoundary is null ? null : new GridBoundaryIdentity(
                second.EntryBoundary.GridId, second.EntryBoundary.CellId, second.EntryBoundary.Side,
                run.Lane, second.EntryBoundary.OwnershipScope, "one-cell-run-exit");
            var kind = run.Axis == RouteAxis.Horizontal
                ? PlannedRouteComponentKind.HorizontalStraightRun
                : PlannedRouteComponentKind.VerticalStraightRun;
            var component = new PlannedRouteComponentContract(
                route.PhysicalLinkId + ":one-cell-run:" + first.Order,
                route.PhysicalLinkId,
                kind,
                first.Order,
                run.Cells,
                entry,
                exit,
                first.ExitSide,
                second.EntrySide,
                run.Lane,
                allocationForLane?.RunId ?? run.RouteId + ":one-cell-run",
                null,
                run.GridId.Value,
                null,
                null,
                "one-cell straight run across adjacent turn-cell boundary");
            components.Insert(index, component);
            index++;
        }
    }

    private static void ValidateComponentGrammar(PlannedGridRoute route,
        IReadOnlyList<PlannedRouteComponentContract> components,
        List<RouteBoundaryContractFinding> findings)
    {
        for (var index = 0; index + 1 < components.Count; index++)
        {
            var before = components[index];
            var after = components[index + 1];
            if (before.Kind == PlannedRouteComponentKind.Turn && after.Kind == PlannedRouteComponentKind.Turn)
            {
                Add(findings, route, "AdjacentTurnComponents", before.ComponentId, after.ComponentId,
                    "Distinct adjacent turn cells must be separated by a one-cell straight run.", before.ExitBoundary, after.EntryBoundary);
            }

            if (IsHorizontalRun(before) && IsVerticalRun(after) || IsVerticalRun(before) && IsHorizontalRun(after))
            {
                Add(findings, route, "MissingTurnBetweenRuns", before.ComponentId, after.ComponentId,
                    "Straight runs of opposite orientation require an intervening turn.", before.ExitBoundary, after.EntryBoundary);
            }
        }
    }

    private static bool IsHorizontalRun(PlannedRouteComponentContract component) =>
        component.Kind == PlannedRouteComponentKind.HorizontalStraightRun;

    private static bool IsVerticalRun(PlannedRouteComponentContract component) =>
        component.Kind == PlannedRouteComponentKind.VerticalStraightRun;

    private PlannedRouteComponentContract BuildStepComponent(PlannedGridRoute route,
        IReadOnlyList<PlannedGridRouteStep> group,
        List<RouteBoundaryContractFinding> routeFindings,
        PlannedRouteComponentContract? previousRun)
    {
        var first = group[0];
        var last = group[group.Count - 1];
        var turn = first.Role == RouteStepRole.Turn
            ? allocation.Turns.FirstOrDefault(item => item.RouteId == route.PhysicalLinkId && item.CellId == first.CellId.ToString())
            : null;
        var kind = first.Role == RouteStepRole.Turn
            ? PlannedRouteComponentKind.Turn
            : IsHorizontal(first) ? PlannedRouteComponentKind.HorizontalStraightRun : PlannedRouteComponentKind.VerticalStraightRun;
        var lane = first.AllocatedLane;
        var entryLane = lane;
        var exitLane = lane;
        if (turn is not null)
        {
            var ordered = route.Steps.OrderBy(step => step.Order).ToArray();
            var preceding = ordered.LastOrDefault(step => step.Order < first.Order && IsOrdinaryRouteStep(step));
            var following = ordered.FirstOrDefault(step => step.Order > first.Order && IsOrdinaryRouteStep(step));
            entryLane = preceding?.Role == RouteStepRole.Turn ? LaneForSide(first.EntrySide, turn, null) : preceding?.AllocatedLane;
            entryLane ??= LaneForSide(first.EntrySide, turn, null);
            exitLane = following?.Role == RouteStepRole.Turn ? LaneForSide(first.ExitSide, turn, null) : following?.AllocatedLane;
            exitLane ??= LaneForSide(first.ExitSide, turn, null);
            if (entryLane is null || exitLane is null)
                Add(routeFindings, route, "IncompleteTurnAllocation", route.PhysicalLinkId + ":turn:" + first.Order, null,
                    "An allocated turn does not bind both horizontal and vertical lanes.", null, null);
        }
        var entry = StepBoundary(first, first.EntrySide, entryLane, "component-entry");
        var exit = StepBoundary(last, last.ExitSide, exitLane, "component-exit");
        if (kind != PlannedRouteComponentKind.Turn && group.Count > 1)
        {
            for (var index = 1; index < group.Count; index++)
            {
                var before = group[index - 1];
                var after = group[index];
                if (before.GridId != after.GridId || before.CellId.ColumnId != after.CellId.ColumnId && !IsHorizontal(before) ||
                    before.CellId.RowId != after.CellId.RowId && IsHorizontal(before) ||
                    before.ExitSide == after.EntrySide)
                    Add(routeFindings, route, "StraightRunCellContinuity", route.PhysicalLinkId + ":run:" + first.StraightRunId, null,
                        "Successive cells in a straight run do not expose complementary shared sides.",
                        StepBoundary(before, before.ExitSide, before.AllocatedLane, "run-exit"), StepBoundary(after, after.EntrySide, after.AllocatedLane, "run-entry"));
            }
        }
        return new PlannedRouteComponentContract(
            route.PhysicalLinkId + ":component:" + first.Order, route.PhysicalLinkId, kind, first.Order,
            group.Select(step => step.CellId).ToArray(), entry, exit, first.EntrySide, last.ExitSide, lane,
            first.StraightRunId, turn?.BendIdentity, first.GridId.Value, null, null,
            "allocated route-step group");
    }

    private LaneId? LaneForRun(string runId) => allocation.HorizontalLanes.Concat(allocation.VerticalLanes)
        .Where(item => item.RunId == runId)
        .OrderBy(item => item.Axis)
        .ThenBy(item => item.Lane.Value, StringComparer.Ordinal)
        .Select(item => (LaneId?)item.Lane)
        .FirstOrDefault();

    private LaneId? LaneForSide(GridSide side, PlannedTurnAllocation turn, LaneId? adjacentLane)
    {
        var runId = side is GridSide.Left or GridSide.Right ? turn.HorizontalRunId : turn.VerticalRunId;
        return runId is null ? adjacentLane : LaneForRun(runId) ?? adjacentLane;
    }

    private static IReadOnlyList<IReadOnlyList<PlannedGridRouteStep>> GroupSteps(IReadOnlyList<PlannedGridRouteStep> steps)
    {
        var groups = new List<IReadOnlyList<PlannedGridRouteStep>>();
        foreach (var step in steps)
        {
            if (step.Role != RouteStepRole.Turn && groups.Count > 0 && groups[groups.Count - 1].Count > 0 &&
                groups[groups.Count - 1][groups[groups.Count - 1].Count - 1].Role != RouteStepRole.Turn &&
                groups[groups.Count - 1][groups[groups.Count - 1].Count - 1].StraightRunId == step.StraightRunId && step.StraightRunId is not null)
                groups[groups.Count - 1] = groups[groups.Count - 1].Concat(new[] { step }).ToArray();
            else
                groups.Add(new[] { step });
        }
        return groups;
    }

    private GridBoundaryIdentity? StepBoundary(PlannedGridRouteStep step, GridSide side, LaneId? lane, string authority) =>
        new(step.GridId, step.CellId, side, lane, step.GridId.Value, authority);

    private static bool IsOrdinaryRouteStep(PlannedGridRouteStep step) =>
        step.Role is RouteStepRole.HorizontalPassThrough or RouteStepRole.VerticalPassThrough or RouteStepRole.Turn;

    private static bool IsVertical(PlannedGridRouteStep step) =>
        step.EntrySide is GridSide.Top or GridSide.Bottom && step.ExitSide is GridSide.Top or GridSide.Bottom;

    private static bool TryCanonicalize(PlannedGridRoute route, PlannedRouteComponentContract beforeComponent,
        PlannedRouteComponentContract afterComponent,
        out GridBoundaryIdentity? canonical)
    {
        canonical = null;
        var before = beforeComponent.ExitBoundary;
        var after = afterComponent.EntryBoundary;
        if (before is null || after is null || before.GridId != after.GridId || before.OwnershipScope != after.OwnershipScope || before.Lane != after.Lane)
            return false;
        if (before.CellId == after.CellId && before.Side == after.Side)
        {
            canonical = new GridBoundaryIdentity(before.GridId, before.CellId, before.Side, before.Lane,
                before.OwnershipScope, "canonical-cell-boundary");
            return true;
        }
        if (before.CellId == after.CellId && AreComplementary(before.Side, after.Side))
        {
            canonical = new GridBoundaryIdentity(before.GridId, before.CellId, before.Side, before.Lane,
                before.OwnershipScope, "canonical-cell-connection");
            return true;
        }

        if (!AreComplementary(before.Side, after.Side)) return false;
        var beforeIndex = route.Steps.OrderBy(step => step.Order)
            .Select((step, index) => (step, index))
            .Where(item => beforeComponent.Cells.Contains(item.step.CellId))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Max();
        var afterIndex = route.Steps.OrderBy(step => step.Order)
            .Select((step, index) => (step, index))
            .Where(item => afterComponent.Cells.Contains(item.step.CellId))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Min();
        if (beforeIndex < 0 || afterIndex != beforeIndex + 1) return false;
        canonical = new GridBoundaryIdentity(before.GridId, before.CellId, before.Side, before.Lane,
            before.OwnershipScope, "ordered-route-boundary", after.CellId);
        return true;
    }

    private static bool AreComplementary(GridSide first, GridSide second) =>
        (first == GridSide.Left && second == GridSide.Right) ||
        (first == GridSide.Right && second == GridSide.Left) ||
        (first == GridSide.Top && second == GridSide.Bottom) ||
        (first == GridSide.Bottom && second == GridSide.Top);

    private GridBoundaryIdentity? EndpointBoundary(PlannedGridRoute route, NodeEndpoint endpoint, GridSide side, string authority)
    {
        var placement = placements.SingleOrDefault(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
        if (placement is null) return null;
        var lane = allocation.Endpoints.Any(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == side)
            ? new LaneId($"endpoint:{route.PhysicalLinkId}:{side}")
            : (LaneId?)null;
        return new GridBoundaryIdentity(placement.AnchorCellId.GridId, placement.AnchorCellId, side, lane,
            Ownership(endpoint), authority);
    }

    private string Ownership(NodeEndpoint endpoint) =>
        nodes.SingleOrDefault(node => node.PhysicalNodeId == endpoint.PhysicalNodeId)?.ProjectId ?? "diagram";

    private static bool IsHorizontal(PlannedGridRouteStep step) => step.EntrySide is GridSide.Left or GridSide.Right || step.ExitSide is GridSide.Left or GridSide.Right;

    private static void Add(List<RouteBoundaryContractFinding> findings, PlannedGridRoute route, string code,
        string componentId, string? otherComponentId, string message, GridBoundaryIdentity? expected, GridBoundaryIdentity? actual) =>
        findings.Add(new RouteBoundaryContractFinding(code, route.PhysicalLinkId, componentId, otherComponentId,
            message, expected?.ToString(), actual?.ToString()));
}
