using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

// Compiles accepted logical tracks into relative geometry. It never changes placement or routes.
internal sealed class ArchitectureV6TrackSizingPlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly IReadOnlyList<ProjectRoutingGrid> projectGrids;
    private readonly IReadOnlyList<SubtreeReservation> reservations;
    private readonly IReadOnlyList<PhysicalNodePlacementMetadata> metadata;
    private readonly List<GridTrackConstraint> constraintsForSizing;
    private readonly DiagramRoutingGrid diagramGrid;
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();
    private long constraintConstructionMilliseconds;
    private long solverMilliseconds;
    private long offsetCompilationMilliseconds;
    private long nodeEnvelopeMilliseconds;
    private long reservationEnvelopeMilliseconds;
    private long validationMilliseconds;

    public ArchitectureV6TrackSizingPlanner(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<ProjectRoutingGrid> projectGrids,
        IReadOnlyList<SubtreeReservation> reservations,
        IReadOnlyList<GridTrackConstraint> constraints,
        DiagramRoutingGrid diagramGrid)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.projectGrids = projectGrids ?? throw new ArgumentNullException(nameof(projectGrids));
        this.reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        constraintsForSizing = new List<GridTrackConstraint>(constraints ?? throw new ArgumentNullException(nameof(constraints)));
        this.diagramGrid = diagramGrid ?? throw new ArgumentNullException(nameof(diagramGrid));
    }

    public PhysicalSizingResult Build()
    {
        var grids = new List<PlannedGridGeometry>();
        var relativeNodes = new List<PlannedRelativeNodeGeometry>();
        var relativeProjects = new List<PlannedRelativeProjectGeometry>();
        var relativeSubtrees = new List<PlannedRelativeSubtreeGeometry>();
        var sizingRows = new List<PlanningGridRow>();
        var sizingColumns = new List<PlanningGridColumn>();
        var sizingConstraints = new List<GridTrackConstraint>();
        var provenance = new List<GridTrackProvenance>();
        var projectWidths = new Dictionary<string, int>(StringComparer.Ordinal);
        var projectHeights = new Dictionary<string, int>(StringComparer.Ordinal);
        var solverIterations = 0;
        var idempotent = true;

        foreach (var project in projectGrids.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var sized = SizeGrid(project.Grid);
            solverIterations += sized.Iterations;
            idempotent &= SameTracks(sized, SizeGrid(project.Grid));
            sizingRows.AddRange(sized.Rows);
            sizingColumns.AddRange(sized.Columns);
            sizingConstraints.AddRange(sized.Constraints);
            provenance.AddRange(sized.Provenance(project.Grid.Id));
            grids.Add(ToGridGeometry(project.Grid.Id, sized));

            var owned = new HashSet<string>(project.OwnedPhysicalNodeIds, StringComparer.Ordinal);
            var projectPlacements = placements.Where(item => owned.Contains(item.PhysicalNodeId)).ToArray();
            var nodeEnvelopeTimer = Stopwatch.StartNew();
            foreach (var placement in projectPlacements)
            {
                var node = nodes.Single(item => item.PhysicalNodeId == placement.PhysicalNodeId);
                var bounds = FootprintBounds(placement, sized);
                relativeNodes.Add(new PlannedRelativeNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId,
                    bounds, placement.GridId, placement.AnchorCellId, node.PositionalOwnerId, node.ProjectionMode, node.IsExternal, node.IsStandalone));
            }
            nodeEnvelopeTimer.Stop();
            nodeEnvelopeMilliseconds += nodeEnvelopeTimer.ElapsedMilliseconds;

            var reservationEnvelopeTimer = Stopwatch.StartNew();
            foreach (var reservation in reservations.Where(item => item.GridId.Equals(project.Grid.Id)))
            {
                relativeSubtrees.Add(new PlannedRelativeSubtreeGeometry(reservation.SubtreeId, reservation.PositionalOwnerId,
                    reservation.GridId, FootprintBounds(reservation.Cells, sized), reservation.AncestorReservationId,
                    reservation.OccupiedRowIntervals.Select(interval => new PlannedRelativeSubtreeInterval(interval.RowId,
                        FootprintBounds(interval.Columns.Select(column => new PlanningGridCellId(reservation.GridId, interval.RowId, column)).ToArray(), sized))).ToArray()));
            }
            reservationEnvelopeTimer.Stop();
            reservationEnvelopeMilliseconds += reservationEnvelopeTimer.ElapsedMilliseconds;

            var content = project.Grid.Columns.OrderBy(item => item.LogicalOrder).Sum(item => sized.Columns.Single(column => column.Id.Equals(item.Id)).FinalExtent);
            var height = project.Grid.Rows.OrderBy(item => item.LogicalOrder).Sum(item => sized.Rows.Single(row => row.Id.Equals(item.Id)).FinalExtent);
            var widthWithPadding = checked(content + request.GridSizing.ContainerPadding * 2);
            var heightWithHeader = checked(height + request.GridSizing.ContainerPadding * 2 + request.GridSizing.ProjectHeaderHeight);
            projectWidths[project.ProjectId] = widthWithPadding;
            projectHeights[project.ProjectId] = heightWithHeader;
            if (project.ProjectLabelReservation is null)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("ProjectLabelMeasurementUnavailable",
                    "No measured project-label rectangle was supplied; the configured project header height is retained as structural space only.",
                    PlanningDiagnosticSubject.Grid, project.ProjectId));
            relativeProjects.Add(new PlannedRelativeProjectGeometry(project.ProjectId,
                new RelativeRectangle(0, 0, widthWithPadding, heightWithHeader),
                new RelativeRectangle(0, 0, widthWithPadding, request.GridSizing.ProjectHeaderHeight),
                projectPlacements.Select(item => item.PhysicalNodeId).ToArray()));
        }

        var hasDiagramTracks = diagramGrid.Grid.Rows.Count > 0 && diagramGrid.Grid.Columns.Count > 0;
        var diagramSource = hasDiagramTracks ? diagramGrid.Grid : CreateDiagramGrid(projectWidths, projectHeights);
        if (hasDiagramTracks) AddDiagramProjectConstraints(diagramSource, projectWidths, projectHeights);
        var diagramSized = SizeGrid(diagramSource);
        solverIterations += diagramSized.Iterations;
        idempotent &= SameTracks(diagramSized, SizeGrid(diagramSource));
        sizingRows.AddRange(diagramSized.Rows);
        sizingColumns.AddRange(diagramSized.Columns);
        sizingConstraints.AddRange(diagramSized.Constraints);
        provenance.AddRange(diagramSized.Provenance(diagramSource.Id));
        grids.Add(ToGridGeometry(diagramSource.Id, diagramSized));
        var diagramWidth = Math.Max(1, diagramSized.Columns.Sum(item => item.FinalExtent));
        var diagramHeight = Math.Max(1, diagramSized.Rows.Sum(item => item.FinalExtent));
        var diagramBounds = new RelativeRectangle(0, 0, diagramWidth, diagramHeight);
        var relative = new PlannedArchitectureRelativeGeometry(relativeNodes, relativeProjects, grids, relativeSubtrees, diagramBounds);
        var sizing = new GridTrackSizingPlan(sizingRows, sizingColumns, sizingConstraints, diagramBounds, provenance);
        var validationTimer = Stopwatch.StartNew();
        Validate(relative, projectGrids, diagramSized);
        validationTimer.Stop();
        validationMilliseconds += validationTimer.ElapsedMilliseconds;
        return new PhysicalSizingResult(sizing, relative, diagnostics, solverIterations, projectWidths, projectHeights,
            sizingRows.Count + sizingColumns.Count, diagramWidth, diagramHeight, idempotent,
            new SizingPerformance(constraintConstructionMilliseconds, solverMilliseconds, offsetCompilationMilliseconds,
                nodeEnvelopeMilliseconds, reservationEnvelopeMilliseconds, validationMilliseconds));
    }

    private void AddDiagramProjectConstraints(PlanningGrid grid, IReadOnlyDictionary<string, int> projectWidths,
        IReadOnlyDictionary<string, int> projectHeights)
    {
        foreach (var column in grid.Columns.Where(column => column.Role == PlanningGridTrackRole.DiagramProjectPlacement && column.OwnerId is not null))
            if (projectWidths.TryGetValue(column.OwnerId!, out var width))
                constraintsForSizing.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, grid.Id,
                    Array.Empty<PlanningGridRowId>(), new[] { column.Id }, width, "sized project footprint width", column.OwnerId));
        foreach (var row in grid.Rows.Where(row => row.Role == PlanningGridTrackRole.DiagramProjectPlacement))
            foreach (var project in projectHeights.OrderBy(item => item.Key, StringComparer.Ordinal))
                constraintsForSizing.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, grid.Id,
                    new[] { row.Id }, Array.Empty<PlanningGridColumnId>(), project.Value, "sized project footprint height", project.Key));
    }

    private SizedGrid SizeGrid(PlanningGrid grid)
    {
        var rows = grid.Rows.OrderBy(item => item.LogicalOrder).ToArray();
        var columns = grid.Columns.OrderBy(item => item.LogicalOrder).ToArray();
        var rowExtents = rows.ToDictionary(item => item.Id, item => 0);
        var columnExtents = columns.ToDictionary(item => item.Id, item => 0);
        var rowContributions = new Dictionary<PlanningGridRowId, List<GridTrackContribution>>();
        var columnContributions = new Dictionary<PlanningGridColumnId, List<GridTrackContribution>>();

        foreach (var row in rows) rowContributions[row.Id] = new List<GridTrackContribution>();
        foreach (var column in columns) columnContributions[column.Id] = new List<GridTrackContribution>();
        var constraintTimer = Stopwatch.StartNew();
        var gridConstraints = BuildGridConstraints(grid, rows, columns);
        constraintTimer.Stop();
        constraintConstructionMilliseconds += constraintTimer.ElapsedMilliseconds;
        var iterations = 0;
        var changed = true;
        var solverTimer = Stopwatch.StartNew();
        while (changed && iterations++ < Math.Max(1, gridConstraints.Count + 1))
        {
            changed = false;
            foreach (var constraint in gridConstraints)
            {
                var extent = RequiredExtent(constraint);
                if (constraint.Rows.Count == 1 && rowExtents.ContainsKey(constraint.Rows[0]))
                    changed |= Raise(rowExtents, rowContributions, constraint.Rows[0], extent, constraint);
                if (constraint.Columns.Count == 1 && columnExtents.ContainsKey(constraint.Columns[0]))
                    changed |= Raise(columnExtents, columnContributions, constraint.Columns[0], extent, constraint);
                if (constraint.Rows.Count > 1)
                    changed |= SatisfySpan(rowExtents, rowContributions, constraint.Rows, extent, constraint);
                if (constraint.Columns.Count > 1)
                    changed |= SatisfySpan(columnExtents, columnContributions, constraint.Columns, extent, constraint);
                if (constraint.Rows.Count == 0 && constraint.Columns.Count == 0 && constraint.MinimumExtent > 0)
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingConstraintWithoutTracks", "Sizing constraint has no owning tracks.", PlanningDiagnosticSubject.TrackConstraint, constraint.OwnerId));
            }
        }

        solverTimer.Stop();
        solverMilliseconds += solverTimer.ElapsedMilliseconds;
        if (changed)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingIterationLimit", "Track sizing did not converge within its deterministic iteration bound.", PlanningDiagnosticSubject.Grid, grid.Id.Value));

        var offsetTimer = Stopwatch.StartNew();
        var sizedRows = CompileRows(rows, rowExtents, rowContributions);
        var sizedColumns = CompileColumns(columns, columnExtents, columnContributions);
        offsetTimer.Stop();
        offsetCompilationMilliseconds += offsetTimer.ElapsedMilliseconds;
        var result = new SizedGrid(grid.Id, sizedRows, sizedColumns, iterations, rowContributions, columnContributions, gridConstraints);
        ValidateConstraints(result, gridConstraints);
        return result;
    }

    private PlanningGrid CreateDiagramGrid(IReadOnlyDictionary<string, int> projectWidths, IReadOnlyDictionary<string, int> projectHeights)
    {
        var projectIds = projectWidths.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var gridId = diagramGrid.Grid.Id;
        var rowId = new PlanningGridRowId("diagram:row:projects");
        var rows = new[] { new PlanningGridRow(rowId, 0, 1, 1, 1, 0, 0,
            PlanningGridTrackRole.DiagramProjectPlacement, "sizing", "diagram project placement", "diagram") };
        var columns = projectIds.Select((projectId, index) => new PlanningGridColumn(new PlanningGridColumnId($"diagram:column:{index}"), index,
            1, 1, 1, 0, 0, PlanningGridTrackRole.DiagramProjectPlacement, "sizing", "diagram project placement", projectId)).ToArray();
        var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
        for (var index = 0; index < columns.Length; index++)
        {
            var cellId = new PlanningGridCellId(gridId, rowId, columns[index].Id);
            cells[cellId] = new PlanningGridCell(cellId, CellCapability.RoutingAllowed | CellCapability.ProjectBoundary,
                CellOccupancy.ProjectFootprint, Array.Empty<string>(), projectIds[index]);
        }
        for (var index = 0; index < projectIds.Length; index++)
        {
            constraintsForSizing.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, gridId,
                Array.Empty<PlanningGridRowId>(), new[] { columns[index].Id }, projectWidths[projectIds[index]], "project footprint width", projectIds[index]));
            constraintsForSizing.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, gridId,
                new[] { rowId }, Array.Empty<PlanningGridColumnId>(), projectHeights[projectIds[index]], "project footprint height", projectIds[index]));
        }
        return new PlanningGrid(gridId, rows, columns, cells, new GridTransform(gridId, new RelativePoint(0, 0)));
    }

    private IReadOnlyList<GridTrackConstraint> BuildGridConstraints(PlanningGrid grid, IReadOnlyList<PlanningGridRow> rows, IReadOnlyList<PlanningGridColumn> columns)
    {
        var rowIds = new HashSet<PlanningGridRowId>(rows.Select(item => item.Id));
        var columnIds = new HashSet<PlanningGridColumnId>(columns.Select(item => item.Id));
        var result = new List<GridTrackConstraint>();
        foreach (var row in rows)
            result.Add(new GridTrackConstraint(TrackConstraintKind.SingleRowMinimum, grid.Id,
                new[] { row.Id }, Array.Empty<PlanningGridColumnId>(), InitialRowMinimum(row),
                $"structural row minimum:{row.Role}", row.Id.Value));
        foreach (var column in columns)
            result.Add(new GridTrackConstraint(TrackConstraintKind.SingleColumnMinimum, grid.Id,
                Array.Empty<PlanningGridRowId>(), new[] { column.Id }, InitialColumnMinimum(column),
                $"structural column minimum:{column.Role}", column.Id.Value));
        foreach (var constraint in constraintsForSizing.Where(item => item.GridId.Equals(grid.Id)))
        {
            var missingRows = constraint.Rows.Where(row => !rowIds.Contains(row)).ToArray();
            var missingColumns = constraint.Columns.Where(column => !columnIds.Contains(column)).ToArray();
            if (missingRows.Length > 0 || missingColumns.Length > 0)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingConstraintTrackMissing",
                    $"Sizing constraint references tracks not present in its owning grid: {constraint.Reason}.",
                    PlanningDiagnosticSubject.TrackConstraint, constraint.OwnerId));
            result.Add(constraint with
            {
                Rows = constraint.Rows.Where(rowIds.Contains).ToArray(),
                Columns = constraint.Columns.Where(columnIds.Contains).ToArray()
            });
        }
        foreach (var placement in placements.Where(item => item.GridId.Equals(grid.Id)))
        {
            var node = nodes.Single(item => item.PhysicalNodeId == placement.PhysicalNodeId);
            var placementRows = placement.Footprint.Select(item => item.RowId).Distinct().Where(rowIds.Contains).ToArray();
            var placementColumns = placement.Footprint.Select(item => item.ColumnId).Distinct().Where(columnIds.Contains).ToArray();
            result.Add(new GridTrackConstraint(TrackConstraintKind.NodeFootprint, grid.Id,
                Array.Empty<PlanningGridRowId>(), placementColumns, NodeWidth(node),
                "resolved display-label footprint width", placement.PhysicalNodeId + ":columns"));
            result.Add(new GridTrackConstraint(TrackConstraintKind.NodeFootprint, grid.Id,
                placementRows, Array.Empty<PlanningGridColumnId>(), request.NodePlacement.MinimumNodeHeight,
                "node minimum footprint height", placement.PhysicalNodeId + ":rows"));
        }
        return result;
    }

    private int InitialRowMinimum(PlanningGridRow row) => row.Role switch
    {
        PlanningGridTrackRole.NodeBearing or PlanningGridTrackRole.BaselineNode or
        PlanningGridTrackRole.ExternalRegion or PlanningGridTrackRole.StandaloneRegion => request.GridSizing.NodeBearingRowMinimum,
        PlanningGridTrackRole.ProjectBoundaryTransition or PlanningGridTrackRole.DiagramProjectPlacement => request.GridSizing.ProjectTransitionRowMinimum,
        _ => request.GridSizing.RoutingRowMinimum
    };

    private int NodeWidth(PlannedPhysicalNode node)
    {
        var label = node.DisplayLabel ?? node.SemanticName;
        var longestLine = label.Split(new[] { '\n' }, StringSplitOptions.None)
            .DefaultIfEmpty(string.Empty).Max(line => line.Length);
        return Math.Max(request.NodePlacement.MinimumNodeWidth, longestLine * 8 + 44);
    }

    private int InitialColumnMinimum(PlanningGridColumn column) => column.Role switch
    {
        PlanningGridTrackRole.NodeFootprint => request.GridSizing.NodeFootprintColumnMinimum,
        PlanningGridTrackRole.DestinationApproach => request.GridSizing.DestinationApproachColumnMinimum,
        PlanningGridTrackRole.OwnershipLocalReturn => request.GridSizing.OwnershipLocalReturnColumnMinimum,
        PlanningGridTrackRole.ProjectBoundaryTransition => request.GridSizing.ProjectTransitionColumnMinimum,
        PlanningGridTrackRole.DiagramCrossProjectRouting or PlanningGridTrackRole.DiagramProjectPlacement => request.GridSizing.DiagramRoutingColumnMinimum,
        _ => request.GridSizing.StructuralColumnMinimum
    };

    private int RequiredExtent(GridTrackConstraint constraint)
    {
        if (constraint.Kind == TrackConstraintKind.HorizontalLaneEnvelope || constraint.Kind == TrackConstraintKind.VerticalLaneEnvelope)
            return Math.Max(1, checked(constraint.MinimumExtent * request.RoutePlanning.MinimumParallelSpacing));
        return Math.Max(1, constraint.MinimumExtent);
    }

    private static bool Raise<TKey>(IDictionary<TKey, int> extents, IDictionary<TKey, List<GridTrackContribution>> contributions, TKey key,
        int required, GridTrackConstraint constraint) where TKey : notnull
    {
        if (extents[key] >= required) return false;
        var contribution = required - extents[key];
        extents[key] = required;
        contributions[key].Add(new GridTrackContribution(constraint.Kind, constraint.OwnerId, contribution));
        return true;
    }

    private static bool SatisfySpan<TKey>(IDictionary<TKey, int> extents, IDictionary<TKey, List<GridTrackContribution>> contributions,
        IReadOnlyList<TKey> keys, int required, GridTrackConstraint constraint) where TKey : notnull
    {
        keys = keys.Where(extents.ContainsKey).ToArray();
        if (keys.Count == 0) return false;
        var deficit = required - keys.Sum(key => extents[key]);
        if (deficit <= 0) return false;
        for (var index = 0; index < deficit; index++)
        {
            var offset = index / 2;
            var keyIndex = index % 2 == 0 ? (keys.Count - 1) / 2 - offset : keys.Count / 2 + offset + 1;
            if (keyIndex < 0 || keyIndex >= keys.Count) keyIndex = index % keys.Count;
            var key = keys[keyIndex];
            extents[key]++;
            contributions[key].Add(new GridTrackContribution(constraint.Kind, constraint.OwnerId, 1));
        }
        return true;
    }

    private static IReadOnlyList<PlanningGridRow> CompileRows(IReadOnlyList<PlanningGridRow> source, IReadOnlyDictionary<PlanningGridRowId, int> extents,
        IReadOnlyDictionary<PlanningGridRowId, List<GridTrackContribution>> contributions)
    {
        var offset = 0;
        return source.Select(row =>
        {
            var extent = extents[row.Id];
            var result = row with { RequiredExtent = extent, FinalExtent = extent, RelativeOffset = offset, AbsoluteOffset = offset };
            offset += extent;
            return result;
        }).ToArray();
    }

    private static IReadOnlyList<PlanningGridColumn> CompileColumns(IReadOnlyList<PlanningGridColumn> source, IReadOnlyDictionary<PlanningGridColumnId, int> extents,
        IReadOnlyDictionary<PlanningGridColumnId, List<GridTrackContribution>> contributions)
    {
        var offset = 0;
        return source.Select(column =>
        {
            var extent = extents[column.Id];
            var result = column with { RequiredExtent = extent, FinalExtent = extent, RelativeOffset = offset, AbsoluteOffset = offset };
            offset += extent;
            return result;
        }).ToArray();
    }

    private static PlannedGridGeometry ToGridGeometry(PlanningGridId id, SizedGrid grid)
    {
        var width = grid.Columns.Sum(item => item.FinalExtent);
        var height = grid.Rows.Sum(item => item.FinalExtent);
        return new PlannedGridGeometry(id, new RelativeRectangle(0, 0, width, height), new AbsoluteRectangle(0, 0, width, height),
            new GridTransform(id, new RelativePoint(0, 0)), grid.Rows, grid.Columns);
    }

    private static RelativeRectangle FootprintBounds(PlannedNodePlacement placement, SizedGrid grid) =>
        FootprintBounds(placement.Footprint, grid);

    private static RelativeRectangle FootprintBounds(IReadOnlyList<PlanningGridCellId> cells, SizedGrid grid)
    {
        if (cells.Count == 0) throw new InvalidOperationException("A footprint must contain at least one cell.");
        var columns = cells.Select(cell => grid.Columns.Single(column => column.Id.Equals(cell.ColumnId))).ToArray();
        var rows = cells.Select(cell => grid.Rows.Single(row => row.Id.Equals(cell.RowId))).ToArray();
        var x = columns.Min(item => item.RelativeOffset);
        var y = rows.Min(item => item.RelativeOffset);
        return new RelativeRectangle(x, y,
            columns.Max(item => item.RelativeOffset + item.FinalExtent) - x,
            rows.Max(item => item.RelativeOffset + item.FinalExtent) - y);
    }

    private static RelativeRectangle FootprintBounds(IReadOnlyList<PlanningGridCellId> cells,
        IReadOnlyList<PlanningGridRow> rows, IReadOnlyList<PlanningGridColumn> columns)
    {
        if (cells.Count == 0) throw new InvalidOperationException("A footprint must contain at least one cell.");
        var selectedColumns = cells.Select(cell => columns.Single(column => column.Id.Equals(cell.ColumnId))).ToArray();
        var selectedRows = cells.Select(cell => rows.Single(row => row.Id.Equals(cell.RowId))).ToArray();
        var x = selectedColumns.Min(item => item.RelativeOffset);
        var y = selectedRows.Min(item => item.RelativeOffset);
        return new RelativeRectangle(x, y,
            selectedColumns.Max(item => item.RelativeOffset + item.FinalExtent) - x,
            selectedRows.Max(item => item.RelativeOffset + item.FinalExtent) - y);
    }

    private static bool Contains(RelativeRectangle outer, RelativeRectangle inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width &&
        inner.Y + inner.Height <= outer.Y + outer.Height;

    private void Validate(PlannedArchitectureRelativeGeometry relative, IReadOnlyList<ProjectRoutingGrid> projects, SizedGrid diagram)
    {
        if (relative.Nodes.Count != placements.Count)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeAccounting", "Every planned node must compile to one relative rectangle.", PlanningDiagnosticSubject.PhysicalNode, null));
        foreach (var group in relative.Nodes.GroupBy(node => node.ProjectId ?? string.Empty))
        {
            foreach (var pair in Overlaps(group.ToArray()))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeOverlap",
                    $"Relative node rectangles overlap after grid sizing: {pair.Left.PhysicalNodeId} [{pair.Left.Bounds}] and {pair.Right.PhysicalNodeId} [{pair.Right.Bounds}].",
                    PlanningDiagnosticSubject.PhysicalNode, pair.Left.PhysicalNodeId));
        }
        if (relative.Nodes.Any(node => node.Bounds.Width <= 0 || node.Bounds.Height <= 0))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeInvalidDimension", "A relative node rectangle has a non-positive dimension.", PlanningDiagnosticSubject.PhysicalNode, null));
        foreach (var grid in relative.Grids)
        {
            if (grid.Rows.Any(row => row.FinalExtent <= 0) || grid.Columns.Any(column => column.FinalExtent <= 0))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingInvalidTrack", "A final grid track has a non-positive extent.", PlanningDiagnosticSubject.Grid, grid.GridId.Value));
            if (grid.RelativeBounds.Width != grid.Columns.Sum(column => column.FinalExtent) ||
                grid.RelativeBounds.Height != grid.Rows.Sum(row => row.FinalExtent))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingGridBoundsMismatch", "Grid bounds do not equal the sum of final track extents.", PlanningDiagnosticSubject.Grid, grid.GridId.Value));
        }
        foreach (var node in relative.Nodes)
        {
            var placement = placements.SingleOrDefault(item => item.PhysicalNodeId == node.PhysicalNodeId);
            var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(node.GridId));
            if (placement is null || grid is null) continue;
            var expected = FootprintBounds(placement.Footprint, grid.Rows, grid.Columns);
            if (node.Bounds != expected)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeEnvelopeMismatch",
                    $"Node rectangle must equal its complete footprint envelope: {node.PhysicalNodeId} [{node.Bounds}] != [{expected}].",
                    PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            if (node.ProjectId is not null)
            {
                var project = relative.Projects.SingleOrDefault(item => item.ProjectId == node.ProjectId);
                if (project is null || !Contains(project.Bounds, node.Bounds))
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeProjectContainment", "A node lies outside its project bounds.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            }
        }
        foreach (var reservation in relative.Subtrees)
        {
            var source = reservations.SingleOrDefault(item => item.SubtreeId == reservation.SubtreeId);
            if (source is null || reservation.Intervals is null) continue;
            var grid = relative.Grids.Single(item => item.GridId.Equals(reservation.GridId));
            foreach (var placement in placements.Where(item => item.GridId.Equals(source.GridId) && item.Footprint.All(source.Cells.Contains)))
            {
                foreach (var cell in placement.Footprint)
                {
                    var interval = reservation.Intervals.SingleOrDefault(item => item.RowId.Equals(cell.RowId));
                    var column = grid.Columns.Single(item => item.Id.Equals(cell.ColumnId));
                    var row = grid.Rows.Single(item => item.Id.Equals(cell.RowId));
                    var cellBounds = new RelativeRectangle(column.RelativeOffset, row.RelativeOffset, column.FinalExtent, row.FinalExtent);
                    if (interval is null || !Contains(interval.Bounds, cellBounds))
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("SparseReservationContainment", "A member node footprint lies outside its sparse subtree reservation interval.", PlanningDiagnosticSubject.Cell, reservation.SubtreeId));
                }
            }
        }
        if (relative.Nodes.Any(node => node.Bounds.X < 0 || node.Bounds.Y < 0))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeNegativeOffset", "A relative node rectangle has a negative track-derived offset.", PlanningDiagnosticSubject.PhysicalNode, null));
    }

    private void ValidateConstraints(SizedGrid grid, IReadOnlyList<GridTrackConstraint> constraints)
    {
        foreach (var constraint in constraints)
        {
            var extent = RequiredExtent(constraint);
            var actual = constraint.Rows.Count > 0
                ? constraint.Rows.Sum(row => grid.Rows.SingleOrDefault(item => item.Id.Equals(row))?.FinalExtent ?? 0)
                : constraint.Columns.Sum(column => grid.Columns.SingleOrDefault(item => item.Id.Equals(column))?.FinalExtent ?? 0);
            if (actual < extent)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingConstraintUnsatisfied",
                    $"Sizing constraint requires {extent} but final tracks provide {actual}: {constraint.Reason}.",
                    PlanningDiagnosticSubject.TrackConstraint, constraint.OwnerId));
        }
    }

    private static bool HasOverlap(IEnumerable<RelativeRectangle> rectangles)
    {
        var values = rectangles.ToArray();
        for (var index = 0; index < values.Length; index++)
            for (var other = index + 1; other < values.Length; other++)
                if (values[index].X < values[other].X + values[other].Width && values[other].X < values[index].X + values[index].Width &&
                    values[index].Y < values[other].Y + values[other].Height && values[other].Y < values[index].Y + values[index].Height)
                    return true;
        return false;
    }

    private static bool SameTracks(SizedGrid left, SizedGrid right) =>
        left.Rows.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset)).SequenceEqual(right.Rows.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset))) &&
        left.Columns.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset)).SequenceEqual(right.Columns.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset)));

    private static IEnumerable<(PlannedRelativeNodeGeometry Left, PlannedRelativeNodeGeometry Right)> Overlaps(IReadOnlyList<PlannedRelativeNodeGeometry> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
            for (var other = index + 1; other < nodes.Count; other++)
                if (HasOverlap(new[] { nodes[index].Bounds, nodes[other].Bounds }))
                    yield return (nodes[index], nodes[other]);
    }

    private sealed record SizedGrid(
        PlanningGridId Id,
        IReadOnlyList<PlanningGridRow> Rows,
        IReadOnlyList<PlanningGridColumn> Columns,
        int Iterations,
        IReadOnlyDictionary<PlanningGridRowId, List<GridTrackContribution>> RowContributions,
        IReadOnlyDictionary<PlanningGridColumnId, List<GridTrackContribution>> ColumnContributions,
        IReadOnlyList<GridTrackConstraint> Constraints)
    {
        public IEnumerable<GridTrackProvenance> Provenance(PlanningGridId gridId) =>
            Rows.Select(row => Create(gridId, "row", row.Id.Value, row.MinimumExtent, row.FinalExtent, row.Role, row.OwnerId,
                RowContributions.TryGetValue(row.Id, out var rowContributions) ? rowContributions : Array.Empty<GridTrackContribution>(),
                Constraints.Where(constraint => constraint.Rows.Contains(row.Id)).ToArray()))
            .Concat(Columns.Select(column => Create(gridId, "column", column.Id.Value, column.MinimumExtent, column.FinalExtent, column.Role, column.OwnerId,
                ColumnContributions.TryGetValue(column.Id, out var columnContributions) ? columnContributions : Array.Empty<GridTrackContribution>(),
                Constraints.Where(constraint => constraint.Columns.Contains(column.Id)).ToArray())));

        private static GridTrackProvenance Create(PlanningGridId gridId, string axis, string trackId, int minimum, int final,
            PlanningGridTrackRole role, string? ownerId, IReadOnlyList<GridTrackContribution> contributions,
            IReadOnlyList<GridTrackConstraint> constraints)
        {
            var dominant = contributions.GroupBy(item => item.Kind).OrderByDescending(group => group.Sum(item => item.Extent))
                .ThenBy(group => group.Key).Select(group => (TrackConstraintKind?)group.Key).FirstOrDefault();
            var laneCount = constraints.Where(item => item.Kind == TrackConstraintKind.HorizontalLaneEnvelope || item.Kind == TrackConstraintKind.VerticalLaneEnvelope)
                .Select(item => item.MinimumExtent).DefaultIfEmpty(0).Max();
            return new GridTrackProvenance(gridId, axis, trackId, minimum, final, contributions, role, ownerId, laneCount,
                Math.Max(0, final - minimum), dominant);
        }
    }
}

internal sealed record PhysicalSizingResult(
    GridTrackSizingPlan Sizing,
    PlannedArchitectureRelativeGeometry RelativeGeometry,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    int ReconciliationIterations,
    IReadOnlyDictionary<string, int> ProjectWidths,
    IReadOnlyDictionary<string, int> ProjectHeights,
    int SizedTrackCount,
    int DiagramWidth,
    int DiagramHeight,
    bool SizingIdempotent,
    SizingPerformance Performance);

internal sealed record SizingPerformance(
    long ConstraintConstructionMilliseconds,
    long SolverMilliseconds,
    long OffsetCompilationMilliseconds,
    long NodeEnvelopeMilliseconds,
    long ReservationEnvelopeMilliseconds,
    long ValidationMilliseconds);
