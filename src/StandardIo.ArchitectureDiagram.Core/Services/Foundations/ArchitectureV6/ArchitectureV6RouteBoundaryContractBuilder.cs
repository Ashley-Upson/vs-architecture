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
            null, GridSide.Bottom, null, null, null, Ownership(route.Source), route.PhysicalLinkId + ":source-departure", null,
            "allocated source endpoint"));

        var first = ordered.FirstOrDefault();
        var firstEntry = first is null ? null : StepBoundary(first, first.EntrySide, first.AllocatedLane, "source-departure-entry");
        var departureId = route.PhysicalLinkId + ":source-departure";
        components.Add(new PlannedRouteComponentContract(departureId, route.PhysicalLinkId,
            PlannedRouteComponentKind.SourceDeparture, 0, first is null ? Array.Empty<PlanningGridCellId>() : new[] { first.CellId },
            sourceBoundary, firstEntry, GridSide.Bottom, first?.EntrySide, first?.AllocatedLane, null, null,
            Ownership(route.Source), sourceTerminalId, null, "derived from source endpoint and first route cell"));
        if (first is null)
            Add(routeFindings, route, "MissingFirstRouteStep", departureId, null, "A source departure has no first routing component.", null, null);
        else if (first.EntrySide != GridSide.Bottom)
            Add(routeFindings, route, "SourceDepartureNotBottomFacing", departureId, null,
                "The selected first route step enters from the top, so the frozen route does not provide a bottom-facing departure boundary.",
                sourceBoundary, firstEntry);

        var groups = GroupSteps(ordered);
        PlannedRouteComponentContract? previousRun = null;
        foreach (var group in groups)
        {
            var component = BuildStepComponent(route, group, routeFindings, previousRun);
            components.Add(component);
            if (component.Kind is PlannedRouteComponentKind.HorizontalStraightRun or PlannedRouteComponentKind.VerticalStraightRun)
                previousRun = component;
        }

        var last = ordered.LastOrDefault();
        var lastExit = last is null ? null : StepBoundary(last, last.ExitSide, last.AllocatedLane, "destination-approach-entry");
        var approachId = route.PhysicalLinkId + ":destination-approach";
        components.Add(new PlannedRouteComponentContract(approachId, route.PhysicalLinkId,
            PlannedRouteComponentKind.DestinationApproach, int.MaxValue - 1,
            last is null ? Array.Empty<PlanningGridCellId>() : new[] { last.CellId }, lastExit, destinationBoundary,
            last?.ExitSide, GridSide.Top, last?.AllocatedLane, last?.StraightRunId, null,
            Ownership(route.Destination), components.LastOrDefault()?.ComponentId, route.PhysicalLinkId + ":destination-terminal",
            "derived from destination approach and terminal endpoint"));
        if (last is null)
            Add(routeFindings, route, "MissingDestinationApproach", approachId, null, "A destination approach has no final route step.", null, null);
        else if (last.ExitSide != GridSide.Top)
            Add(routeFindings, route, "DestinationApproachNotTopFacing", approachId, null,
                "The selected final route step does not expose a top-facing terminal approach boundary.", lastExit, destinationBoundary);

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
            else if (before.ExitBoundary != after.EntryBoundary)
            {
                Add(routeFindings, route, "ComponentBoundaryMismatch", before.ComponentId, after.ComponentId,
                    "Adjacent components do not reference the same logical boundary identity.", before.ExitBoundary, after.EntryBoundary);
            }
        }

        return new PlannedRouteBoundaryContract(route.PhysicalLinkId, route.TopologyFamily, components,
            routeFindings, routeFindings.Count == 0, "boundary contract derived from allocated cells, sides and lanes");
    }

    private PlannedRouteComponentContract BuildStepComponent(PlannedGridRoute route,
        IReadOnlyList<PlannedGridRouteStep> group,
        List<RouteBoundaryContractFinding> routeFindings,
        PlannedRouteComponentContract? previousRun)
    {
        var first = group[0];
        var last = group[group.Count - 1];
        var turn = first.Role == RouteStepRole.Turn
            ? allocation.Turns.SingleOrDefault(item => item.RouteId == route.PhysicalLinkId && item.CellId == first.CellId.ToString())
            : null;
        var kind = first.Role == RouteStepRole.Turn
            ? PlannedRouteComponentKind.Turn
            : IsHorizontal(first) ? PlannedRouteComponentKind.HorizontalStraightRun : PlannedRouteComponentKind.VerticalStraightRun;
        var lane = first.AllocatedLane;
        var entryLane = lane;
        var exitLane = lane;
        if (turn is not null)
        {
            entryLane = turn.HorizontalRunId is null ? null : LaneForRun(turn.HorizontalRunId);
            exitLane = turn.VerticalRunId is null ? null : LaneForRun(turn.VerticalRunId);
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
        .SingleOrDefault(item => item.RunId == runId)?.Lane;

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

    private GridBoundaryIdentity? EndpointBoundary(PlannedGridRoute route, NodeEndpoint endpoint, GridSide side, string authority)
    {
        var placement = placements.SingleOrDefault(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
        if (placement is null) return null;
        var lane = allocation.Endpoints.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == side)?.Endpoint.PreferredTrack is { } preferred
            ? new LaneId("endpoint:" + preferred.Value)
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
