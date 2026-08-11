using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7PhysicalSceneCompilationStage
{
    public ArchitectureV7PhysicalSceneFreeze Compile(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7CollectiveAllocationFreeze allocation,
        ArchitectureV7PhysicalSceneConfiguration configuration,
        IReadOnlyDictionary<string, int>? preRoutingWidthRequirements = null)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (routes is null) throw new ArgumentNullException(nameof(routes));
        if (allocation is null) throw new ArgumentNullException(nameof(allocation));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (!string.Equals(allocation.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal) ||
            !string.Equals(allocation.RouteFingerprint, routes.RouteFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The allocation freeze does not belong to the supplied placement and route freezes.", nameof(allocation));

        var diagnostics = new List<ArchitectureV7PhysicalSceneDiagnostic>();
        var indexes = CompilationIndexes.Create(allocation);
        var rowCount = Math.Max(placement.DiagramGrid.RowCount, placement.Nodes.Count == 0 ? 0 : placement.Nodes.Max(x => x.DiagramRow) + 1);
        var columnCount = Math.Max(placement.DiagramGrid.ColumnCount, placement.Nodes.Count == 0 ? 0 : placement.Nodes.Max(x => x.DiagramColumn + x.LogicalSpan));
        var rows = SizeRows(rowCount, placement, allocation, indexes, configuration);
        var columns = SizeColumns(columnCount, placement, allocation, indexes, configuration, preRoutingWidthRequirements);
        var nodes = MaterialiseNodes(placement, rows, columns, configuration, diagnostics);
        var terminals = MaterialiseTerminals(allocation, nodes, configuration, diagnostics);
        var routesOutput = MaterialiseRoutes(routes, allocation, indexes, rows, columns, nodes, terminals, diagnostics);
        var fingerprint = Fingerprint(rows, columns, nodes, terminals, routesOutput, diagnostics, placement.PlacementFingerprint, routes.RouteFingerprint, allocation.AllocationFingerprint);
        return new ArchitectureV7PhysicalSceneFreeze(rows, columns, nodes, terminals, routesOutput, diagnostics,
            placement.PlacementFingerprint, routes.RouteFingerprint, allocation.AllocationFingerprint, fingerprint);
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTrackDimension> SizeRows(int count, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7CollectiveAllocationFreeze allocation, CompilationIndexes indexes, ArchitectureV7PhysicalSceneConfiguration configuration)
    {
        var extents = Enumerable.Range(0, count).Select(row => ArchitectureV7PhysicalSceneSizing.RowMinimum(row, placement, configuration)).ToArray();
        foreach (var group in allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal).GroupBy(x => x.Cells[0].Row))
        {
            if ((uint)group.Key >= (uint)extents.Length) continue;
            var laneCount = group.Select(x => indexes.AssignmentsByRunId[x.RunId].LaneOrdinal).DefaultIfEmpty(0).Max() + 1;
            var laneEnvelope = ArchitectureV7PhysicalSceneSizing.LaneEnvelope(laneCount, configuration);
            extents[group.Key] = Math.Max(extents[group.Key], laneEnvelope);
        }
        foreach (var demand in allocation.TrackDemands)
            if ((uint)demand.LogicalRow < (uint)extents.Length)
                extents[demand.LogicalRow] = Math.Max(extents[demand.LogicalRow], demand.RequiredRowExtent);
        var result = new List<ArchitectureV7PhysicalTrackDimension>(count);
        var cursor = 0d;
        for (var index = 0; index < count; index++)
        {
            var laneCount = LaneCount(allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal && x.Cells[0].Row == index), indexes);
            var laneCoordinates = LaneCoordinates(cursor, extents[index], laneCount, configuration.ParallelLaneSpacing);
            result.Add(new(index, cursor, cursor + extents[index], extents[index], laneCoordinates));
            cursor += extents[index];
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTrackDimension> SizeColumns(int count, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7CollectiveAllocationFreeze allocation, CompilationIndexes indexes, ArchitectureV7PhysicalSceneConfiguration configuration,
        IReadOnlyDictionary<string, int>? preRoutingWidthRequirements)
    {
        var extents = Enumerable.Repeat(configuration.BaseCellWidth, count).ToArray();
        foreach (var node in placement.Nodes)
        {
            var first = node.LogicalFootprint.Count == 0 ? node.DiagramColumn : node.LogicalFootprint.Min(x => x.Column);
            var last = node.LogicalFootprint.Count == 0 ? node.DiagramColumn + node.LogicalSpan - 1 : node.LogicalFootprint.Max(x => x.Column);
            var fallbackRequired = Math.Max(configuration.NodeMinimumWidth, node.VisibleLabel.Length * configuration.LabelCharacterWidth + 2 * configuration.LabelHorizontalMargin);
            var required = preRoutingWidthRequirements is not null && preRoutingWidthRequirements.TryGetValue(node.PhysicalNodeId, out var preRoutingRequired)
                ? Math.Max(fallbackRequired, preRoutingRequired)
                : fallbackRequired;
            var current = Enumerable.Range(first, Math.Max(0, last - first + 1)).Where(x => (uint)x < (uint)extents.Length).Sum(x => extents[x]);
            if (last >= first && current < required && (uint)first < (uint)extents.Length) extents[Math.Min(last, Math.Max(first, node.CentreCell))] += required - current;
        }
        foreach (var group in allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Vertical).GroupBy(x => x.Cells[0].Column))
        {
            if ((uint)group.Key >= (uint)extents.Length) continue;
            var laneCount = group.Select(x => indexes.AssignmentsByRunId[x.RunId].LaneOrdinal).DefaultIfEmpty(0).Max() + 1;
            extents[group.Key] = Math.Max(extents[group.Key], 2 * configuration.RouteClearance + Math.Max(0, laneCount - 1) * configuration.ParallelLaneSpacing);
        }
        foreach (var demand in allocation.TrackDemands)
            if ((uint)demand.LogicalColumn < (uint)extents.Length)
                extents[demand.LogicalColumn] = Math.Max(extents[demand.LogicalColumn], demand.RequiredColumnExtent);
        var result = new List<ArchitectureV7PhysicalTrackDimension>(count);
        var cursor = 0d;
        for (var index = 0; index < count; index++)
        {
            var laneCount = LaneCount(allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Vertical && x.Cells[0].Column == index), indexes);
            result.Add(new(index, cursor, cursor + extents[index], extents[index], LaneCoordinates(cursor, extents[index], laneCount, configuration.ParallelLaneSpacing)));
            cursor += extents[index];
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalSceneNode> MaterialiseNodes(ArchitectureV7PlacementFreeze placement,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ArchitectureV7PhysicalSceneConfiguration configuration, ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7PhysicalSceneNode>();
        foreach (var node in placement.Nodes)
        {
            var footprint = node.LogicalFootprint.Count == 0 ? new[] { (node.DiagramRow, node.DiagramColumn) } : node.LogicalFootprint;
            var minRow = footprint.Min(x => x.Row); var maxRow = footprint.Max(x => x.Row); var minColumn = footprint.Min(x => x.Column); var maxColumn = footprint.Max(x => x.Column);
            if ((uint)minRow >= (uint)rows.Count || (uint)maxRow >= (uint)rows.Count || (uint)minColumn >= (uint)columns.Count || (uint)maxColumn >= (uint)columns.Count)
            {
                diagnostics.Add(new("NODE-FOOTPRINT-OUT-OF-RANGE", "A frozen node footprint cannot be represented by the physical track table.", true));
                continue;
            }
            var rowTop = rows[minRow].Start;
            var rowBottom = rows[maxRow].End;
            if (minRow == maxRow && ArchitectureV7PhysicalSceneSizing.IsNodeBearingRow(minRow, placement))
            {
                rowTop += configuration.NodeClearance;
                rowBottom -= configuration.NodeClearance;
            }
            result.Add(new(node.PhysicalNodeId, new(columns[minColumn].Start, rowTop, columns[maxColumn].End, rowBottom), "frozen-logical-footprint;node-row-clearance-envelope;physical-track-boundaries"));
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTerminal> MaterialiseTerminals(
        ArchitectureV7CollectiveAllocationFreeze allocation, IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        ArchitectureV7PhysicalSceneConfiguration configuration, ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7PhysicalTerminal>();
        foreach (var slot in allocation.Terminals)
        {
            var node = nodes.FirstOrDefault(x => x.PhysicalNodeId == slot.PhysicalNodeId);
            if (node is null) { diagnostics.Add(new("TERMINAL-NODE-MISSING", "A frozen terminal has no materialised node bounds.", true, slot.PhysicalLinkId)); continue; }
            var x = (node.Bounds.Left + node.Bounds.Right) / 2d + slot.RelativeOffset;
            var y = slot.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? node.Bounds.Bottom : node.Bounds.Top;
            if (x < node.Bounds.Left + configuration.TerminalInset || x > node.Bounds.Right - configuration.TerminalInset)
                diagnostics.Add(new("TERMINAL-OUT-OF-BOUNDS", "A frozen terminal slot does not fit; terminal clamping is forbidden.", true, slot.PhysicalLinkId));
            result.Add(new(slot.PhysicalLinkId, slot.PhysicalNodeId, slot.EndpointKind, slot.SlotOrdinal, new(x, y, "terminal-edge+frozen-slot"), "node-edge;frozen-slot-ordinal;configured-spacing-inset"));
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalRoute> MaterialiseRoutes(
        ArchitectureV7LogicalRouteFreeze routes, ArchitectureV7CollectiveAllocationFreeze allocation, CompilationIndexes indexes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes, IReadOnlyList<ArchitectureV7PhysicalTerminal> terminals,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7PhysicalRoute>();
        foreach (var route in routes.Routes)
        {
            var routeRuns = indexes.RunsByPhysicalLinkId.TryGetValue(route.PhysicalLinkId, out var indexedRuns)
                ? indexedRuns : Array.Empty<ArchitectureV7StraightRun>();
            var routeTerminals = terminals.Where(x => x.PhysicalLinkId == route.PhysicalLinkId).ToArray();
            var source = routeTerminals.FirstOrDefault(x => x.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var destination = routeTerminals.FirstOrDefault(x => x.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
            if (!route.IsComplete || source is null || destination is null || route.Cells.Count < 3 || routeRuns.Count == 0)
            {
                diagnostics.Add(new("ROUTE-COMPILATION-FAILED", "A frozen route cannot be mechanically compiled without repair.", true, route.PhysicalLinkId));
                continue;
            }
            var points = new List<CompiledPoint> { new(0, route.Cells[0], source.Position, routeRuns[0].RunId, LaneFor(routeRuns[0], indexes), "source-terminal") };
            var routeFailed = false;
            for (var index = 1; index < route.Cells.Count - 1; index++)
            {
                var point = PointFor(route, index, indexes, rows, columns, diagnostics);
                if (point is null) { routeFailed = true; break; }
                points.Add(point);
            }
            if (routeFailed) continue;
            points.Add(new(route.Cells.Count - 1, route.Cells[route.Cells.Count - 1], destination.Position, routeRuns[routeRuns.Count - 1].RunId, LaneFor(routeRuns[routeRuns.Count - 1], indexes), "destination-terminal"));
            var expanded = AddAllocatedEndpointHandoffs(points, route, indexes, source, destination, rows, columns, diagnostics);
            if (expanded is null) continue;
            var physicalPoints = expanded.Select(x => x.Point).ToArray();
            var segments = new List<ArchitectureV7PhysicalSegment>();
            for (var index = 0; index + 1 < expanded.Count; index++)
            {
                var a = expanded[index]; var b = expanded[index + 1];
                if (a.Point.X != b.Point.X && a.Point.Y != b.Point.Y) { diagnostics.Add(new("DIAGONAL-COMPILER-OUTPUT", "Mechanical compilation produced a diagonal; no repair is permitted.", true, route.PhysicalLinkId)); continue; }
                segments.Add(new(route.PhysicalLinkId, a.Point, b.Point, new[] { a.RouteIndex, b.RouteIndex }.Distinct().ToArray(), new[] { route.Cells[a.RouteIndex], route.Cells[b.RouteIndex] }.Distinct().ToArray(), a.RunId, a.LaneId, a.Provenance + ";" + b.Provenance));
            }
            result.Add(new(route.PhysicalLinkId, physicalPoints, segments, "exact-frozen-route-cell-sequence;unsimplified"));
        }
        return result;
    }

    private static List<CompiledPoint>? AddAllocatedEndpointHandoffs(List<CompiledPoint> points, ArchitectureV7LogicalRoute route,
        CompilationIndexes indexes, ArchitectureV7PhysicalTerminal source, ArchitectureV7PhysicalTerminal destination,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<CompiledPoint> { points[0] };
        var first = points[1];
        if (source.Position.X != first.Point.X && source.Position.Y != first.Point.Y)
        {
            var handoff = indexes.HandoffsByEndpoint.TryGetValue((route.PhysicalLinkId, ArchitectureV7EndpointKind.SourceDeparture), out var sourceHandoff)
                ? sourceHandoff : null;
            if (handoff is null)
            {
                diagnostics.Add(new("ENDPOINT-HANDOFF-MISSING", "Terminal/lane mismatch has no frozen handoff allocation; compiler will not synthesize one.", true, route.PhysicalLinkId));
                return null;
            }
            result.Add(MaterialiseHandoffPoint(handoff, source, route, rows, columns, first));
        }
        result.AddRange(points.Skip(1).Take(points.Count - 2));
        var last = points[points.Count - 1]; var previous = result[result.Count - 1];
        if (previous.Point.X != destination.Position.X && previous.Point.Y != destination.Position.Y)
        {
            var handoff = indexes.HandoffsByEndpoint.TryGetValue((route.PhysicalLinkId, ArchitectureV7EndpointKind.DestinationArrival), out var destinationHandoff)
                ? destinationHandoff : null;
            if (handoff is null)
            {
                diagnostics.Add(new("ENDPOINT-HANDOFF-MISSING", "Terminal/lane mismatch has no frozen handoff allocation; compiler will not synthesize one.", true, route.PhysicalLinkId));
                return null;
            }
            result.Add(MaterialiseHandoffPoint(handoff, destination, route, rows, columns, previous));
        }
        result.Add(last);
        return result;
    }

    private static CompiledPoint MaterialiseHandoffPoint(ArchitectureV7EndpointHandoff handoff, ArchitectureV7PhysicalTerminal terminal,
        ArchitectureV7LogicalRoute route, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        CompiledPoint adjacentPoint)
    {
        if (handoff.LogicalCell is not { } cell || (uint)cell.Row >= (uint)rows.Count || (uint)cell.Column >= (uint)columns.Count)
            throw new InvalidOperationException("A frozen endpoint handoff has no representable logical cell.");
        var point = handoff.HandoffOrientation == ArchitectureV7RunOrientation.Vertical
            ? new ArchitectureV7PhysicalPoint(adjacentPoint.Point.X, terminal.Position.Y, "frozen-" + handoff.ResourceId)
            : new ArchitectureV7PhysicalPoint(terminal.Position.X, adjacentPoint.Point.Y, "frozen-" + handoff.ResourceId);
        return new(handoff.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? 0 : route.Cells.Count - 1,
            handoff.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Cells[0] : route.Cells[route.Cells.Count - 1], point,
            adjacentPoint.RunId, adjacentPoint.LaneId, "handoff-resource=" + handoff.ResourceId + ";" + handoff.Provenance);
    }

    private static CompiledPoint? PointFor(ArchitectureV7LogicalRoute route, int index, CompilationIndexes indexes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var incoming = indexes.IncomingRunsByLinkAndRouteIndex[(route.PhysicalLinkId, index)];
        var outgoing = indexes.OutgoingRunsByLinkAndRouteIndex[(route.PhysicalLinkId, index)];
        var incomingLane = indexes.AssignmentsByRunId[incoming.RunId];
        var outgoingLane = indexes.AssignmentsByRunId[outgoing.RunId];
        var isTurn = incoming.Orientation != outgoing.Orientation;
        if (isTurn)
        {
            var bend = indexes.BendsByLinkAndRouteIndex.TryGetValue((route.PhysicalLinkId, index), out var indexedBend)
                ? indexedBend : null;
            if (bend is null)
            {
                diagnostics.Add(new("BEND-RESOURCE-MISSING", "A frozen logical turn has no allocated bend resource; compiler will not invent one.", true, route.PhysicalLinkId));
                return null;
            }
            return PointAtCell(route, index, bend.EffectiveRelativePosition,
                outgoing.RunId, outgoingLane.LaneId, "bend-resource=" + bend.BendId + ";" + bend.Provenance, rows, columns);
        }

        // Straight traversal is owned by the maximal run lane. Crossing
        // resources describe interactions at the frozen intersection; they
        // must not displace the straight run or become a compiler authority.
        var x = incoming.Orientation == ArchitectureV7RunOrientation.Horizontal ? (columns[route.Cells[index].Column].Start + columns[route.Cells[index].Column].End) / 2d : columns[route.Cells[index].Column].LaneCoordinates[incomingLane.LaneOrdinal];
        var y = incoming.Orientation == ArchitectureV7RunOrientation.Vertical ? (rows[route.Cells[index].Row].Start + rows[route.Cells[index].Row].End) / 2d : rows[route.Cells[index].Row].LaneCoordinates[incomingLane.LaneOrdinal];
        return new(index, route.Cells[index], new(x, y, "frozen-track-boundaries;straight-run"), incoming.RunId, incomingLane.LaneId, "straight-run;run=" + incoming.RunId + ";lane=" + incomingLane.LaneId);
    }

    private static CompiledPoint PointAtCell(ArchitectureV7LogicalRoute route, int index, ArchitectureV7PhysicalRelativePosition relative,
        string runId, string laneId, string provenance, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns)
    {
        var cell = route.Cells[index];
        var x = (columns[cell.Column].Start + columns[cell.Column].End) / 2d + relative.XOffset;
        var y = (rows[cell.Row].Start + rows[cell.Row].End) / 2d + relative.YOffset;
        return new(index, cell, new(x, y, provenance), runId, laneId, provenance);
    }

    private static string LaneFor(ArchitectureV7StraightRun run, CompilationIndexes indexes) => indexes.AssignmentsByRunId[run.RunId].LaneId;
    private static int LaneCount(IEnumerable<ArchitectureV7StraightRun> runs, CompilationIndexes indexes) => runs.Select(x => indexes.AssignmentsByRunId[x.RunId].LaneOrdinal).DefaultIfEmpty(-1).Max() + 1;
    private static IReadOnlyList<double> LaneCoordinates(double start, double extent, int count, double spacing)
    {
        if (count <= 0) return Array.Empty<double>();
        var centre = start + extent / 2d;
        return Enumerable.Range(0, count).Select(index => centre + (index - (count - 1) / 2d) * spacing).ToArray();
    }
    private static string Fingerprint(IEnumerable<ArchitectureV7PhysicalTrackDimension> rows, IEnumerable<ArchitectureV7PhysicalTrackDimension> columns,
        IEnumerable<ArchitectureV7PhysicalSceneNode> nodes, IEnumerable<ArchitectureV7PhysicalTerminal> terminals, IEnumerable<ArchitectureV7PhysicalRoute> routes,
        IEnumerable<ArchitectureV7PhysicalSceneDiagnostic> diagnostics, params string[] inputs) => string.Join("|", inputs) + ";" + string.Join(";", rows.Select(x => "r" + x.LogicalIndex + ":" + x.Start + ":" + x.End).Concat(columns.Select(x => "c" + x.LogicalIndex + ":" + x.Start + ":" + x.End)).Concat(nodes.Select(x => x.PhysicalNodeId + ":" + x.Bounds)).Concat(terminals.Select(x => x.PhysicalLinkId + ":" + x.Position.X + ":" + x.Position.Y)).Concat(routes.Select(x => x.PhysicalLinkId + ":" + string.Join(",", x.Points.Select(p => p.X + "/" + p.Y))).Concat(diagnostics.Select(x => x.Code))));

    private sealed record CompiledPoint(int RouteIndex, ArchitectureV7RouteCell Cell, ArchitectureV7PhysicalPoint Point, string RunId, string LaneId, string Provenance);

    private sealed class CompilationIndexes
    {
        private CompilationIndexes(
            IReadOnlyDictionary<string, ArchitectureV7StraightRun> runsById,
            IReadOnlyDictionary<string, ArchitectureV7RunLaneAssignment> assignmentsByRunId,
            IReadOnlyDictionary<string, IReadOnlyList<ArchitectureV7StraightRun>> runsByPhysicalLinkId,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> incomingRuns,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> outgoingRuns,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7BendAllocation> bends,
            IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointHandoff> handoffs,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7CrossingAllocation> crossings,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), IReadOnlyList<ArchitectureV7CrossingInteraction>> crossingInteractions)
        {
            RunsById = runsById; AssignmentsByRunId = assignmentsByRunId; RunsByPhysicalLinkId = runsByPhysicalLinkId;
            IncomingRunsByLinkAndRouteIndex = incomingRuns; OutgoingRunsByLinkAndRouteIndex = outgoingRuns;
            BendsByLinkAndRouteIndex = bends; HandoffsByEndpoint = handoffs; CrossingsByLinkAndRouteIndex = crossings;
            CrossingInteractionsByLinkAndRouteIndex = crossingInteractions;
        }

        public IReadOnlyDictionary<string, ArchitectureV7StraightRun> RunsById { get; }
        public IReadOnlyDictionary<string, ArchitectureV7RunLaneAssignment> AssignmentsByRunId { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<ArchitectureV7StraightRun>> RunsByPhysicalLinkId { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> IncomingRunsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> OutgoingRunsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7BendAllocation> BendsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointHandoff> HandoffsByEndpoint { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7CrossingAllocation> CrossingsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), IReadOnlyList<ArchitectureV7CrossingInteraction>> CrossingInteractionsByLinkAndRouteIndex { get; }

        public static CompilationIndexes Create(ArchitectureV7CollectiveAllocationFreeze allocation)
        {
            var orderedRuns = allocation.Runs.OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal)
                .ThenBy(x => x.StartRouteIndex).ThenBy(x => x.RunId, StringComparer.Ordinal).ToArray();
            var runsById = orderedRuns.GroupBy(x => x.RunId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var assignments = allocation.RunAssignments.ToDictionary(x => x.RunId, StringComparer.Ordinal);
            var runsByLink = orderedRuns.GroupBy(x => x.PhysicalLinkId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7StraightRun>)Array.AsReadOnly(x.ToArray()), StringComparer.Ordinal);
            var incoming = new Dictionary<(string, int), ArchitectureV7StraightRun>();
            var outgoing = new Dictionary<(string, int), ArchitectureV7StraightRun>();
            foreach (var run in orderedRuns)
            {
                for (var index = run.StartRouteIndex + 1; index <= run.EndRouteIndex; index++)
                    if (!incoming.ContainsKey((run.PhysicalLinkId, index))) incoming[(run.PhysicalLinkId, index)] = run;
                for (var index = run.StartRouteIndex; index < run.EndRouteIndex; index++)
                    if (!outgoing.ContainsKey((run.PhysicalLinkId, index))) outgoing[(run.PhysicalLinkId, index)] = run;
            }
            var bends = allocation.Bends.OrderBy(x => x.BendId, StringComparer.Ordinal)
                .GroupBy(x => (x.PhysicalLinkId, x.RouteIndex)).ToDictionary(x => x.Key, x => x.First());
            var handoffs = allocation.Handoffs.OrderBy(x => x.EndpointKind).ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                .GroupBy(x => (x.PhysicalLinkId, x.EndpointKind)).ToDictionary(x => x.Key, x => x.First());
            var crossings = allocation.Crossings.OrderBy(x => x.CrossingId, StringComparer.Ordinal)
                .SelectMany(x => CrossingKeys(x).Select(key => (key, x))).GroupBy(x => x.key)
                .ToDictionary(x => x.Key, x => x.First().x);
            var interactions = allocation.CrossingInteractions.SelectMany(x => InteractionKeys(x).Select(key => (key, x)))
                .GroupBy(x => x.key).ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7CrossingInteraction>)Array.AsReadOnly(x.Select(item => item.x).OrderBy(item => item.InteractionId, StringComparer.Ordinal).ToArray()));
            return new(
                new ReadOnlyDictionary<string, ArchitectureV7StraightRun>(runsById),
                new ReadOnlyDictionary<string, ArchitectureV7RunLaneAssignment>(assignments),
                new ReadOnlyDictionary<string, IReadOnlyList<ArchitectureV7StraightRun>>(runsByLink),
                new ReadOnlyDictionary<(string, int), ArchitectureV7StraightRun>(incoming),
                new ReadOnlyDictionary<(string, int), ArchitectureV7StraightRun>(outgoing),
                new ReadOnlyDictionary<(string, int), ArchitectureV7BendAllocation>(bends),
                new ReadOnlyDictionary<(string, ArchitectureV7EndpointKind), ArchitectureV7EndpointHandoff>(handoffs),
                new ReadOnlyDictionary<(string, int), ArchitectureV7CrossingAllocation>(crossings),
                new ReadOnlyDictionary<(string, int), IReadOnlyList<ArchitectureV7CrossingInteraction>>(interactions));

            static IEnumerable<(string, int)> CrossingKeys(ArchitectureV7CrossingAllocation crossing)
            {
                if (crossing.HorizontalRouteIndex >= 0) yield return (crossing.HorizontalPhysicalLinkId, crossing.HorizontalRouteIndex);
                if (crossing.VerticalRouteIndex >= 0) yield return (crossing.VerticalPhysicalLinkId, crossing.VerticalRouteIndex);
            }
            static IEnumerable<(string, int)> InteractionKeys(ArchitectureV7CrossingInteraction interaction)
            {
                yield return (interaction.HorizontalPhysicalLinkId, interaction.HorizontalRouteIndex);
                yield return (interaction.VerticalPhysicalLinkId, interaction.VerticalRouteIndex);
            }
        }
    }
}
