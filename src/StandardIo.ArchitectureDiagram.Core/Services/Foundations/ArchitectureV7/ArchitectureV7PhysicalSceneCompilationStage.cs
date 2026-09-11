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
        ArchitectureV7PhysicalSceneConfiguration configuration)
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
        var indexes = CompilationIndexes.Create(allocation);
        var rows = SizeRows(rowCount, placement, allocation, indexes, configuration);
        var columns = SizeColumns(columnCount, placement, allocation, indexes, configuration);
        ValidateVerticalLaneContainment(columns, allocation, indexes, diagnostics);
        var nodes = MaterialiseNodes(placement, rows, columns, configuration, diagnostics);
        var projectBounds = MaterialiseProjectBounds(placement, rows, columns, diagnostics);
        var endpointAllocation = new ArchitectureV7EndpointGeometryAllocationStage().Allocate(placement, routes, allocation, rows, columns, nodes);
        var endpointIndexes = CompilationIndexes.Create(endpointAllocation);
        var terminals = MaterialiseTerminals(endpointAllocation, routes, nodes, rows, columns, configuration, diagnostics);
        ValidateEndpointLaneAuthority(endpointAllocation, endpointIndexes, nodes, columns, terminals, diagnostics);
        var routesOutput = MaterialiseRoutes(routes, endpointAllocation, endpointIndexes, rows, columns, nodes, terminals, configuration, diagnostics);
        var fingerprint = Fingerprint(rows, columns, nodes, terminals, routesOutput, diagnostics, placement.PlacementFingerprint, routes.RouteFingerprint, endpointAllocation.AllocationFingerprint);
        return new ArchitectureV7PhysicalSceneFreeze(rows, columns, nodes, terminals, routesOutput, diagnostics,
            placement.PlacementFingerprint, routes.RouteFingerprint, endpointAllocation.AllocationFingerprint, fingerprint,
            routes.Routes.Select(x => x.PhysicalLinkId).ToArray(), projectBounds);
    }

    private static IReadOnlyList<ArchitectureV7PhysicalProjectBounds> MaterialiseProjectBounds(
        ArchitectureV7PlacementFreeze placement,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7PhysicalProjectBounds>();
        foreach (var project in placement.Projects)
        {
            var transform = project.Transform;
            var rightColumn = transform.RegionOriginColumn + transform.Width - 1;
            var bottomRow = transform.RegionOriginRow + transform.Height - 1;
            if (transform.RegionOriginRow < 0 || transform.RegionOriginColumn < 0 || bottomRow >= rows.Count || rightColumn >= columns.Count)
            {
                diagnostics.Add(new("PROJECT-BOUNDS-OUT-OF-RANGE", $"Project {project.ProjectId} has no representable final physical bounds.", true));
                continue;
            }
            result.Add(new(project.ProjectId,
                new ArchitectureV7PhysicalBounds(columns[transform.RegionOriginColumn].Start, rows[transform.RegionOriginRow].Start,
                    columns[rightColumn].End, rows[bottomRow].End),
                "placement-transform;physical-row-column-tables;includes-header-boundary-surround"));
        }
        return result;
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
            var demands = allocation.TrackDemands.Where(demand => demand.LogicalRow == index && demand.RequiredRowExtent > 0).ToArray();
            var minimumOffset = demands.Length == 0 ? 0 : demands.Min(demand => demand.RequiredRowMinimumOffset);
            var maximumOffset = demands.Length == 0 ? Math.Max(0, laneCount - 1) * configuration.ParallelLaneSpacing : demands.Max(demand => demand.RequiredRowMaximumOffset);
            var clearance = demands.Length == 0 ? configuration.RouteClearance : demands.Max(demand => demand.RequiredRowExtent - (demand.RequiredRowMaximumOffset - demand.RequiredRowMinimumOffset)) / 2d;
            var laneCoordinates = HorizontalLaneCoordinates(cursor, extents[index], laneCount, configuration.ParallelLaneSpacing, minimumOffset, clearance);
            result.Add(new(index, cursor, cursor + extents[index], extents[index], laneCoordinates));
            cursor += extents[index];
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTrackDimension> SizeColumns(int count, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7CollectiveAllocationFreeze allocation, CompilationIndexes indexes, ArchitectureV7PhysicalSceneConfiguration configuration)
    {
        var extents = Enumerable.Repeat(configuration.BaseCellWidth, count).ToArray();
        // Node capacity is decided before routing by logical cell span. Do not
        // widen one physical track here to satisfy a pixel requirement; that
        // creates cell over-allocation after the span freeze.
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
            var demands = allocation.TrackDemands.Where(demand => demand.LogicalColumn == index && demand.RequiredColumnExtent > 0).ToArray();
            var laneClearance = demands.Length == 0
                ? configuration.RouteClearance
                : demands.Max(demand => Math.Max(0, (demand.RequiredColumnExtent - (demand.RequiredColumnMaximumOffset - demand.RequiredColumnMinimumOffset)) / 2d));
            result.Add(new(index, cursor, cursor + extents[index], extents[index], LaneCoordinates(cursor, extents[index], laneCount, configuration.ParallelLaneSpacing, laneClearance)));
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
            var reservedLeft = columns[minColumn].Start;
            var reservedRight = columns[maxColumn].End;
            var occupiedCellCount = maxColumn - minColumn + 1;
            var occupiedWidth = occupiedCellCount * configuration.BaseCellWidth;
            var centre = (reservedLeft + reservedRight) / 2d;
            var visibleLeft = centre - occupiedWidth / 2d;
            var visibleRight = centre + occupiedWidth / 2d;
            result.Add(new(node.PhysicalNodeId, new(visibleLeft, rowTop, visibleRight, rowBottom),
                "frozen-logical-footprint;node-row-clearance-envelope;logical-cell-occupancy-width;centred-in-physical-footprint"));
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7PhysicalTerminal> MaterialiseTerminals(
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ArchitectureV7PhysicalSceneConfiguration configuration, ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<ArchitectureV7PhysicalTerminal>();
        foreach (var slot in allocation.Terminals)
        {
            var node = nodes.FirstOrDefault(x => x.PhysicalNodeId == slot.PhysicalNodeId);
            if (node is null) { diagnostics.Add(new("TERMINAL-NODE-MISSING", "A frozen terminal has no materialised node bounds.", true, slot.PhysicalLinkId)); continue; }
            // Left/direct/right are ordering groups on the top/bottom edge.
            // A complete maximal vertical run adds a relationship-level
            // coordinate constraint: both terminals inherit the allocated run
            // X. This is consumption of allocation authority, not compiler
            // alignment or topology repair.
            var shared = allocation.SharedVerticalRunConstraints.FirstOrDefault(item => item.PhysicalLinkId == slot.PhysicalLinkId);
            var displacedEndpointZ = allocation.EndpointZBends.Any(item => item.PhysicalLinkId == slot.PhysicalLinkId && item.EndpointKind == slot.EndpointKind);
            var corridor = allocation.EndpointCorridors.FirstOrDefault(c => c.PhysicalLinkId == slot.PhysicalLinkId && c.EndpointKind == slot.EndpointKind);
            if (corridor is not null && (Math.Abs(corridor.X - Math.Round((node.Bounds.Left + node.Bounds.Right) / 2d + slot.RelativeOffset, MidpointRounding.AwayFromZero)) > .001 ||
                corridor.SignedSlot != slot.SignedSlotOrdinal))
                diagnostics.Add(new("TERMINAL-FINAL-LANE-AUTHORITY-VIOLATION", "Frozen terminal and endpoint corridor disagree; neither coordinate is repaired.", true, slot.PhysicalLinkId));
            var x = corridor is not null ? corridor.X : shared is null || displacedEndpointZ
                ? Math.Round((node.Bounds.Left + node.Bounds.Right) / 2d + slot.RelativeOffset, MidpointRounding.AwayFromZero)
                : SharedRunX(shared, allocation, nodes, columns, slot.PhysicalLinkId, diagnostics);
            var y = Math.Round(slot.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? node.Bounds.Bottom : node.Bounds.Top, MidpointRounding.AwayFromZero);
            if (x < node.Bounds.Left + configuration.TerminalInset || x > node.Bounds.Right - configuration.TerminalInset)
                diagnostics.Add(new("TERMINAL-OUT-OF-BOUNDS", "A frozen terminal slot does not fit; terminal clamping is forbidden.", true, slot.PhysicalLinkId));
            var provenance = (shared is null || displacedEndpointZ
                ? "node-edge;terminal-authoritative;frozen-slot-ordinal;configured-spacing-inset"
                : "node-edge;terminal-authoritative;shared-maximal-vertical-run;allocated-run-X;run=" + shared.RunId)
                + (displacedEndpointZ ? ";endpoint-z-bend;fixed-incoming-x" : string.Empty)
                + ";direction=" + slot.Direction + ";" + slot.Provenance;
            result.Add(new(slot.PhysicalLinkId, slot.PhysicalNodeId, slot.EndpointKind, slot.SlotOrdinal, new(x, y, "terminal-edge+frozen-slot"), provenance));
        }
        ValidateSharedRunLegalRanges(allocation, nodes, result, configuration, diagnostics);
        return result;
    }

    private static void ValidateEndpointLaneAuthority(
        ArchitectureV7CollectiveAllocationFreeze allocation,
        CompilationIndexes indexes,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalTerminal> terminals,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        foreach (var coordinate in allocation.EndpointLaneCoordinates)
        {
            var node = nodes.FirstOrDefault(item => item.PhysicalNodeId == coordinate.PhysicalNodeId);
            var terminal = terminals.FirstOrDefault(item => item.PhysicalLinkId == coordinate.PhysicalLinkId && item.EndpointKind == coordinate.EndpointKind);
            if (node is null || terminal is null) continue;
            var displacedEndpointZ = allocation.EndpointZBends.Any(item => item.PhysicalLinkId == coordinate.PhysicalLinkId && item.EndpointKind == coordinate.EndpointKind);
            var corridor = allocation.EndpointCorridors.FirstOrDefault(c => c.PhysicalLinkId == coordinate.PhysicalLinkId && c.EndpointKind == coordinate.EndpointKind);
            var expectedX = corridor is null && !displacedEndpointZ && indexes.SharedVerticalRunsByLinkId.TryGetValue(coordinate.PhysicalLinkId, out var shared)
                ? SharedRunX(shared, allocation, nodes, columns, coordinate.PhysicalLinkId, diagnostics)
                : Math.Round((node.Bounds.Left + node.Bounds.Right) / 2d + coordinate.RelativeXOffset, MidpointRounding.AwayFromZero);
            if (Math.Abs(expectedX - terminal.Position.X) > .001)
                diagnostics.Add(new("TERMINAL-FINAL-LANE-AUTHORITY-VIOLATION",
                    $"The allocated endpoint lane coordinate does not equal the allocated terminal coordinate. expectedX={expectedX:R};terminalX={terminal.Position.X:R};relativeOffset={coordinate.RelativeXOffset:R};nodeBounds={node.Bounds.Left:R},{node.Bounds.Right:R}.", true,
                    coordinate.PhysicalLinkId));
        }
    }

    private static double SharedRunX(ArchitectureV7SharedVerticalRunConstraint constraint,
        ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        string physicalLinkId,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var run = allocation.Runs.FirstOrDefault(item => item.RunId == constraint.RunId);
        var assignment = run is null ? null : allocation.RunAssignments.FirstOrDefault(item => item.RunId == run.RunId);
        if (run is not null && assignment is not null && (uint)run.Cells[0].Column < (uint)columns.Count &&
            (uint)assignment.LaneOrdinal < (uint)columns[run.Cells[0].Column].LaneCoordinates.Count)
            return columns[run.Cells[0].Column].LaneCoordinates[assignment.LaneOrdinal];

        var sourceSlot = allocation.Terminals.FirstOrDefault(item => item.PhysicalLinkId == physicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
        var sourceNode = sourceSlot is null ? null : nodes.FirstOrDefault(item => item.PhysicalNodeId == sourceSlot.PhysicalNodeId);
        if (sourceSlot is null || sourceNode is null)
        {
            diagnostics.Add(new("SHARED-VERTICAL-RUN-RESOURCE-MISSING", "A shared vertical run constraint has no allocated physical lane or source terminal coordinate.", true, physicalLinkId));
            return double.NaN;
        }
        // Legacy fallback is retained only for malformed synthetic freezes
        // that omit the constrained run assignment.
        return Math.Round((sourceNode.Bounds.Left + sourceNode.Bounds.Right) / 2d + sourceSlot.RelativeOffset, MidpointRounding.AwayFromZero);
    }

    private static void ValidateSharedRunLegalRanges(
        ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalTerminal> terminals,
        ArchitectureV7PhysicalSceneConfiguration configuration,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        foreach (var constraint in allocation.SharedVerticalRunConstraints)
        {
            var endpoints = terminals.Where(item => item.PhysicalLinkId == constraint.PhysicalLinkId).ToArray();
            if (endpoints.Length != 2) continue;
            endpoints = endpoints.Where(endpoint => !allocation.EndpointZBends.Any(z => z.PhysicalLinkId == endpoint.PhysicalLinkId && z.EndpointKind == endpoint.EndpointKind)
                && !allocation.EndpointCorridors.Any(c => c.PhysicalLinkId == endpoint.PhysicalLinkId && c.EndpointKind == endpoint.EndpointKind)).ToArray();
            if (endpoints.Length < 2) continue;
            var intervals = endpoints.Select(endpoint =>
            {
                var node = nodes.FirstOrDefault(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
                if (node is null) return (Endpoint: endpoint, Lower: double.NaN, Upper: double.NaN);
                var lower = node.Bounds.Left + configuration.TerminalInset;
                var upper = node.Bounds.Right - configuration.TerminalInset;
                var peers = terminals.Where(item => item.PhysicalNodeId == endpoint.PhysicalNodeId && item.EndpointKind == endpoint.EndpointKind && item.PhysicalLinkId != endpoint.PhysicalLinkId).ToArray();
                lower = Math.Max(lower, peers.Where(peer => peer.SlotOrdinal < endpoint.SlotOrdinal).Select(peer => peer.Position.X + configuration.TerminalPortSpacing).DefaultIfEmpty(lower).Max());
                upper = Math.Min(upper, peers.Where(peer => peer.SlotOrdinal > endpoint.SlotOrdinal).Select(peer => peer.Position.X - configuration.TerminalPortSpacing).DefaultIfEmpty(upper).Min());
                return (Endpoint: endpoint, Lower: lower, Upper: upper);
            }).ToArray();
            var intersectionLower = intervals.Max(item => item.Lower);
            var intersectionUpper = intervals.Min(item => item.Upper);
            var x = endpoints[0].Position.X;
            if (double.IsNaN(x) || intersectionLower > intersectionUpper || x < intersectionLower || x > intersectionUpper)
                diagnostics.Add(new("SHARED-VERTICAL-RUN-CONSTRAINT-UNSATISFIED",
                    $"The allocated shared vertical run X={x:R} is outside the endpoint legal intersection [{intersectionLower:R},{intersectionUpper:R}].", true,
                    constraint.PhysicalLinkId));
        }
    }

    private static IReadOnlyList<ArchitectureV7PhysicalRoute> MaterialiseRoutes(
        ArchitectureV7LogicalRouteFreeze routes, ArchitectureV7CollectiveAllocationFreeze allocation, CompilationIndexes indexes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalTerminal> terminals,
        ArchitectureV7PhysicalSceneConfiguration configuration,
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
            var points = new List<CompiledPoint> { new(0, route.Cells[0], source.Position, TerminalResourceId(source), TerminalResourceId(source), "source-terminal") };
            var routeFailed = false;
            for (var index = 1; index < route.Cells.Count - 1; index++)
            {
                var transitions = allocation.EndpointCorridors.Where(c => c.PhysicalLinkId == route.PhysicalLinkId && c.RouteIndex == index &&
                    allocation.Runs.Any(r => r.RunId == c.HorizontalRunId && r.IsEndpointTransition)).ToArray();
                if (transitions.Length > 0)
                {
                    foreach (var corridor in transitions.OrderBy(c => c.EndpointKind))
                    {
                        var horizontalLane = indexes.AssignmentsByRunId[corridor.HorizontalRunId];
                        var verticalLane = indexes.AssignmentsByRunId[corridor.OrdinaryRunId];
                        var endpointPoint = new CompiledPoint(index, route.Cells[index], new(corridor.X, corridor.RoutingY, corridor.Provenance),
                            corridor.ResourceId, corridor.ResourceId, "allocated-endpoint-corridor;" + corridor.Provenance);
                        var ordinaryPoint = new CompiledPoint(index, route.Cells[index], new(corridor.OrdinaryX, corridor.RoutingY, "straight-run-boundary;" + corridor.Provenance),
                            corridor.OrdinaryRunId, verticalLane.LaneId, "allocated-transition-bend;straight-run-boundary;horizontal-run=" + corridor.HorizontalRunId + ";lane=" + horizontalLane.LaneId);
                        if (corridor.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture)
                        { points.Add(endpointPoint); points.Add(ordinaryPoint); }
                        else { points.Add(ordinaryPoint); points.Add(endpointPoint); }
                    }
                    continue;
                }
                var point = PointFor(route, index, indexes, rows, columns, nodes, configuration, diagnostics);
                if (point is null) { routeFailed = true; break; }
                points.Add(point);
            }
            if (routeFailed)
            {
                diagnostics.Add(new("ROUTE-COMPILATION-FAILED", "A frozen route could not be compiled from its authoritative allocation.", true, route.PhysicalLinkId));
                continue;
            }
            points.Add(new(route.Cells.Count - 1, route.Cells[route.Cells.Count - 1], destination.Position, TerminalResourceId(destination), TerminalResourceId(destination), "destination-terminal"));
            // Keep the frozen run lane authoritative through the endpoint approach.
            // A terminal/run mismatch is represented by the allocated handoff below;
            // rewriting the whole approach to the terminal coordinate can collapse
            // two distinct vertical lanes into one physical interval.
            var expanded = AddAllocatedEndpointBoundaryGeometry(points, route, indexes, source, destination, rows, columns, diagnostics);
            if (expanded is null)
            {
                diagnostics.Add(new("ROUTE-COMPILATION-FAILED", "A frozen endpoint resource could not be compiled without reconciliation or repair.", true, route.PhysicalLinkId));
                continue;
            }
            // Coincident allocated vertices have no intervening geometry.
            // Keep their provenance without emitting zero-length segments.
            for (var i = expanded.Count - 2; i >= 0; i--)
                if (expanded[i].Point.X == expanded[i + 1].Point.X && expanded[i].Point.Y == expanded[i + 1].Point.Y)
                {
                    expanded[i] = expanded[i + 1] with { Provenance = expanded[i].Provenance + ";coincident-resource=" + expanded[i + 1].RunId + ";" + expanded[i + 1].Provenance };
                    expanded.RemoveAt(i + 1);
                }
            var physicalPoints = expanded.Select(x => x.Point).ToArray();
            if (physicalPoints.Zip(physicalPoints.Skip(1), (a, b) => (a, b))
                .Any(pair => pair.a.X != pair.b.X && pair.a.Y != pair.b.Y))
            {
                diagnostics.Add(new("DIAGONAL-PHYSICAL-SEGMENT", "The frozen physical polyline contains a non-orthogonal consecutive point pair.", true, route.PhysicalLinkId));
                continue;
            }
            var segments = new List<ArchitectureV7PhysicalSegment>();
            for (var index = 0; index + 1 < expanded.Count; index++)
            {
                var a = expanded[index]; var b = expanded[index + 1];
                if (a.Point.X != b.Point.X && a.Point.Y != b.Point.Y) { diagnostics.Add(new("DIAGONAL-COMPILER-OUTPUT", "Mechanical compilation produced a diagonal; no repair is permitted.", true, route.PhysicalLinkId)); continue; }
                var resourceOwner = a.Provenance.Contains("frozen-handoff:", StringComparison.Ordinal) ? a : b.Provenance.Contains("frozen-handoff:", StringComparison.Ordinal) ? b :
                    a.RunId.StartsWith("endpoint-z-bend:", StringComparison.Ordinal) ? a : b.RunId.StartsWith("endpoint-z-bend:", StringComparison.Ordinal) ? b : a;
                var ownershipProvenance = resourceOwner.RunId.StartsWith("handoff:", StringComparison.Ordinal) || resourceOwner.RunId.StartsWith("terminal:", StringComparison.Ordinal) || resourceOwner.RunId.StartsWith("endpoint-z-bend:", StringComparison.Ordinal)
                    ? "resource=" + resourceOwner.RunId + ";lane=" + resourceOwner.LaneId
                    : "run=" + resourceOwner.RunId + ";lane=" + resourceOwner.LaneId;
                var corridorOwner = allocation.EndpointCorridors.FirstOrDefault(c => c.PhysicalLinkId == route.PhysicalLinkId &&
                    ((a.Point.X == b.Point.X && a.Point.X == c.X && Math.Min(a.Point.Y, b.Point.Y) >= Math.Min(c.NodeEdgeY, c.RoutingY) && Math.Max(a.Point.Y, b.Point.Y) <= Math.Max(c.NodeEdgeY, c.RoutingY)) ||
                     (a.Point.Y == b.Point.Y && a.Point.Y == c.RoutingY && Math.Min(a.Point.X,b.Point.X) == Math.Min(c.X,c.OrdinaryX) && Math.Max(a.Point.X,b.Point.X) == Math.Max(c.X,c.OrdinaryX))));
                var runId = resourceOwner.RunId;
                var laneId = resourceOwner.LaneId;
                if (corridorOwner is not null)
                {
                    runId = a.Point.X == b.Point.X ? corridorOwner.ResourceId : corridorOwner.HorizontalRunId;
                    laneId = a.Point.X == b.Point.X ? corridorOwner.ResourceId : indexes.AssignmentsByRunId[corridorOwner.HorizontalRunId].LaneId;
                    ownershipProvenance = (a.Point.X == b.Point.X ? "resource=" : "run=") + runId + ";lane=" + laneId;
                }
                segments.Add(new(route.PhysicalLinkId, a.Point, b.Point, new[] { a.RouteIndex, b.RouteIndex }.Distinct().ToArray(), new[] { route.Cells[a.RouteIndex], route.Cells[b.RouteIndex] }.Distinct().ToArray(), runId, laneId,
                    ownershipProvenance + ";start=" + a.Provenance + ";end=" + b.Provenance));
            }
            result.Add(new(route.PhysicalLinkId, physicalPoints, segments, "exact-frozen-route-cell-sequence;unsimplified"));
        }
        return result;
    }

    private static List<CompiledPoint>? AddAllocatedEndpointBoundaryGeometry(List<CompiledPoint> points, ArchitectureV7LogicalRoute route,
        CompilationIndexes indexes, ArchitectureV7PhysicalTerminal source, ArchitectureV7PhysicalTerminal destination,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var result = new List<CompiledPoint> { points[0] };
        var first = points[1];
        if (source.Position.X != first.Point.X && source.Position.Y != first.Point.Y)
        {
            if (indexes.EndpointZBendsByEndpoint.TryGetValue((route.PhysicalLinkId, ArchitectureV7EndpointKind.SourceDeparture), out var sourceZBend))
            {
                result.Add(MaterialiseEndpointZBendPoint(sourceZBend, source, route, first));
            }
            else
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
        }
        result.AddRange(points.Skip(1).Take(points.Count - 2));
        var last = points[points.Count - 1]; var previous = result[result.Count - 1];
        if (previous.Point.X != destination.Position.X && previous.Point.Y != destination.Position.Y)
        {
            if (indexes.EndpointZBendsByEndpoint.TryGetValue((route.PhysicalLinkId, ArchitectureV7EndpointKind.DestinationArrival), out var destinationZBend))
            {
                result.Add(MaterialiseEndpointZBendPoint(destinationZBend, destination, route, previous));
            }
            else
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
        }
        result.Add(last);
        return result;
    }

    private static CompiledPoint MaterialiseEndpointZBendPoint(ArchitectureV7EndpointZBend bend,
        ArchitectureV7PhysicalTerminal terminal, ArchitectureV7LogicalRoute route, CompiledPoint adjacentPoint)
    {
        var point = new ArchitectureV7PhysicalPoint(terminal.Position.X, adjacentPoint.Point.Y,
            "allocated-endpoint-z-bend-" + bend.PhysicalLinkId + "-" + bend.EndpointKind);
        return new(bend.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? 0 : route.Cells.Count - 1,
            bend.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Cells[0] : route.Cells[route.Cells.Count - 1],
            point, "endpoint-z-bend:" + bend.FixedRunId, bend.FixedRunId, "endpoint-z-bend:" + bend.Provenance);
    }

    private static CompiledPoint MaterialiseHandoffPoint(ArchitectureV7EndpointHandoff handoff, ArchitectureV7PhysicalTerminal terminal,
        ArchitectureV7LogicalRoute route, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        CompiledPoint adjacentPoint)
    {
        if (handoff.LogicalCell is not { } cell || (uint)cell.Row >= (uint)rows.Count || (uint)cell.Column >= (uint)columns.Count)
            throw new InvalidOperationException("A frozen endpoint handoff has no representable logical cell.");
        // V7 endpoints are always on the top/bottom node edge. Preserve the
        // adjacent run's perpendicular coordinate, but keep the handoff's X
        // on the authoritative terminal so the final approach cannot drift
        // back to the lane coordinate.
        var point = new ArchitectureV7PhysicalPoint(terminal.Position.X, adjacentPoint.Point.Y, "frozen-" + handoff.ResourceId);
        return new(handoff.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? 0 : route.Cells.Count - 1,
            handoff.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Cells[0] : route.Cells[route.Cells.Count - 1], point,
            handoff.ResourceId, handoff.ResourceId, "frozen-handoff:" + handoff.ResourceId + ";" + handoff.Provenance);
    }

    private static CompiledPoint? PointFor(ArchitectureV7LogicalRoute route, int index, CompilationIndexes indexes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows, IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        ArchitectureV7PhysicalSceneConfiguration configuration,
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
            var verticalRun = incoming.Orientation == ArchitectureV7RunOrientation.Vertical ? incoming : outgoing;
            var horizontalRun = incoming.Orientation == ArchitectureV7RunOrientation.Horizontal ? incoming : outgoing;
            var verticalLane = indexes.AssignmentsByRunId[verticalRun.RunId];
            var horizontalLane = indexes.AssignmentsByRunId[horizontalRun.RunId];
            var bendX = XForVerticalRun(route, index, verticalRun, verticalLane, indexes, columns, nodes, configuration.ParallelLaneSpacing, diagnostics);
            if (bendX is null) return null;
            var bendY = LaneCoordinate(rows[route.Cells[index].Row], horizontalLane, configuration.ParallelLaneSpacing);
            var provenance = "bend-resource=" + bend.BendId + ";" + bend.Provenance +
                ";lane-coordinate;vertical-lane=" + verticalLane.LaneId + ";horizontal-lane=" + horizontalLane.LaneId;
            return new(index, route.Cells[index], new(bendX.Value, bendY, provenance), outgoing.RunId, outgoingLane.LaneId, provenance);
        }

        // Straight traversal is owned by the maximal run lane. Crossing
        // resources describe interactions at the frozen intersection; they
        // must not displace the straight run or become a compiler authority.
        var x = incoming.Orientation == ArchitectureV7RunOrientation.Vertical
            ? XForVerticalRun(route, index, incoming, incomingLane, indexes, columns, nodes, configuration.ParallelLaneSpacing, diagnostics)
            : (double?)((columns[route.Cells[index].Column].Start + columns[route.Cells[index].Column].End) / 2d);
        if (x is null) return null;
        var y = incoming.Orientation == ArchitectureV7RunOrientation.Horizontal ? LaneCoordinate(rows[route.Cells[index].Row], incomingLane, configuration.ParallelLaneSpacing) : (rows[route.Cells[index].Row].Start + rows[route.Cells[index].Row].End) / 2d;
        return new(index, route.Cells[index], new(x.Value, y, "frozen-track-boundaries;straight-run"), incoming.RunId, incomingLane.LaneId, "straight-run;run=" + incoming.RunId + ";lane=" + incomingLane.LaneId);
    }

    private static double? XForVerticalRun(ArchitectureV7LogicalRoute route, int index, ArchitectureV7StraightRun run,
        ArchitectureV7RunLaneAssignment assignment, CompilationIndexes indexes,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyList<ArchitectureV7PhysicalSceneNode> nodes,
        double spacing,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var corridor = indexes.Allocation.EndpointCorridors.FirstOrDefault(c => c.OrdinaryRunId == run.RunId);
        if (corridor is not null)
            return run.EndRouteIndex - run.StartRouteIndex == 1 ? corridor.X : LaneCoordinate(columns[run.Cells[0].Column], assignment, spacing);
        if (indexes.SharedVerticalRunsByLinkId.TryGetValue(route.PhysicalLinkId, out var shared) && shared.RunId == run.RunId)
            return SharedRunX(shared, indexes.Allocation, nodes, columns, route.PhysicalLinkId, diagnostics);
        // Endpoint geometry owns the final vertical approach coordinate. A
        // terminal-anchored endpoint run must retain that coordinate through
        // its allocated approach; shared/fixed-X runs are handled by the
        // authority above.
        var endpointCoordinate = indexes.EndpointLanesByLinkAndKind.Values
            .FirstOrDefault(item => item.RunId == run.RunId);
        // The endpoint-adjacent vertical run shares the terminal's midpoint-
        // relative signed offset. This is allocated endpoint authority, not
        // compiler reconciliation, and prevents a terminal-to-lane sideways
        // segment from being materialised merely because the old positive
        // lane origin differed from the edge origin.
        if (endpointCoordinate is not null && run.EndRouteIndex - run.StartRouteIndex == 1)
        {
            var endpointNode = nodes.FirstOrDefault(item => item.PhysicalNodeId == endpointCoordinate.PhysicalNodeId);
            if (endpointNode is not null)
                return Math.Round((endpointNode.Bounds.Left + endpointNode.Bounds.Right) / 2d + endpointCoordinate.RelativeXOffset, MidpointRounding.AwayFromZero);
        }
        return LaneCoordinate(columns[route.Cells[index].Column], assignment, spacing);
    }

    private static string LaneFor(ArchitectureV7StraightRun run, CompilationIndexes indexes) => indexes.AssignmentsByRunId[run.RunId].LaneId;
    private static string TerminalResourceId(ArchitectureV7PhysicalTerminal terminal) =>
        $"terminal:{terminal.PhysicalLinkId}:{terminal.EndpointKind}:{terminal.SlotOrdinal}";
    private static int LaneCount(IEnumerable<ArchitectureV7StraightRun> runs, CompilationIndexes indexes) => runs.Select(x => indexes.AssignmentsByRunId[x.RunId].LaneOrdinal).DefaultIfEmpty(-1).Max() + 1;
    private static IReadOnlyList<double> LaneCoordinates(double start, double extent, int count, double spacing, double clearance)
    {
        if (count <= 0) return Array.Empty<double>();
        // Vertical lanes share the column midpoint origin with endpoint slots.
        // Allocation ordinals remain stable identities; this is their physical
        // signed midpoint-relative materialisation.
        var midpoint = start + extent / 2d;
        return Enumerable.Range(0, count).Select(index => Math.Round(midpoint + (index - (count - 1) / 2d) * spacing, MidpointRounding.AwayFromZero)).ToArray();
    }

    private static double LaneCoordinate(ArchitectureV7PhysicalTrackDimension track, ArchitectureV7RunLaneAssignment assignment, double spacing) =>
        Math.Round((track.Start + track.End) / 2d + assignment.SignedLaneOrdinal * spacing, MidpointRounding.AwayFromZero);

    private static void ValidateVerticalLaneContainment(IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        ArchitectureV7CollectiveAllocationFreeze allocation, CompilationIndexes indexes,
        ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        foreach (var run in allocation.Runs.Where(run => run.Orientation == ArchitectureV7RunOrientation.Vertical))
        {
            var column = columns.ElementAtOrDefault(run.Cells[0].Column);
            if (column is null) continue;
            var assignment = indexes.AssignmentsByRunId[run.RunId];
            var lane = column.LaneCoordinates.ElementAtOrDefault(assignment.LaneOrdinal);
            if (lane < column.Start || lane > column.End)
                diagnostics.Add(new("VERTICAL-LANE-COLUMN-CONTAINMENT", $"Vertical lane {assignment.LaneId} escapes physical column {column.LogicalIndex}: {lane} not in [{column.Start},{column.End}].", true, run.PhysicalLinkId));
        }
    }

    private static IReadOnlyList<double> HorizontalLaneCoordinates(double start, double extent, int count, double spacing, double minimumOffset, double clearance)
    {
        if (count <= 0) return Array.Empty<double>();
        var midpoint = start + extent / 2d;
        return Enumerable.Range(0, count).Select(index => Math.Round(midpoint + (index - (count - 1) / 2d) * spacing, MidpointRounding.AwayFromZero)).ToArray();
    }
    private static string Fingerprint(IEnumerable<ArchitectureV7PhysicalTrackDimension> rows, IEnumerable<ArchitectureV7PhysicalTrackDimension> columns,
        IEnumerable<ArchitectureV7PhysicalSceneNode> nodes, IEnumerable<ArchitectureV7PhysicalTerminal> terminals, IEnumerable<ArchitectureV7PhysicalRoute> routes,
        IEnumerable<ArchitectureV7PhysicalSceneDiagnostic> diagnostics, params string[] inputs) => string.Join("|", inputs) + ";" + string.Join(";", rows.Select(x => "r" + x.LogicalIndex + ":" + x.Start + ":" + x.End).Concat(columns.Select(x => "c" + x.LogicalIndex + ":" + x.Start + ":" + x.End)).Concat(nodes.Select(x => x.PhysicalNodeId + ":" + x.Bounds)).Concat(terminals.Select(x => x.PhysicalLinkId + ":" + x.Position.X + ":" + x.Position.Y)).Concat(routes.Select(x => x.PhysicalLinkId + ":" + string.Join(",", x.Points.Select(p => p.X + "/" + p.Y))).Concat(diagnostics.Select(x => x.Code))));

    private sealed record CompiledPoint(int RouteIndex, ArchitectureV7RouteCell Cell, ArchitectureV7PhysicalPoint Point, string RunId, string LaneId, string Provenance);

    private sealed class CompilationIndexes
    {
        private CompilationIndexes(
            ArchitectureV7CollectiveAllocationFreeze allocation,
            IReadOnlyDictionary<string, ArchitectureV7StraightRun> runsById,
            IReadOnlyDictionary<string, ArchitectureV7RunLaneAssignment> assignmentsByRunId,
            IReadOnlyDictionary<string, IReadOnlyList<ArchitectureV7StraightRun>> runsByPhysicalLinkId,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> incomingRuns,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> outgoingRuns,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7BendAllocation> bends,
            IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointHandoff> handoffs,
            IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointZBend> endpointZBends,
            IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointLaneCoordinate> endpointLanes,
            IReadOnlyDictionary<string, ArchitectureV7SharedVerticalRunConstraint> sharedVerticalRuns,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7CrossingAllocation> crossings,
            IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), IReadOnlyList<ArchitectureV7CrossingInteraction>> crossingInteractions)
        {
            Allocation = allocation;
            RunsById = runsById; AssignmentsByRunId = assignmentsByRunId; RunsByPhysicalLinkId = runsByPhysicalLinkId;
            IncomingRunsByLinkAndRouteIndex = incomingRuns; OutgoingRunsByLinkAndRouteIndex = outgoingRuns;
            BendsByLinkAndRouteIndex = bends; HandoffsByEndpoint = handoffs; EndpointZBendsByEndpoint = endpointZBends; EndpointLanesByLinkAndKind = endpointLanes; CrossingsByLinkAndRouteIndex = crossings;
            CrossingInteractionsByLinkAndRouteIndex = crossingInteractions;
            SharedVerticalRunsByLinkId = sharedVerticalRuns;
        }

        public IReadOnlyDictionary<string, ArchitectureV7StraightRun> RunsById { get; }
        public IReadOnlyDictionary<string, ArchitectureV7RunLaneAssignment> AssignmentsByRunId { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<ArchitectureV7StraightRun>> RunsByPhysicalLinkId { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> IncomingRunsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7StraightRun> OutgoingRunsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, int RouteIndex), ArchitectureV7BendAllocation> BendsByLinkAndRouteIndex { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointHandoff> HandoffsByEndpoint { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointZBend> EndpointZBendsByEndpoint { get; }
        public IReadOnlyDictionary<(string PhysicalLinkId, ArchitectureV7EndpointKind EndpointKind), ArchitectureV7EndpointLaneCoordinate> EndpointLanesByLinkAndKind { get; }
        public IReadOnlyDictionary<string, ArchitectureV7SharedVerticalRunConstraint> SharedVerticalRunsByLinkId { get; }
        public ArchitectureV7CollectiveAllocationFreeze Allocation { get; }
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
            var endpointZBends = allocation.EndpointZBends
                .GroupBy(x => (x.PhysicalLinkId, x.EndpointKind)).ToDictionary(x => x.Key, x => x.First());
            var endpointLanes = allocation.EndpointLaneCoordinates
                .GroupBy(x => (x.PhysicalLinkId, x.EndpointKind)).ToDictionary(x => x.Key, x => x.First());
            var crossings = allocation.Crossings.OrderBy(x => x.CrossingId, StringComparer.Ordinal)
                .SelectMany(x => CrossingKeys(x).Select(key => (key, x))).GroupBy(x => x.key)
                .ToDictionary(x => x.Key, x => x.First().x);
            var interactions = allocation.CrossingInteractions.SelectMany(x => InteractionKeys(x).Select(key => (key, x)))
                .GroupBy(x => x.key).ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7CrossingInteraction>)Array.AsReadOnly(x.Select(item => item.x).OrderBy(item => item.InteractionId, StringComparer.Ordinal).ToArray()));
            return new(
                allocation,
                new ReadOnlyDictionary<string, ArchitectureV7StraightRun>(runsById),
                new ReadOnlyDictionary<string, ArchitectureV7RunLaneAssignment>(assignments),
                new ReadOnlyDictionary<string, IReadOnlyList<ArchitectureV7StraightRun>>(runsByLink),
                new ReadOnlyDictionary<(string, int), ArchitectureV7StraightRun>(incoming),
                new ReadOnlyDictionary<(string, int), ArchitectureV7StraightRun>(outgoing),
                new ReadOnlyDictionary<(string, int), ArchitectureV7BendAllocation>(bends),
                new ReadOnlyDictionary<(string, ArchitectureV7EndpointKind), ArchitectureV7EndpointHandoff>(handoffs),
                new ReadOnlyDictionary<(string, ArchitectureV7EndpointKind), ArchitectureV7EndpointZBend>(endpointZBends),
                new ReadOnlyDictionary<(string, ArchitectureV7EndpointKind), ArchitectureV7EndpointLaneCoordinate>(endpointLanes),
                new ReadOnlyDictionary<string, ArchitectureV7SharedVerticalRunConstraint>(allocation.SharedVerticalRunConstraints.ToDictionary(x => x.PhysicalLinkId, StringComparer.Ordinal)),
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
