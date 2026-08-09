using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6CanonicalPlacementBuilder
{
    private const int MinimumSpan = 3;
    private const int Separation = 1;
    private readonly ArchitecturePlanningRequest request;
    private readonly ArchitectureProjectionResult projection;
    private readonly ArchitectureV6ReservedDepthTable reservations;
    private readonly IReadOnlyDictionary<string, int> spans;
    private readonly Dictionary<string, PlannedPhysicalNode> nodes;
    private readonly Dictionary<string, int> order;
    private readonly Dictionary<string, string?> owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LayoutNode> layouts = new(StringComparer.Ordinal);

    public ArchitectureV6CanonicalPlacementBuilder(
        ArchitecturePlanningRequest request,
        ArchitectureProjectionResult projection,
        IReadOnlyList<ArchitectureV6NodeSpanRequirement> spanRequirements,
        ArchitectureV6ReservedDepthTable reservations)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        this.reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        spans = (spanRequirements ?? Array.Empty<ArchitectureV6NodeSpanRequirement>())
            .ToDictionary(item => item.PhysicalNodeId, item => Math.Max(MinimumSpan, item.RequiredSpan), StringComparer.Ordinal);
        nodes = projection.PhysicalNodes.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        order = projection.PhysicalNodes.Select((node, index) => (Id: node.PhysicalNodeId, Index: index))
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
    }

    public ArchitectureV6CanonicalPlacementResult Build()
    {
        ResolveOwnership();
        var placementsByProject = new Dictionary<string, List<PlannedNodePlacement>>(StringComparer.Ordinal);
        var reservationsByProject = new Dictionary<string, List<SubtreeReservation>>(StringComparer.Ordinal);
        var projectGrids = new List<ProjectRoutingGrid>();
        var metadata = new List<PhysicalNodePlacementMetadata>();
        var linkMetadata = new List<PlannedPhysicalLinkMetadata>();
        var compositionMetrics = new List<PlacementCompositionMetrics>();
        var rowByNode = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var projectId in ProjectOrder())
        {
            var projectNodes = nodes.Values.Where(node => ProjectOf(node) == projectId)
                .OrderBy(node => order[node.PhysicalNodeId]).ToArray();
            var project = BuildProject(projectId, projectNodes, out var projectPlacements, out var subtreeReservations, out var projectMetadata,
                out var projectCompositionMetrics);
            placementsByProject[projectId] = projectPlacements;
            reservationsByProject[projectId] = subtreeReservations;
            projectGrids.Add(project);
            metadata.AddRange(projectMetadata);
            compositionMetrics.Add(projectCompositionMetrics);
            foreach (var item in projectMetadata)
                rowByNode[item.PhysicalNodeId] = item.PhysicalRow;
        }

        foreach (var link in projection.PhysicalLinks.OrderBy(item => order[item.SourcePhysicalNodeId]).ThenBy(item => order[item.DestinationPhysicalNodeId]))
        {
            var source = nodes[link.SourcePhysicalNodeId];
            var target = nodes[link.DestinationPhysicalNodeId];
            linkMetadata.Add(new PlannedPhysicalLinkMetadata(link.PhysicalLinkId, link.SemanticLinkId,
                link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId, source.ProjectId, target.ProjectId,
                rowByNode[link.SourcePhysicalNodeId], rowByNode[link.DestinationPhysicalNodeId],
                rowByNode[link.SourcePhysicalNodeId] < rowByNode[link.DestinationPhysicalNodeId] ? "downward" : "upward",
                source.ProjectId != target.ProjectId, target.IsExternal));
        }

        var allPlacements = placementsByProject.Values.SelectMany(items => items).OrderBy(item => order[item.PhysicalNodeId]).ToArray();
        var projectGridArray = projectGrids
            .Select((project, index) => project with
            {
                Grid = project.Grid with
                {
                    Transform = new GridTransform(project.Grid.Id,
                        new RelativePoint(projectGrids.Take(index).Sum(previous => previous.Grid.Columns.Count + Separation), 0))
                }
            })
            .ToArray();
        var diagramGrid = BuildDiagramGrid(projectGridArray);
        var allReservations = reservationsByProject.Values.SelectMany(items => items).ToArray();
        var diagnostics = new List<ArchitecturePlanningDiagnostic>();
        foreach (var metrics in compositionMetrics)
            diagnostics.Add(metrics.ToDiagnostic());
        Validate(allPlacements, projectGridArray, diagnostics);
        var result = new LogicalPlacementResult(projectGridArray, diagramGrid, metadata, linkMetadata, allReservations,
            diagnostics, allPlacements, new PlacementPerformance(0, 0, 0, 0, 0, 0, 0));
        var frozenNodes = allPlacements.Select(item => new ArchitectureV6FrozenNodePlacement(item.PhysicalNodeId,
            owners[item.PhysicalNodeId], item.GridId, item.AnchorCellId.RowId, item.CentreColumnId, item.ColumnSpan,
            nodes[item.PhysicalNodeId].IsExternal, nodes[item.PhysicalNodeId].IsStandalone)).ToArray();
        return new ArchitectureV6CanonicalPlacementResult(result,
            new ArchitectureV6PlacementFreeze(frozenNodes, projectGridArray, diagramGrid, Fingerprint(frozenNodes, projectGridArray)));
    }

    private ProjectRoutingGrid BuildProject(string projectId, IReadOnlyList<PlannedPhysicalNode> projectNodes,
        out List<PlannedNodePlacement> placements, out List<SubtreeReservation> subtreeReservations,
        out List<PhysicalNodePlacementMetadata> metadata, out PlacementCompositionMetrics compositionMetrics)
    {
        layouts.Clear();
        var roots = projectNodes.Where(node => owners[node.PhysicalNodeId] is null && !node.IsExternal && !node.IsStandalone)
            .OrderBy(node => order[node.PhysicalNodeId]).ToArray();
        var treeWidths = new List<(string Id, int Width)>();
        var nextColumn = 0;
        foreach (var root in roots)
        {
            var unit = Measure(root.PhysicalNodeId, 1);
            Place(root.PhysicalNodeId, nextColumn, 1);
            treeWidths.Add((root.PhysicalNodeId, unit.Width));
            nextColumn += unit.Width + Separation;
        }

        var detached = projectNodes.Where(node => owners[node.PhysicalNodeId] is not null && !node.IsExternal && !node.IsStandalone)
            .Where(node => IsDetached(node)).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
        var detachedWidths = new List<(string Id, int Width)>();
        foreach (var node in detached)
        {
            if (layouts.ContainsKey(node.PhysicalNodeId)) continue;
            var unit = Measure(node.PhysicalNodeId, RequiredRow(node)!.Value);
            Place(node.PhysicalNodeId, nextColumn, RequiredRow(node)!.Value);
            detachedWidths.Add((node.PhysicalNodeId, unit.Width));
            nextColumn += unit.Width + Separation;
        }

        // Every selected physical node must receive a placement before the grid is frozen. This
        // deterministic completion is only for ownership cases which could not be reached from
        // a root tree; it does not alter the frozen reservation table or route topology.
        foreach (var node in projectNodes.Where(node => !layouts.ContainsKey(node.PhysicalNodeId) && !node.IsExternal && !node.IsStandalone)
                     .OrderBy(node => order[node.PhysicalNodeId]))
        {
            var row = RequiredRow(node) ?? 1;
            var unit = Measure(node.PhysicalNodeId, row);
            Place(node.PhysicalNodeId, nextColumn, row);
            detachedWidths.Add((node.PhysicalNodeId, unit.Width));
            nextColumn += unit.Width + Separation;
        }

        var externalRow = reservations.External.NodeRow;
        var externalStart = nextColumn;
        var externalNodes = projectNodes.Where(node => node.IsExternal).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
        foreach (var node in externalNodes)
        {
            var owner = owners[node.PhysicalNodeId];
            var preferred = owner is not null && layouts.TryGetValue(owner, out var ownerLayout) ? ownerLayout.Center : nextColumn;
            var center = preferred;
            while (layouts.Values.Any(item => item.Row == externalRow && RangesOverlap(item.Center, item.Span, center, Span(node)))) center++;
            layouts[node.PhysicalNodeId] = new LayoutNode(node.PhysicalNodeId, externalRow, center, Span(node), false);
            nextColumn = Math.Max(nextColumn, center + Span(node) / 2 + Separation);
        }

        var standaloneRow = externalRow + 2;
        var standalones = projectNodes.Where(node => node.IsStandalone && !node.IsExternal).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
        var standaloneWidth = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standalones.Length)));
        var standaloneStart = nextColumn;
        var standaloneRowCursors = new Dictionary<int, int>();
        for (var index = 0; index < standalones.Length; index++)
        {
            var row = standaloneRow + (index / standaloneWidth) * 2;
            var column = standaloneRowCursors.TryGetValue(row, out var cursor) ? cursor : standaloneStart;
            var span = Span(standalones[index]);
            layouts[standalones[index].PhysicalNodeId] = new LayoutNode(standalones[index].PhysicalNodeId, row, column + span / 2, span, false);
            standaloneRowCursors[row] = column + span + Separation;
        }

        var maxRow = layouts.Values.Select(item => item.Row).DefaultIfEmpty(1).Max();
        var maxRight = layouts.Values.Select(item => item.Center + item.Span / 2).DefaultIfEmpty(0).Max();
        var gridId = new PlanningGridId("project:" + projectId);
        var rows = BuildRows(gridId, -2, maxRow + 2);
        var columns = BuildColumns(gridId, -2, Math.Max(3, maxRight + 2));
        var cells = BuildCells(gridId, rows, columns, layouts.Values);
        var grid = new PlanningGrid(gridId, rows, columns, cells, new GridTransform(gridId, new RelativePoint(0, 0)));
        placements = new List<PlannedNodePlacement>();
        subtreeReservations = new List<SubtreeReservation>();
        metadata = new List<PhysicalNodePlacementMetadata>();
        foreach (var node in projectNodes)
        {
            var layout = layouts[node.PhysicalNodeId];
            var row = new PlanningGridRowId("r" + layout.Row);
            var centre = new PlanningGridColumnId("c" + layout.Center);
            var footprint = Enumerable.Range(layout.Center - layout.Span / 2, layout.Span)
                .Select(column => new PlanningGridCellId(gridId, row, new PlanningGridColumnId("c" + column))).ToArray();
            placements.Add(new PlannedNodePlacement(node.PhysicalNodeId, gridId, footprint[layout.Span / 2], layout.Span, 1, footprint, centre, "pre-routing-span"));
            var reservationId = "subtree:" + node.PhysicalNodeId;
            subtreeReservations.Add(new SubtreeReservation(reservationId, owners[node.PhysicalNodeId], gridId, footprint, owners[node.PhysicalNodeId] is null ? null : "subtree:" + owners[node.PhysicalNodeId], new[] { new SubtreeReservationRowInterval(row, footprint.Select(item => item.ColumnId).ToArray()) }));
            var childrenIds = children[node.PhysicalNodeId].OrderBy(id => order[id]).ToArray();
            metadata.Add(new PhysicalNodePlacementMetadata(node.PhysicalNodeId, node.SemanticNodeId, owners[node.PhysicalNodeId], childrenIds,
                projection.PhysicalLinks.Where(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId).OrderBy(link => order[link.SourcePhysicalNodeId]).Select(link => link.SourcePhysicalNodeId).ToArray(),
                projection.PhysicalLinks.Where(link => link.SourcePhysicalNodeId == node.PhysicalNodeId).OrderBy(link => order[link.DestinationPhysicalNodeId]).Select(link => link.DestinationPhysicalNodeId).ToArray(),
                "tree:" + (owners[node.PhysicalNodeId] ?? node.PhysicalNodeId), Array.Empty<string>(), node.ProjectId, 0, false, node.IsExternal, node.IsStandalone,
                node.IsExternal ? "external-final-reserved-row" : node.IsStandalone ? "standalone-square-region" : "canonical-tree", SemanticDepth: layout.Row,
                RoleSelector: node.ResolvedRole, PhysicalRow: layout.Row, PhysicalColumn: layout.Center, ParentSemanticId: owners[node.PhysicalNodeId],
                VerticalSpacingPolicy: node.IsStandalone ? "standalone-square-region" : "logical-layer"));
        }
        var standaloneEnd = standaloneRowCursors.Values.DefaultIfEmpty(standaloneStart).Max() -
            (standalones.Length == 0 ? 0 : Separation);
        var externalEnd = layouts.Values.Where(item => item.Row == externalRow)
            .Select(item => item.Center + item.Span / 2).DefaultIfEmpty(externalStart - 1).Max();
        compositionMetrics = new PlacementCompositionMetrics(projectId, roots.Length, treeWidths, detachedWidths,
            standalones.Length == 0 ? 0 : standaloneEnd - standaloneStart + 1,
            externalEnd < externalStart ? 0 : externalEnd - externalStart + 1,
            Separation, columns.Count);
        return new ProjectRoutingGrid(projectId, grid, subtreeReservations, null,
            projectNodes.Select(node => node.PhysicalNodeId).ToArray(), projectNodes.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId).ToArray());
    }

    private LayoutNode Measure(string id, int row)
    {
        if (layouts.TryGetValue(id, out var existing)) return existing;
        var childIds = children[id].Where(child => !nodes[child].IsExternal && !nodes[child].IsStandalone && !IsDetached(nodes[child]))
            .OrderBy(child => order[child]).ToArray();
        var width = Span(nodes[id]);
        var childWidths = childIds
            .Select(child => Measure(child, Math.Max(row + 2, RequiredRow(nodes[child]) ?? row + 2)).Width)
            .ToArray();
        if (childWidths.Length == 1)
            width = Math.Max(width, childWidths[0]);
        else if (childWidths.Length > 1)
            width = Math.Max(width, childWidths.Sum() + (childWidths.Length - 1) * Separation);
        return new LayoutNode(id, row, 0, Math.Max(Span(nodes[id]), width), true);
    }

    private void Place(string id, int left, int row)
    {
        var childIds = children[id].Where(child => !nodes[child].IsExternal && !nodes[child].IsStandalone && !IsDetached(nodes[child]))
            .OrderBy(child => order[child]).ToArray();
        var childUnits = childIds.Select(child => Measure(child, Math.Max(row + 2, RequiredRow(nodes[child]) ?? row + 2))).ToArray();
        var childLeft = left;
        foreach (var childUnit in childUnits)
        {
            Place(childUnit.Id, childLeft, childUnit.Row);
            childLeft += childUnit.Width + Separation;
        }
        var center = childUnits.Length == 0
            ? left + Span(nodes[id]) / 2
            : (layouts[childUnits[0].Id].Center + layouts[childUnits[childUnits.Length - 1].Id].Center) / 2;
        layouts[id] = new LayoutNode(id, row, center, Span(nodes[id]), true);
    }

    private bool IsDetached(PlannedPhysicalNode node)
    {
        if (owners[node.PhysicalNodeId] is null) return false;
        var parent = nodes[owners[node.PhysicalNodeId]!];
        var required = RequiredRow(node);
        if (!required.HasValue) return false;
        var parentRow = layouts.TryGetValue(parent.PhysicalNodeId, out var parentLayout)
            ? parentLayout.Row
            : RequiredRow(parent) ?? int.MinValue;
        return required.Value <= parentRow;
    }

    private int? RequiredRow(PlannedPhysicalNode node)
    {
        if (node.IsExternal) return reservations.External.NodeRow;
        return reservations.Reservations.FirstOrDefault(item => item.Name == node.ResolvedRole)?.NodeRow;
    }

    private int Span(PlannedPhysicalNode node) => spans.TryGetValue(node.PhysicalNodeId, out var value) ? value : MinimumSpan;
    private string? ProjectOf(PlannedPhysicalNode node) => node.ProjectId;
    private IEnumerable<string> ProjectOrder() => projection.PhysicalNodes.Select(node => node.ProjectId).Where(id => id is not null).Distinct(StringComparer.Ordinal).Select(id => id!);

    private void ResolveOwnership()
    {
        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId])) children[node.PhysicalNodeId] = new List<string>();
        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var owner = node.PositionalOwnerId;
            if (owner is null || !nodes.ContainsKey(owner) || nodes[owner].ProjectId != node.ProjectId || order[owner] >= order[node.PhysicalNodeId]) owner = projection.PhysicalLinks
                .Where(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId && nodes.ContainsKey(link.SourcePhysicalNodeId))
                .Select(link => link.SourcePhysicalNodeId).Where(parent => nodes[parent].ProjectId == node.ProjectId && order[parent] < order[node.PhysicalNodeId])
                .OrderBy(parent => order[parent]).FirstOrDefault();
            owners[node.PhysicalNodeId] = owner;
            if (owner is not null) children[owner].Add(node.PhysicalNodeId);
        }
    }

    private static IReadOnlyList<PlanningGridRow> BuildRows(PlanningGridId gridId, int minimum, int maximum)
    {
        return Enumerable.Range(minimum, Math.Max(5, maximum - minimum + 1)).Select(index => new PlanningGridRow(new PlanningGridRowId("r" + index), index, 1, 1, 1, 0, 0,
            index == minimum || index == maximum - 1 ? PlanningGridTrackRole.ProjectHeader : index == minimum + 1 || index == maximum ? PlanningGridTrackRole.InterLayerRouting : index % 2 == 0 ? PlanningGridTrackRole.InterLayerRouting : PlanningGridTrackRole.NodeBearing,
            "canonical-placement", "fixed-parity")).ToArray();
    }

    private static IReadOnlyList<PlanningGridColumn> BuildColumns(PlanningGridId gridId, int minimum, int maximum)
    {
        return Enumerable.Range(minimum, Math.Max(6, maximum - minimum + 1)).Select(index => new PlanningGridColumn(new PlanningGridColumnId("c" + index), index, 1, 1, 1, 0, 0,
            index == minimum || index == maximum - 1 ? PlanningGridTrackRole.ProjectBoundaryTransition : index == maximum ? PlanningGridTrackRole.SubtreeSiblingGap : PlanningGridTrackRole.NodeFootprint,
            "canonical-placement", "fixed-surround")).ToArray();
    }

    private static IReadOnlyDictionary<PlanningGridCellId, PlanningGridCell> BuildCells(PlanningGridId gridId, IReadOnlyList<PlanningGridRow> rows,
        IReadOnlyList<PlanningGridColumn> columns, IEnumerable<LayoutNode> layouts)
    {
        var nodeCells = new HashSet<string>(layouts.SelectMany(layout => Enumerable.Range(layout.Center - layout.Span / 2, layout.Span).Select(column => layout.Row + ":c" + column)), StringComparer.Ordinal);
        var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
        foreach (var row in rows)
        foreach (var column in columns)
        {
            var id = new PlanningGridCellId(gridId, row.Id, column.Id);
            var boundary = row.Role == PlanningGridTrackRole.ProjectHeader || column.Role == PlanningGridTrackRole.ProjectBoundaryTransition;
            var key = row.LogicalOrder + ":" + column.Id.Value;
            cells[id] = new PlanningGridCell(id, nodeCells.Contains(key) ? CellCapability.NodeAllowed : CellCapability.RoutingAllowed | (boundary ? CellCapability.ProjectBoundary : CellCapability.None),
                nodeCells.Contains(key) ? CellOccupancy.NodeAnchor : CellOccupancy.Empty, Array.Empty<string>());
        }
        return cells;
    }

    private static bool RangesOverlap(int leftCentre, int leftSpan, int rightCentre, int rightSpan) =>
        leftCentre - leftSpan / 2 <= rightCentre + rightSpan / 2 &&
        rightCentre - rightSpan / 2 <= leftCentre + leftSpan / 2;

    private static DiagramRoutingGrid BuildDiagramGrid(IReadOnlyList<ProjectRoutingGrid> projects)
    {
        var gridId = new PlanningGridId("diagram");
        var maxRows = projects.Select(project => project.Grid.Rows.Count).DefaultIfEmpty(5).Max();
        var width = projects.Sum(project => project.Grid.Columns.Count + Separation);
        var rows = Enumerable.Range(0, Math.Max(5, maxRows)).Select(index => new PlanningGridRow(new PlanningGridRowId("r" + index), index, 1, 1, 1, 0, 0,
            index % 2 == 0 ? PlanningGridTrackRole.DiagramProjectPlacement : PlanningGridTrackRole.DiagramCrossProjectRouting, "diagram-composition", "fifo-project-order")).ToArray();
        var columns = Enumerable.Range(0, Math.Max(1, width)).Select(index => new PlanningGridColumn(new PlanningGridColumnId("c" + index), index, 1, 1, 1, 0, 0,
            PlanningGridTrackRole.DiagramProjectPlacement, "diagram-composition", "fifo-project-order")).ToArray();
        var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
        foreach (var row in rows) foreach (var column in columns)
        {
            var id = new PlanningGridCellId(gridId, row.Id, column.Id);
            cells[id] = new PlanningGridCell(id, CellCapability.RoutingAllowed, CellOccupancy.Empty, Array.Empty<string>());
        }
        var grid = new PlanningGrid(gridId, rows, columns, cells, new GridTransform(gridId, new RelativePoint(0, 0)));
        var footprints = projects.Select((project, index) => new RelativeRectangle(project.Grid.Transform.Origin.X + index * (project.Grid.Columns.Count + Separation), project.Grid.Transform.Origin.Y,
            project.Grid.Columns.Count, project.Grid.Rows.Count)).ToArray();
        return new DiagramRoutingGrid(grid, footprints, Array.Empty<GridTransition>());
    }

    private static void Validate(IReadOnlyList<PlannedNodePlacement> placements, IReadOnlyList<ProjectRoutingGrid> projects,
        ICollection<ArchitecturePlanningDiagnostic> diagnostics)
    {
        foreach (var group in placements.SelectMany(item => item.Footprint.Select(cell => (cell, item.PhysicalNodeId)))
            .GroupBy(item => item.cell).Where(group => group.Select(item => item.PhysicalNodeId).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            var owners = group.Select(item => item.PhysicalNodeId).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementFootprintOverlap",
                $"Canonical placement footprints overlap; owners={string.Join(",", owners)}; phase=placement-cell-materialisation.",
                PlanningDiagnosticSubject.PhysicalNode, group.Key.ToString()));
        }
    }

    private static string Fingerprint(IReadOnlyList<ArchitectureV6FrozenNodePlacement> nodes, IReadOnlyList<ProjectRoutingGrid> projects)
    {
        var text = string.Join("|", nodes.OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal).Select(node => node.PhysicalNodeId + ":" + node.GridId + ":" + node.RowId + ":" + node.CentreColumnId + ":" + node.ColumnSpan)) +
            "#" + string.Join("|", projects.Select(project => project.ProjectId + ":" + project.Grid.Rows.Count + ":" + project.Grid.Columns.Count));
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }

    private sealed record LayoutNode(string Id, int Row, int Center, int Span, bool IsTree)
    {
        public int Width => Span;
    }

    private sealed record PlacementCompositionMetrics(
        string ProjectId,
        int TopLevelTreeCount,
        IReadOnlyList<(string Id, int Width)> TreeWidths,
        IReadOnlyList<(string Id, int Width)> DetachedUnitWidths,
        int StandaloneRegionWidth,
        int ExternalRegionContribution,
        int AtomicUnitSeparation,
        int FinalProjectGridWidth)
    {
        public ArchitecturePlanningDiagnostic ToDiagnostic() => new(
            "LogicalPlacementCompositionMetrics",
            $"project={ProjectId};topLevelTrees={TopLevelTreeCount};treeWidths={Format(TreeWidths)};detachedWidths={Format(DetachedUnitWidths)};standaloneRegionWidth={StandaloneRegionWidth};externalRegionContribution={ExternalRegionContribution};atomicUnitSeparation={AtomicUnitSeparation};finalProjectGridWidth={FinalProjectGridWidth}.",
            PlanningDiagnosticSubject.Grid, "project:" + ProjectId, ArchitecturePlanningDiagnosticSeverity.Info);

        private static string Format(IReadOnlyList<(string Id, int Width)> values) =>
            values.Count == 0 ? "none" : string.Join(",", values.Select(value => value.Id + ":" + value.Width));
    }
}

internal sealed record ArchitectureV6CanonicalPlacementResult(LogicalPlacementResult Placement, ArchitectureV6PlacementFreeze Freeze);
