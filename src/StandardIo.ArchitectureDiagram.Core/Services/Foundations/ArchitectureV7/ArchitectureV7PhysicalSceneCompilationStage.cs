using System;
using System.Collections.Generic;
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
        var rowCount = Math.Max(placement.DiagramGrid.RowCount, placement.Nodes.Count == 0 ? 0 : placement.Nodes.Max(x => x.DiagramRow) + 1);
        var columnCount = Math.Max(placement.DiagramGrid.ColumnCount, placement.Nodes.Count == 0 ? 0 : placement.Nodes.Max(x => x.DiagramColumn + x.LogicalSpan));
        var rows = SizeRows(rowCount, placement, allocation, configuration);
        var columns = SizeColumns(columnCount, placement, allocation, configuration, preRoutingWidthRequirements);
        var nodes = MaterialiseNodes(placement, rows, columns, configuration, diagnostics);
        var terminals = MaterialiseTerminals(allocation, nodes, configuration, diagnostics);
        var routesOutput = MaterialiseRoutes(routes, allocation, rows, columns, nodes, terminals, diagnostics);
        var fingerprint = Fingerprint(rows, columns, nodes, terminals, routesOutput, diagnostics, placement.PlacementFingerprint, routes.RouteFingerprint, allocation.AllocationFingerprint);
        return new ArchitectureV7PhysicalSceneFreeze(rows, columns, nodes, terminals, routesOutput, diagnostics,
            placement.PlacementFingerprint, routes.RouteFingerprint, allocation.AllocationFingerprint, fingerprint);
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTrackDimension> SizeRows(int count, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneConfiguration configuration)
    {
        var extents = Enumerable.Range(0, count).Select(row => ArchitectureV7PhysicalSceneSizing.RowMinimum(row, placement, configuration)).ToArray();
        for (var row = 0; row < extents.Length; row++)
            if (ArchitectureV7PhysicalSceneSizing.IsRoutingRow(row, placement))
                extents[row] = Math.Max(extents[row], ArchitectureV7PhysicalSceneSizing.NodeRoutingInterfaceCount(row, placement) * configuration.NodeClearance);
        foreach (var group in allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal).GroupBy(x => x.Cells[0].Row))
        {
            if ((uint)group.Key >= (uint)extents.Length) continue;
            var laneCount = group.Select(x => allocation.RunAssignments.First(a => a.RunId == x.RunId).LaneOrdinal).DefaultIfEmpty(0).Max() + 1;
            var laneEnvelope = ArchitectureV7PhysicalSceneSizing.LaneEnvelope(laneCount, configuration);
            var interfaceClearance = ArchitectureV7PhysicalSceneSizing.NodeRoutingInterfaceCount(group.Key, placement) * configuration.NodeClearance;
            extents[group.Key] = Math.Max(extents[group.Key], laneEnvelope + interfaceClearance);
        }
        var result = new List<ArchitectureV7PhysicalTrackDimension>(count);
        var cursor = 0d;
        for (var index = 0; index < count; index++)
        {
            var laneCount = LaneCount(allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Horizontal && x.Cells[0].Row == index), allocation);
            var laneCoordinates = LaneCoordinates(cursor, extents[index], laneCount, configuration.ParallelLaneSpacing);
            result.Add(new(index, cursor, cursor + extents[index], extents[index], laneCoordinates));
            cursor += extents[index];
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTrackDimension> SizeColumns(int count, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneConfiguration configuration,
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
            var laneCount = group.Select(x => allocation.RunAssignments.First(a => a.RunId == x.RunId).LaneOrdinal).DefaultIfEmpty(0).Max() + 1;
            extents[group.Key] = Math.Max(extents[group.Key], 2 * configuration.RouteClearance + Math.Max(0, laneCount - 1) * configuration.ParallelLaneSpacing);
        }
        var result = new List<ArchitectureV7PhysicalTrackDimension>(count);
        var cursor = 0d;
        for (var index = 0; index < count; index++)
        {
            var laneCount = LaneCount(allocation.Runs.Where(x => x.Orientation == ArchitectureV7RunOrientation.Vertical && x.Cells[0].Column == index), allocation);
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
            result.Add(new(node.PhysicalNodeId, new(columns[minColumn].Start, rows[minRow].Start, columns[maxColumn].End, rows[maxRow].End), "frozen-logical-footprint;physical-track-boundaries"));
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
        ArchitectureV7LogicalRouteFreeze routes, ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes, IReadOnlyList<ArchitectureV7PhysicalTerminal> terminals,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7PhysicalRoute>();
        foreach (var route in routes.Routes)
        {
            var routeRuns = allocation.Runs.Where(x => x.PhysicalLinkId == route.PhysicalLinkId).OrderBy(x => x.StartRouteIndex).ToArray();
            var routeTerminals = terminals.Where(x => x.PhysicalLinkId == route.PhysicalLinkId).ToArray();
            var source = routeTerminals.FirstOrDefault(x => x.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var destination = routeTerminals.FirstOrDefault(x => x.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
            if (!route.IsComplete || source is null || destination is null || route.Cells.Count < 3 || routeRuns.Length == 0)
            {
                diagnostics.Add(new("ROUTE-COMPILATION-FAILED", "A frozen route cannot be mechanically compiled without repair.", true, route.PhysicalLinkId));
                continue;
            }
            var points = new List<CompiledPoint> { new(0, route.Cells[0], source.Position, routeRuns[0].RunId, LaneFor(routeRuns[0], allocation), "source-terminal") };
            for (var index = 1; index < route.Cells.Count - 1; index++) points.Add(PointFor(route, index, routeRuns, allocation, rows, columns));
            points.Add(new(route.Cells.Count - 1, route.Cells[route.Cells.Count - 1], destination.Position, routeRuns[routeRuns.Length - 1].RunId, LaneFor(routeRuns[routeRuns.Length - 1], allocation), "destination-terminal"));
            var expanded = AddAllocatedEndpointHandoffs(points, route, allocation, source, destination, diagnostics);
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

    private static List<CompiledPoint> AddAllocatedEndpointHandoffs(List<CompiledPoint> points, ArchitectureV7LogicalRoute route,
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalTerminal source, ArchitectureV7PhysicalTerminal destination,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<CompiledPoint> { points[0] };
        var first = points[1];
        if (source.Position.X != first.Point.X && source.Position.Y != first.Point.Y)
        {
            if (!allocation.Handoffs.Any(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture)) diagnostics.Add(new("ENDPOINT-HANDOFF-MISSING", "Terminal/lane mismatch has no frozen handoff allocation.", true, route.PhysicalLinkId));
            else result.Add(new(0, route.Cells[0], new(first.Point.X, source.Position.Y, "allocated-source-handoff"), first.RunId, first.LaneId, "allocated-orthogonal-source-handoff"));
        }
        result.AddRange(points.Skip(1).Take(points.Count - 2));
        var last = points[points.Count - 1]; var previous = result[result.Count - 1];
        if (previous.Point.X != destination.Position.X && previous.Point.Y != destination.Position.Y)
        {
            if (!allocation.Handoffs.Any(x => x.PhysicalLinkId == route.PhysicalLinkId && x.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival)) diagnostics.Add(new("ENDPOINT-HANDOFF-MISSING", "Terminal/lane mismatch has no frozen handoff allocation.", true, route.PhysicalLinkId));
            else result.Add(new(route.Cells.Count - 1, route.Cells[route.Cells.Count - 1], new(destination.Position.X, previous.Point.Y, "allocated-destination-handoff"), last.RunId, last.LaneId, "allocated-orthogonal-destination-handoff"));
        }
        result.Add(last);
        return result;
    }

    private static CompiledPoint PointFor(ArchitectureV7LogicalRoute route, int index, IReadOnlyList<ArchitectureV7StraightRun> runs,
        ArchitectureV7CollectiveAllocationFreeze allocation, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns)
    {
        var incoming = runs.First(x => x.StartRouteIndex <= index - 1 && x.EndRouteIndex >= index);
        var outgoing = runs.First(x => x.StartRouteIndex <= index && x.EndRouteIndex >= index + 1);
        var incomingLane = allocation.RunAssignments.First(x => x.RunId == incoming.RunId);
        var outgoingLane = allocation.RunAssignments.First(x => x.RunId == outgoing.RunId);
        var x = incoming.Orientation == ArchitectureV7RunOrientation.Horizontal ? (columns[route.Cells[index].Column].Start + columns[route.Cells[index].Column].End) / 2d : columns[route.Cells[index].Column].LaneCoordinates[incomingLane.LaneOrdinal];
        var y = incoming.Orientation == ArchitectureV7RunOrientation.Vertical ? (rows[route.Cells[index].Row].Start + rows[route.Cells[index].Row].End) / 2d : rows[route.Cells[index].Row].LaneCoordinates[incomingLane.LaneOrdinal];
        if (incoming.Orientation != outgoing.Orientation)
        {
            x = outgoing.Orientation == ArchitectureV7RunOrientation.Vertical ? columns[route.Cells[index].Column].LaneCoordinates[outgoingLane.LaneOrdinal] : x;
            y = outgoing.Orientation == ArchitectureV7RunOrientation.Horizontal ? rows[route.Cells[index].Row].LaneCoordinates[outgoingLane.LaneOrdinal] : y;
        }
        return new(index, route.Cells[index], new(x, y, "logical-cell;run-transition"), outgoing.RunId, outgoingLane.LaneId, incoming.RunId == outgoing.RunId ? "straight-run" : "allocated-bend-transition");
    }

    private static string LaneFor(ArchitectureV7StraightRun run, ArchitectureV7CollectiveAllocationFreeze allocation) => allocation.RunAssignments.First(x => x.RunId == run.RunId).LaneId;
    private static int LaneCount(IEnumerable<ArchitectureV7StraightRun> runs, ArchitectureV7CollectiveAllocationFreeze allocation) => runs.Select(x => allocation.RunAssignments.First(a => a.RunId == x.RunId).LaneOrdinal).DefaultIfEmpty(-1).Max() + 1;
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
}
