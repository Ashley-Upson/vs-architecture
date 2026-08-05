using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6AbstractRoutePlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly Dictionary<string, PlannedNodePlacement> placements;
    private readonly Dictionary<string, PhysicalNodePlacementMetadata> metadata;
    private readonly Dictionary<PlanningGridId, MutableGrid> grids;
    private readonly Dictionary<string, int> rowOrderByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> columnOrderByNode = new(StringComparer.Ordinal);
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

    public ArchitectureV6AbstractRoutePlanner(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedPhysicalLink> links,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<ProjectRoutingGrid> projectGrids)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.links = links ?? throw new ArgumentNullException(nameof(links));
        this.placements = placements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        this.metadata = metadata.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        grids = projectGrids.ToDictionary(item => item.Grid.Id, MutableGrid.From, EqualityComparer<PlanningGridId>.Default);
        foreach (var placement in placements)
        {
            var grid = projectGrids.Single(item => item.Grid.Id.Equals(placement.GridId)).Grid;
            rowOrderByNode[placement.PhysicalNodeId] = grid.Rows.Single(row => row.Id.Equals(placement.AnchorCellId.RowId)).LogicalOrder;
            columnOrderByNode[placement.PhysicalNodeId] = grid.Columns.Single(column => column.Id.Equals(placement.AnchorCellId.ColumnId)).LogicalOrder;
        }
    }

    public AbstractRoutePlanningResult Build()
    {
        var approaches = BuildApproaches();
        var routes = new List<PlannedGridRoute>();
        var transitions = new List<GridTransition>();
        var routeSteps = new List<PlannedGridRouteStep>();

        foreach (var link in links.OrderBy(item => ProjectOf(item.SourcePhysicalNodeId), StringComparer.Ordinal)
                     .ThenBy(item => Classify(item))
                     .ThenBy(item => rowOrderByNode[item.SourcePhysicalNodeId])
                     .ThenBy(item => columnOrderByNode[item.SourcePhysicalNodeId])
                     .ThenBy(item => rowOrderByNode[item.DestinationPhysicalNodeId])
                     .ThenBy(item => columnOrderByNode[item.DestinationPhysicalNodeId])
                     .ThenBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            var route = BuildRoute(link, approaches, transitions);
            routes.Add(route);
            routeSteps.AddRange(route.Steps);
        }

        ValidateRoutes(routes);

        var runs = CompileStraightRuns(routes);
        var endpointDemands = routes.SelectMany(route => new[]
        {
            new NodeEndpointDemand(route.PhysicalLinkId, route.Source, 1, null),
            new NodeEndpointDemand(route.PhysicalLinkId, route.Destination, 1, route.DestinationApproachReservationId)
        }).ToArray();
        var turnDemands = runs.Where(run => run.Axis == RouteAxis.Horizontal)
            .SelectMany(run => routes.Where(route => route.PhysicalLinkId == run.RouteId)
                .SelectMany(route => route.Steps.Where(step => step.Role == RouteStepRole.Turn)
                    .Select(step => new TurnDemand(route.PhysicalLinkId, step.CellId.ToString(),
                        new LaneId($"provisional:h:{route.PhysicalLinkId}"), new LaneId($"provisional:v:{route.PhysicalLinkId}")))))
            .ToArray();

        var projectGrids = grids.Values.Where(grid => grid.Id.Value.StartsWith("project:", StringComparison.Ordinal))
            .OrderBy(grid => grid.Id.Value, StringComparer.Ordinal)
            .Select(grid => grid.ToProjectGrid(nodes.Where(node => ProjectOf(node.PhysicalNodeId) == grid.Id.Value.Substring("project:".Length))))
            .ToArray();
        var diagramGrid = BuildDiagramGrid(transitions);
        return new AbstractRoutePlanningResult(projectGrids, diagramGrid, routes, approaches, runs, turnDemands, endpointDemands, diagnostics);
    }

    private void ValidateRoutes(IReadOnlyList<PlannedGridRoute> routes)
    {
        foreach (var duplicate in routes.GroupBy(route => route.PhysicalLinkId, StringComparer.Ordinal).Where(group => group.Count() != 1))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteCardinality", "Every physical link must have exactly one abstract route.", PlanningDiagnosticSubject.PhysicalLink, duplicate.Key));

        foreach (var link in links)
        {
            var route = routes.SingleOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId);
            if (route is null)
            {
                diagnostics.Add(new ArchitecturePlanningDiagnostic("MissingAbstractRoute", "A physical link has no abstract route or explicit unsupported result.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
                continue;
            }
            if (route.Source.PhysicalNodeId != link.SourcePhysicalNodeId || route.Destination.PhysicalNodeId != link.DestinationPhysicalNodeId)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteEndpointMismatch", "Abstract route endpoints do not match the physical link.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
            foreach (var step in route.Steps)
            {
                if (!grids.TryGetValue(step.GridId, out var grid) || !grid.Cells.ContainsKey(step.CellId))
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteUnknownCell", "An abstract route step references a cell absent from its grid.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
                else if ((grid.Cells[step.CellId].Capabilities & CellCapability.RoutingAllowed) == 0)
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteCellNotRoutable", "An abstract route step uses a cell without routing capability.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
            }
        }
    }

    private IReadOnlyList<DestinationApproachReservation> BuildApproaches()
    {
        return nodes.OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal).Select(node =>
        {
            var gridId = GridOf(node);
            var cell = AddCell(gridId, $"approach:{node.PhysicalNodeId}", Column(node), CellOccupancy.Empty);
            var linkIds = links.Where(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId)
                .OrderBy(link => link.PhysicalLinkId, StringComparer.Ordinal).Select(link => link.PhysicalLinkId).ToArray();
            return new DestinationApproachReservation($"approach:{node.PhysicalNodeId}", node.PhysicalNodeId, gridId,
                new[] { cell }, linkIds, node.IsExternal ? "external-direct" : "direct");
        }).ToArray();
    }

    private PlannedGridRoute BuildRoute(
        PlannedPhysicalLink link,
        IReadOnlyList<DestinationApproachReservation> approaches,
        ICollection<GridTransition> transitions)
    {
        var topology = Classify(link);
        var source = nodes.Single(node => node.PhysicalNodeId == link.SourcePhysicalNodeId);
        var destination = nodes.Single(node => node.PhysicalNodeId == link.DestinationPhysicalNodeId);
        var sourceGrid = GridOf(source);
        var destinationGrid = GridOf(destination);
        var sourceEndpoint = Endpoint(source, GridSide.Bottom, "source", sourceGrid);
        var destinationEndpoint = Endpoint(destination, GridSide.Top, "destination", destinationGrid);
        var approach = approaches.Single(item => item.PhysicalNodeId == destination.PhysicalNodeId);
        var steps = new List<PlannedGridRouteStep>();

        if (!sourceGrid.Equals(destinationGrid))
        {
            var sourceBoundary = AddCell(sourceGrid, $"transition:{link.PhysicalLinkId}:exit", Column(source), CellOccupancy.Empty);
            var diagramId = new PlanningGridId("diagram");
            var diagramBoundary = AddCell(diagramId, $"transition:{link.PhysicalLinkId}:passage", 0, CellOccupancy.Empty);
            var destinationBoundary = AddCell(destinationGrid, $"transition:{link.PhysicalLinkId}:entry", Column(destination), CellOccupancy.Empty);
            transitions.Add(new GridTransition(sourceGrid, sourceBoundary, diagramId, diagramBoundary, "project-to-diagram", link.SemanticLinkId,
                "exit", link.SourceProjectId, null, true, request.ProjectPlacement.ShowProjectContainers));
            transitions.Add(new GridTransition(diagramId, diagramBoundary, destinationGrid, destinationBoundary, "diagram-to-project", link.SemanticLinkId,
                "entry", null, link.DestinationProjectId, true, request.ProjectPlacement.ShowProjectContainers));
            steps.Add(Step(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId, GridSide.Top, GridSide.Bottom, RouteStepRole.SourceExit, 0, topology));
            steps.Add(Step(sourceGrid, sourceBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.ProjectExit, 1, topology));
            steps.Add(Step(diagramId, diagramBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.DiagramGridPassage, 2, topology));
            steps.Add(Step(destinationGrid, destinationBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.ProjectEntry, 3, topology));
            steps.Add(Step(destinationGrid, approach.Cells[0], GridSide.Top, GridSide.Bottom, RouteStepRole.DestinationEntry, 4, topology));
        }
        else
        {
            var departure = AddCell(sourceGrid, $"route:{link.PhysicalLinkId}:departure", DepartureColumn(source, destination), CellOccupancy.Empty);
            var routeRow = AddCell(sourceGrid, $"route:{link.PhysicalLinkId}:run", Column(destination), CellOccupancy.Empty);
            steps.Add(Step(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId, GridSide.Top, topology == RouteTopologyFamily.SameLayer || topology == RouteTopologyFamily.Upward || topology == RouteTopologyFamily.OwnershipLocalReturn ? GridSide.Right : GridSide.Bottom,
                RouteStepRole.SourceExit, 0, topology));
            if (topology == RouteTopologyFamily.SameLayer || topology == RouteTopologyFamily.Upward || topology == RouteTopologyFamily.OwnershipLocalReturn)
            {
                steps.Add(Step(sourceGrid, departure, GridSide.Left, GridSide.Bottom, RouteStepRole.Turn, 1, topology));
                steps.Add(Step(sourceGrid, routeRow, GridSide.Top, GridSide.Bottom, RouteStepRole.VerticalPassThrough, 2, topology));
            }
            else
                steps.Add(Step(sourceGrid, departure, GridSide.Top, GridSide.Bottom, RouteStepRole.VerticalPassThrough, 1, topology));
            steps.Add(Step(sourceGrid, approach.Cells[0], GridSide.Top, GridSide.Bottom, RouteStepRole.DestinationEntry,
                steps.Count, topology));
        }

        var supported = ValidateSteps(steps, link, topology);
        var unsupportedReason = supported ? null : "Topology-specific structural route could not be represented without entering an unrelated node footprint.";
        if (!supported)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("UnsupportedAbstractRoute", unsupportedReason!, PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
        return new PlannedGridRoute(link.PhysicalLinkId, sourceEndpoint, steps, transitions.Where(item => item.SemanticLinkId == link.SemanticLinkId).ToArray(),
            destinationEndpoint, topology, link.SourceProjectId, link.DestinationProjectId,
            $"{topology}: deterministic topology-owned route", approach.ReservationId, supported, unsupportedReason);
    }

    private bool ValidateSteps(IReadOnlyList<PlannedGridRouteStep> steps, PlannedPhysicalLink link, RouteTopologyFamily topology)
    {
        if (steps.Count == 0) return false;
        foreach (var step in steps)
        {
            if (!grids.TryGetValue(step.GridId, out var grid) && step.GridId.Value != "diagram") return false;
            if (step.EntrySide == step.ExitSide) return false;
            if (step.Role != RouteStepRole.SourceExit && step.Role != RouteStepRole.DestinationEntry &&
                grid!.Cells.TryGetValue(step.CellId, out var cell) && cell.FootprintOwnerId is not null)
                return false;
        }
        return steps[0].Role == RouteStepRole.SourceExit && steps[steps.Count - 1].Role == RouteStepRole.DestinationEntry;
    }

    private IReadOnlyList<PlannedStraightRun> CompileStraightRuns(IReadOnlyList<PlannedGridRoute> routes)
    {
        var result = new List<PlannedStraightRun>();
        foreach (var route in routes)
        {
            var current = new List<PlanningGridCellId>();
            RouteAxis? axis = null;
            foreach (var step in route.Steps)
            {
                var stepAxis = IsVertical(step) ? RouteAxis.Vertical : RouteAxis.Horizontal;
                if (axis is not null && axis != stepAxis && current.Count > 0)
                {
                    result.Add(new PlannedStraightRun(route.PhysicalLinkId, route.Steps.First(item => current.Contains(item.CellId)).GridId,
                        axis.Value, current.ToArray(), "step-start", "step-end", new LaneId($"provisional:{route.PhysicalLinkId}:{result.Count}")));
                    current = new List<PlanningGridCellId>();
                }
                axis = stepAxis;
                current.Add(step.CellId);
            }
            if (axis is not null && current.Count > 0)
                result.Add(new PlannedStraightRun(route.PhysicalLinkId, route.Steps.First(item => current.Contains(item.CellId)).GridId,
                    axis.Value, current.ToArray(), "step-start", "step-end", new LaneId($"provisional:{route.PhysicalLinkId}:{result.Count}")));
        }
        return result;
    }

    private DiagramRoutingGrid BuildDiagramGrid(IReadOnlyList<GridTransition> transitions)
    {
        var grid = grids.TryGetValue(new PlanningGridId("diagram"), out var existing)
            ? existing
            : new MutableGrid(new PlanningGridId("diagram"));
        return new DiagramRoutingGrid(grid.ToGrid(), Array.Empty<RelativeRectangle>(), transitions.ToArray());
    }

    private PlannedGridRouteStep Step(PlanningGridId gridId, PlanningGridCellId cell, GridSide entry, GridSide exit,
        RouteStepRole role, int order, RouteTopologyFamily topology) =>
        new(gridId, cell, entry, exit, role, order, topology.ToString(), null);

    private NodeEndpoint Endpoint(PlannedPhysicalNode node, GridSide side, string terminal, PlanningGridId gridId) =>
        new(node.PhysicalNodeId, side, $"{terminal}:{node.PhysicalNodeId}", 0, gridId, new PlanningGridColumnId($"column:{Column(node)}"), 1, $"{ProjectOf(node.PhysicalNodeId)}:{node.PhysicalNodeId}");

    private RouteTopologyFamily Classify(PlannedPhysicalLink link)
    {
        if (link.SourceProjectId != link.DestinationProjectId) return RouteTopologyFamily.CrossProject;
        var sourceRow = rowOrderByNode[link.SourcePhysicalNodeId];
        var targetRow = rowOrderByNode[link.DestinationPhysicalNodeId];
        if (nodes.Single(node => node.PhysicalNodeId == link.DestinationPhysicalNodeId).IsExternal) return RouteTopologyFamily.External;
        if (targetRow == sourceRow)
            return metadata[link.SourcePhysicalNodeId].PositionalOwnerId == metadata[link.DestinationPhysicalNodeId].PositionalOwnerId ? RouteTopologyFamily.OwnershipLocalReturn : RouteTopologyFamily.SameLayer;
        if (targetRow > sourceRow)
            return targetRow == sourceRow + 1 ? RouteTopologyFamily.AdjacentDownward : RouteTopologyFamily.LongDownward;
        return RouteTopologyFamily.Upward;
    }

    private PlanningGridCellId AddCell(PlanningGridId gridId, string rowToken, int column, CellOccupancy occupancy)
    {
        if (!grids.TryGetValue(gridId, out var grid))
        {
            grid = new MutableGrid(gridId);
            grids[gridId] = grid;
        }
        return grid.Add(rowToken, column, occupancy);
    }

    private PlanningGridId GridOf(PlannedPhysicalNode node) => new($"project:{ProjectOf(node.PhysicalNodeId)}");
    private string ProjectOf(string physicalNodeId) => nodes.Single(node => node.PhysicalNodeId == physicalNodeId).ProjectId ?? "external";
    private static bool IsVertical(PlannedGridRouteStep step) => step.EntrySide == GridSide.Top || step.EntrySide == GridSide.Bottom;
    private int Column(PlannedPhysicalNode node) => columnOrderByNode[node.PhysicalNodeId];
    private int DepartureColumn(PlannedPhysicalNode source, PlannedPhysicalNode destination) => Column(source) <= Column(destination)
        ? Column(source) + placements[source.PhysicalNodeId].ColumnSpan / 2 + 1
        : Column(source) - placements[source.PhysicalNodeId].ColumnSpan / 2 - 1;

    private sealed class MutableGrid
    {
        public MutableGrid(PlanningGridId id) => Id = id;
        public PlanningGridId Id { get; }
        public Dictionary<PlanningGridCellId, PlanningGridCell> Cells { get; } = new();
        public List<PlanningGridRowId> RowOrder { get; } = new();
        public List<PlanningGridColumnId> ColumnOrder { get; } = new();
        public IReadOnlyList<SubtreeReservation> Reservations { get; private set; } = Array.Empty<SubtreeReservation>();
        public string? ProjectId { get; private set; }
        public IReadOnlyList<string> OwnedPhysicalNodeIds { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> OwnedExternalNodeIds { get; private set; } = Array.Empty<string>();
        public static MutableGrid From(ProjectRoutingGrid project)
        {
            var result = new MutableGrid(project.Grid.Id)
            {
                Reservations = project.SubtreeReservations,
                ProjectId = project.ProjectId,
                OwnedPhysicalNodeIds = project.OwnedPhysicalNodeIds,
                OwnedExternalNodeIds = project.OwnedExternalNodeIds
            };
            result.RowOrder.AddRange(project.Grid.Rows.OrderBy(item => item.LogicalOrder).Select(item => item.Id));
            result.ColumnOrder.AddRange(project.Grid.Columns.OrderBy(item => item.LogicalOrder).Select(item => item.Id));
            foreach (var cell in project.Grid.Cells) result.Cells[cell.Key] = cell.Value;
            return result;
        }
        public PlanningGridCellId Add(string rowToken, int column, CellOccupancy occupancy)
        {
            var cell = new PlanningGridCellId(Id, new PlanningGridRowId(rowToken.StartsWith("row:", StringComparison.Ordinal) ? rowToken : $"route-row:{rowToken}"), new PlanningGridColumnId($"column:{column}"));
            if (!RowOrder.Contains(cell.RowId)) RowOrder.Add(cell.RowId);
            if (!ColumnOrder.Contains(cell.ColumnId)) ColumnOrder.Add(cell.ColumnId);
            if (!Cells.ContainsKey(cell)) Cells[cell] = new PlanningGridCell(cell, CellCapability.RoutingAllowed | CellCapability.NodeAllowed, occupancy, Array.Empty<string>());
            return cell;
        }
        public PlanningGrid ToGrid()
        {
            var rows = RowOrder.Distinct().Select((id, index) => new PlanningGridRow(id, index, 1, 1, 1, index, index)).ToArray();
            var columns = ColumnOrder.Distinct().Select((id, index) => new PlanningGridColumn(id, index, 1, 1, 1, index, index)).ToArray();
            return new PlanningGrid(Id, rows, columns, new Dictionary<PlanningGridCellId, PlanningGridCell>(Cells), new GridTransform(Id, new RelativePoint(0, 0)));
        }
        public ProjectRoutingGrid ToProjectGrid(IEnumerable<PlannedPhysicalNode> owned) => new(ProjectId ?? owned.FirstOrDefault()?.ProjectId ?? Id.Value.Substring("project:".Length), ToGrid(), Reservations, null,
            OwnedPhysicalNodeIds.Count == 0 ? owned.Select(item => item.PhysicalNodeId).ToArray() : OwnedPhysicalNodeIds,
            OwnedExternalNodeIds.Count == 0 ? owned.Where(item => item.IsExternal).Select(item => item.PhysicalNodeId).ToArray() : OwnedExternalNodeIds);
    }
}

internal sealed record AbstractRoutePlanningResult(
    IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
    DiagramRoutingGrid DiagramGrid,
    IReadOnlyList<PlannedGridRoute> Routes,
    IReadOnlyList<DestinationApproachReservation> DestinationApproaches,
    IReadOnlyList<PlannedStraightRun> StraightRuns,
    IReadOnlyList<TurnDemand> TurnDemands,
    IReadOnlyList<NodeEndpointDemand> EndpointDemands,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics);
