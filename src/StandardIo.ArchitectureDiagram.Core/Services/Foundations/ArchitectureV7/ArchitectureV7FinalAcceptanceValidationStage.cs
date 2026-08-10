using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7FinalAcceptanceValidationStage
{
    public ArchitectureV7AcceptanceReport Validate(
        ArchitectureV7PhysicalProjectionResult projection,
        ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7NodeSpanSizingResult sizing,
        ArchitectureV7ReservationReconciliationResult reservation,
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7CollectiveAllocationFreeze allocation,
        ArchitectureV7PhysicalSceneFreeze scene,
        ArchitectureV7PhysicalSceneConfiguration configuration)
    {
        if (projection is null || ownership is null || sizing is null || reservation is null || placement is null || routes is null || allocation is null || scene is null || configuration is null)
            throw new ArgumentNullException("All immutable V7 pipeline products are required.");

        var findings = new List<ArchitectureV7AcceptanceFinding>();
        var indexes = new ValidationIndexes(routes, allocation, scene);
        ValidateFingerprints(projection, ownership, sizing, reservation, placement, routes, allocation, scene, findings);
        ValidateAccounting(projection, routes, scene, indexes, findings);
        ValidatePlacement(projection, ownership, reservation, placement, findings);
        ValidateLogicalRoutes(projection, placement, routes, findings);
        ValidatePhysicalGeometry(placement, routes, allocation, scene, configuration, indexes, findings);
        ValidateCrossings(allocation, scene, indexes, findings);
        ValidateTrackSizing(placement, allocation, scene, configuration, findings);
        ValidateRetainedDiagnostics(routes, allocation, scene, findings);
        var counts = findings.GroupBy(x => x.Code, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var normalEligible = !findings.Any(x => x.Code is "MISSING-RELATIONSHIP-ACCOUNTING" or "FINGERPRINT-MISMATCH" or "MISSING-SCENE-ROUTE");
        var metrics = indexes.Metrics;
        return new ArchitectureV7AcceptanceReport(findings, counts, projection.FreezeFingerprint, ownership.FreezeFingerprint, sizing.FreezeFingerprint,
            reservation.Table.Fingerprint, placement.PlacementFingerprint, routes.RouteFingerprint, allocation.AllocationFingerprint, scene.PhysicalSceneFingerprint, normalEligible, metrics);
    }

    private static void ValidateFingerprints(ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7NodeSpanSizingResult sizing, ArchitectureV7ReservationReconciliationResult reservation, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes, ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneFreeze scene,
        ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        Check(string.Equals(ownership.ProjectionFreezeFingerprint, projection.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "ownership", "Ownership does not carry the projection fingerprint.", projection.FreezeFingerprint, findings);
        Check(string.Equals(sizing.Ownership.FreezeFingerprint, ownership.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "sizing", "Sizing does not carry the ownership freeze.", ownership.FreezeFingerprint, findings);
        Check(string.Equals(reservation.Inspection.Ownership.FreezeFingerprint, ownership.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "reservation", "Reservation inspection does not carry the ownership freeze.", ownership.FreezeFingerprint, findings);
        Check(string.Equals(placement.ProjectionFingerprint, projection.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "placement", "Placement projection fingerprint differs.", placement.PlacementFingerprint, findings);
        Check(string.Equals(placement.OwnershipFingerprint, ownership.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "placement", "Placement ownership fingerprint differs.", placement.PlacementFingerprint, findings);
        Check(string.Equals(placement.SizingFingerprint, sizing.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "placement", "Placement sizing fingerprint differs.", placement.PlacementFingerprint, findings);
        Check(string.Equals(placement.ReservationFingerprint, reservation.Table.Fingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "placement", "Placement reservation fingerprint differs.", placement.PlacementFingerprint, findings);
        Check(string.Equals(routes.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "route", "Route placement fingerprint differs.", routes.RouteFingerprint, findings);
        Check(string.Equals(routes.ProjectionFingerprint, projection.FreezeFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "route", "Route projection fingerprint differs.", routes.RouteFingerprint, findings);
        Check(string.Equals(allocation.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "allocation", "Allocation placement fingerprint differs.", allocation.AllocationFingerprint, findings);
        Check(string.Equals(allocation.RouteFingerprint, routes.RouteFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "allocation", "Allocation route fingerprint differs.", allocation.AllocationFingerprint, findings);
        Check(string.Equals(scene.PlacementFingerprint, placement.PlacementFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "scene", "Scene placement fingerprint differs.", scene.PhysicalSceneFingerprint, findings);
        Check(string.Equals(scene.RouteFingerprint, routes.RouteFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "scene", "Scene route fingerprint differs.", scene.PhysicalSceneFingerprint, findings);
        Check(string.Equals(scene.AllocationFingerprint, allocation.AllocationFingerprint, StringComparison.Ordinal), "FINGERPRINT-MISMATCH", "scene", "Scene allocation fingerprint differs.", scene.PhysicalSceneFingerprint, findings);
    }

    private static void ValidateAccounting(ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7PhysicalSceneFreeze scene, ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var routeIds = new HashSet<string>(routes.Routes.Select(x => x.PhysicalLinkId), StringComparer.Ordinal);
        var sceneIds = new HashSet<string>(scene.Routes.Select(x => x.PhysicalLinkId), StringComparer.Ordinal);
        foreach (var unaccounted in projection.UnaccountedSemanticLinkIds)
            Add(findings, "MISSING-RELATIONSHIP-ACCOUNTING", "accounting", "Projection explicitly reports an unaccounted semantic relationship.", unaccounted, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "projection=" + projection.FreezeFingerprint });
        foreach (var link in projection.PhysicalLinks)
        {
            indexes.MetricsBuilder.AccountingLookups++;
            var route = indexes.LogicalRouteById.TryGetValue(link.PhysicalLinkId, out var indexedRoute) ? indexedRoute : null;
            if (route is null) Add(findings, "MISSING-RELATIONSHIP-ACCOUNTING", "accounting", "Projected relationship has no route state.", link.PhysicalLinkId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "projection=" + projection.FreezeFingerprint });
            else if (!route.IsComplete && route.Diagnostics.Count == 0) Add(findings, "MISSING-RELATIONSHIP-ACCOUNTING", "accounting", "Incomplete route has no retained diagnostic evidence.", link.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "route=" + routes.RouteFingerprint });
            else if (route.IsComplete && !sceneIds.Contains(link.PhysicalLinkId)) Add(findings, "MISSING-SCENE-ROUTE", "accounting", "Successful route has no physical scene route.", link.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "route=" + routes.RouteFingerprint });
        }
        foreach (var route in routes.Routes.Where(x => !projection.PhysicalLinks.Any(link => link.PhysicalLinkId == x.PhysicalLinkId)))
            Add(findings, "MISSING-RELATIONSHIP-ACCOUNTING", "accounting", "Route has no projected relationship identity.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "route=" + routes.RouteFingerprint });
        foreach (var sceneRoute in scene.Routes.Where(x => !routeIds.Contains(x.PhysicalLinkId)))
            Add(findings, "UNEXPECTED-SCENE-ROUTE", "accounting", "Scene contains a relationship absent from the route freeze.", sceneRoute.PhysicalLinkId, Array.Empty<ArchitectureV7RouteCell>(), sceneRoute.Points, new[] { sceneRoute.Provenance });
    }

    private static void ValidatePlacement(ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7ReservationReconciliationResult reservation, ArchitectureV7PlacementFreeze placement, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var nodeById = placement.Nodes.ToDictionary(x => x.PhysicalNodeId, StringComparer.Ordinal);
        var occupied = new Dictionary<(int Row, int Column), string>();
        foreach (var node in placement.Nodes)
            foreach (var cell in node.LogicalFootprint)
                if (occupied.TryGetValue(cell, out var other)) Add(findings, "LOGICAL-FOOTPRINT-OVERLAP", "placement", "Frozen node footprints overlap.", node.PhysicalNodeId, new[] { new ArchitectureV7RouteCell(cell.Row, cell.Column) }, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "other=" + other });
                else occupied[cell] = node.PhysicalNodeId;
        var externalRow = placement.External.NodeRow;
        foreach (var node in placement.Nodes)
        {
            if (node.IsExternal != (node.DiagramRow == externalRow)) Add(findings, node.IsExternal ? "EXTERNAL-ROW-MISMATCH" : "NON-EXTERNAL-ON-EXTERNAL-ROW", "placement", "External row occupancy is inconsistent with frozen roles.", node.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "external-row=" + externalRow });
            if (node.IsStandalone && !placement.Standalone.PhysicalNodeIds.Contains(node.PhysicalNodeId, StringComparer.Ordinal)) Add(findings, "STANDALONE-REGION-MISMATCH", "placement", "Standalone node is outside the standalone region.", node.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
            if (!node.IsStandalone && placement.Standalone.PhysicalNodeIds.Contains(node.PhysicalNodeId, StringComparer.Ordinal)) Add(findings, "NON-STANDALONE-IN-STANDALONE-REGION", "placement", "Non-standalone node occupies the standalone region.", node.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
        }
        foreach (var left in placement.Nodes)
            foreach (var right in placement.Nodes.Where(x => string.CompareOrdinal(x.PhysicalNodeId, left.PhysicalNodeId) > 0))
                if (left.LogicalFootprint.Any(a => right.LogicalFootprint.Any(b => Math.Abs(a.Row - b.Row) + Math.Abs(a.Column - b.Column) == 1)))
                    Add(findings, "LOGICAL-SEPARATION-MISSING", "placement", "Atomic placement units have no required logical separation column/row.", left.PhysicalNodeId + "/" + right.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "other=" + right.PhysicalNodeId });
        foreach (var decision in ownership.Decisions.Where(x => x.PositionalParentPhysicalNodeId is not null))
            if (nodeById.TryGetValue(decision.PhysicalNodeId, out var child) && nodeById.TryGetValue(decision.PositionalParentPhysicalNodeId!, out var parent) && !child.IsDetached && child.DiagramRow <= parent.DiagramRow)
                Add(findings, "POSITIONAL-DEPTH-INVALID", "placement", "Positional child is not below its positional parent.", child.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "parent=" + parent.PhysicalNodeId });
        foreach (var project in placement.Projects)
        {
            var transform = placement.Transforms.FirstOrDefault(x => x.ProjectId == project.ProjectId);
            if (transform is null || !Equals(transform, project.Transform)) Add(findings, "PROJECT-TRANSFORM-MISMATCH", "placement", "Project transform is not frozen consistently across products.", project.ProjectId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
        }
        foreach (var physicalNode in projection.PhysicalNodes.Where(x => !nodeById.ContainsKey(x.PhysicalNodeId)))
            Add(findings, "UNPLACED-PROJECTED-NODE", "placement", "Projected node has no final placement.", physicalNode.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "projection=" + projection.FreezeFingerprint });
        _ = reservation;
    }

    private static void ValidateLogicalRoutes(ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var grid = placement.DiagramGrid.Cells.ToDictionary(x => (x.Row, x.Column), x => x);
        var nodes = placement.Nodes.ToDictionary(x => x.PhysicalNodeId, StringComparer.Ordinal);
        foreach (var route in routes.Routes.Where(x => x.IsComplete))
        {
            if (!nodes.TryGetValue(route.SourcePhysicalNodeId, out var source) || !nodes.TryGetValue(route.DestinationPhysicalNodeId, out var destination)) continue;
            if (route.Cells.Count < 2 || route.Cells[0].Row != source.DiagramRow || route.Cells[0].Column != source.CentreCell) Add(findings, "ROUTE-SOURCE-MISMATCH", "logical-route", "Route does not begin at the frozen source node centre.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "source=" + source.PhysicalNodeId });
            if (route.Cells.Count < 2 || route.Cells[route.Cells.Count - 1].Row != destination.DiagramRow || route.Cells[route.Cells.Count - 1].Column != destination.CentreCell) Add(findings, "ROUTE-TARGET-MISMATCH", "logical-route", "Route does not end at the frozen destination node centre.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "destination=" + destination.PhysicalNodeId });
            if (route.Cells.Count > 1 && route.Cells[1].Row <= route.Cells[0].Row) Add(findings, "SOURCE-NOT-BOTTOM-DEPARTURE", "logical-route", "Source departure is not downward.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
            if (route.Cells.Count > 1 && route.Cells[route.Cells.Count - 2].Row >= route.Cells[route.Cells.Count - 1].Row) Add(findings, "DESTINATION-NOT-TOP-ENTRY", "logical-route", "Destination is not entered downward from the routing row above.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
            for (var index = 1; index < route.Cells.Count; index++)
            {
                var previous = route.Cells[index - 1]; var current = route.Cells[index];
                if (Math.Abs(previous.Row - current.Row) + Math.Abs(previous.Column - current.Column) != 1) Add(findings, "LOGICAL-CELL-JUMP", "logical-route", "Route contains a non-adjacent logical transition.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "index=" + index });
            }
            for (var index = 2; index < route.Cells.Count; index++)
                if (route.Cells[index - 2] == route.Cells[index]) Add(findings, "LOGICAL-A-B-A-RETRACE", "logical-route", "Route immediately retraces a logical cell.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "index=" + index });
            for (var index = 2; index < route.Cells.Count; index++)
                if (route.Cells[index - 2].Row == route.Cells[index - 1].Row && route.Cells[index - 1].Row == route.Cells[index].Row && Math.Sign(route.Cells[index - 1].Column - route.Cells[index - 2].Column) != Math.Sign(route.Cells[index].Column - route.Cells[index - 1].Column))
                    Add(findings, "LOGICAL-HORIZONTAL-REVERSAL", "logical-route", "Route reverses horizontally without an accepted topology explanation.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
            for (var index = 1; index < route.Cells.Count - 1; index++)
            {
                var cell = route.Cells[index];
                var entry = DirectionOf(route.Cells[index - 1], cell);
                var exit = DirectionOf(cell, route.Cells[index + 1]);
                if (!grid.TryGetValue((cell.Row, cell.Column), out var logicalCell) || !ArchitectureV7CellTraversalPolicy.Allows(logicalCell.Capabilities, entry, exit))
                    Add(findings, "ROUTE-CAPABILITY-VIOLATION", "logical-route", "Route uses a cell without capability for its traversed entry/exit directions.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "cell=" + cell.Row + "," + cell.Column, "entry=" + entry, "exit=" + exit });
                if (logicalCell?.OccupantId is not null && logicalCell.OccupantId != route.SourcePhysicalNodeId && logicalCell.OccupantId != route.DestinationPhysicalNodeId)
                    Add(findings, "ROUTE-CROSSES-NODE-FOOTPRINT", "logical-route", "Route crosses an unrelated frozen node footprint.", route.PhysicalLinkId, route.Cells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "occupant=" + logicalCell.OccupantId });
            }
        }
        _ = projection;
    }

    private static ArchitectureV7TraversalDirection DirectionOf(ArchitectureV7RouteCell from, ArchitectureV7RouteCell to) =>
        to.Row == from.Row ? (to.Column > from.Column ? ArchitectureV7TraversalDirection.Right : ArchitectureV7TraversalDirection.Left) :
        (to.Row > from.Row ? ArchitectureV7TraversalDirection.Down : ArchitectureV7TraversalDirection.Up);

    private static void ValidatePhysicalGeometry(ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneFreeze scene, ArchitectureV7PhysicalSceneConfiguration configuration,
        ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var route in scene.Routes)
        {
            var frozen = indexes.LogicalRouteById.TryGetValue(route.PhysicalLinkId, out var frozenRoute) ? frozenRoute : null;
            var terminals = indexes.TerminalsByRoute.TryGetValue(route.PhysicalLinkId, out var routeTerminals) ? routeTerminals : Array.Empty<ArchitectureV7PhysicalTerminal>();
            var source = terminals.FirstOrDefault(x => x.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var destination = terminals.FirstOrDefault(x => x.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
            if (frozen is null || source is null || destination is null) continue;
            if (route.Points.Count == 0 || route.Points[0] != source.Position) Add(findings, "PHYSICAL-SOURCE-TERMINAL-MISMATCH", "physical-geometry", "Physical route does not begin at its allocated source terminal.", route.PhysicalLinkId, frozen.Cells, route.Points, new[] { "terminal=" + source.SlotOrdinal });
            if (route.Points.Count == 0 || route.Points[route.Points.Count - 1] != destination.Position) Add(findings, "PHYSICAL-DESTINATION-TERMINAL-MISMATCH", "physical-geometry", "Physical route does not end at its allocated destination terminal.", route.PhysicalLinkId, frozen.Cells, route.Points, new[] { "terminal=" + destination.SlotOrdinal });
            foreach (var segment in route.Segments)
            {
                if (segment.Start.X != segment.End.X && segment.Start.Y != segment.End.Y) Add(findings, "PHYSICAL-DIAGONAL", "physical-geometry", "Physical scene contains a diagonal segment.", route.PhysicalLinkId, segment.LogicalCells, new[] { segment.Start, segment.End }, new[] { segment.RunId, segment.LaneId });
                foreach (var node in indexes.NodesForSegment(segment))
                {
                    indexes.MetricsBuilder.SegmentNodePredicateEvaluations++;
                    if (node.Key != route.PhysicalLinkId && StrictlyCrossesNode(segment, node.Value.Bounds)) Add(findings, "PHYSICAL-NODE-BODY-CROSSING", "physical-geometry", "Physical route crosses an unrelated node body.", route.PhysicalLinkId, segment.LogicalCells, new[] { segment.Start, segment.End }, new[] { node.Key });
                }
                if (segment.AllocationProvenance is null || segment.RunId.Length == 0 || segment.LaneId.Length == 0) Add(findings, "MISSING-PHYSICAL-PROVENANCE", "physical-geometry", "Physical segment lacks run/lane provenance.", route.PhysicalLinkId, segment.LogicalCells, new[] { segment.Start, segment.End }, Array.Empty<string>());
            }
            var bends = route.Points.Zip(route.Points.Skip(1), (a, b) => (a, b)).Zip(route.Points.Skip(2), (pair, c) => (pair.a, pair.b, c)).Count(x => (x.a.X == x.b.X) != (x.b.X == x.c.X));
            var allocatedBends = indexes.BendsByRoute.TryGetValue(route.PhysicalLinkId, out var routeBends) ? routeBends.Count : 0;
            var handoffs = indexes.HandoffsByRoute.TryGetValue(route.PhysicalLinkId, out var routeHandoffs) ? routeHandoffs.Count : 0;
            if (bends > allocatedBends + handoffs) Add(findings, "UNALLOCATED-Z-GEOMETRY", "physical-geometry", "Physical polyline contains more bends than frozen bend/handoff allocation.", route.PhysicalLinkId, frozen.Cells, route.Points, new[] { "allocated-bends=" + allocatedBends });
            var nodes = indexes.NodeById;
            if (source.Position.Y != nodes[source.PhysicalNodeId].Bounds.Bottom) Add(findings, "SOURCE-NOT-BOTTOM-EDGE", "physical-geometry", "Source terminal is not on the node bottom edge.", route.PhysicalLinkId, frozen.Cells, new[] { source.Position }, Array.Empty<string>());
            if (destination.Position.Y != nodes[destination.PhysicalNodeId].Bounds.Top) Add(findings, "DESTINATION-NOT-TOP-EDGE", "physical-geometry", "Destination terminal is not on the node top edge.", route.PhysicalLinkId, frozen.Cells, new[] { destination.Position }, Array.Empty<string>());
        }
        foreach (var left in indexes.PhysicalGeometryPairs())
        {
            indexes.MetricsBuilder.PhysicalGeometryCandidateSegmentPairs++;
            indexes.MetricsBuilder.SegmentPairPredicateEvaluations++;
            var a = left.Left.Segment; var b = left.Right.Segment;
            if (CollinearOverlap(a, b, out var distance) && distance > 0)
            {
                Add(findings, "SHARED-PHYSICAL-INTERVAL", "physical-geometry", "Unrelated routes share a non-zero physical interval.", left.Left.RouteId + "/" + left.Right.RouteId, a.LogicalCells, new[] { a.Start, a.End, b.Start, b.End }, new[] { a.LaneId, b.LaneId });
                if (distance < configuration.ParallelLaneSpacing) Add(findings, "INSUFFICIENT-PARALLEL-SPACING", "physical-geometry", "Parallel routes are closer than configured spacing.", left.Left.RouteId + "/" + left.Right.RouteId, a.LogicalCells, new[] { a.Start, b.Start }, new[] { a.LaneId, b.LaneId });
            }
            else if (ParallelOverlap(a, b, out var parallelDistance) && parallelDistance < configuration.ParallelLaneSpacing)
                Add(findings, "INSUFFICIENT-PARALLEL-SPACING", "physical-geometry", "Parallel routes are closer than configured spacing.", left.Left.RouteId + "/" + left.Right.RouteId, a.LogicalCells, new[] { a.Start, b.Start }, new[] { a.LaneId, b.LaneId });
        }
        _ = placement; _ = routes; _ = allocation;
    }

    private static void ValidateCrossings(ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneFreeze scene,
        ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var pair in indexes.CrossingPairs())
        {
            indexes.MetricsBuilder.CrossingCandidates++;
            indexes.MetricsBuilder.CrossingPredicateEvaluations++;
            var horizontal = pair.Horizontal; var vertical = pair.Vertical;
            var x = vertical.Segment.Start.X; var y = horizontal.Segment.Start.Y;
            if (x < Math.Min(horizontal.Segment.Start.X, horizontal.Segment.End.X) || x > Math.Max(horizontal.Segment.Start.X, horizontal.Segment.End.X) || y < Math.Min(vertical.Segment.Start.Y, vertical.Segment.End.Y) || y > Math.Max(vertical.Segment.Start.Y, vertical.Segment.End.Y)) continue;
            var hRoute = indexes.RouteById[horizontal.RouteId];
            var vRoute = indexes.RouteById[vertical.RouteId];
            if (TurnsAt(hRoute, x, y) || TurnsAt(vRoute, x, y)) Add(findings, "CROSSING-TURN-CONFLICT", "physical-geometry", "A route turns at a perpendicular physical crossing.", horizontal.RouteId + "/" + vertical.RouteId, Array.Empty<ArchitectureV7RouteCell>(), new[] { new ArchitectureV7PhysicalPoint(x, y, "crossing") }, new[] { horizontal.Segment.LaneId, vertical.Segment.LaneId });
            else if (!indexes.CrossingResourcePairs.Contains(horizontal.RouteId + "\u001f" + vertical.RouteId)) Add(findings, "UNALLOCATED-CROSSING", "physical-geometry", "Perpendicular crossing has no frozen crossing allocation.", horizontal.RouteId + "/" + vertical.RouteId, Array.Empty<ArchitectureV7RouteCell>(), new[] { new ArchitectureV7PhysicalPoint(x, y, "crossing") }, Array.Empty<string>());
        }
        _ = allocation; _ = scene;
    }

    private static bool TurnsAt(ArchitectureV7PhysicalRoute route, double x, double y) => route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).Any(point => point.X == x && point.Y == y);

    private static void ValidateTrackSizing(ArchitectureV7PlacementFreeze placement, ArchitectureV7CollectiveAllocationFreeze allocation,
        ArchitectureV7PhysicalSceneFreeze scene, ArchitectureV7PhysicalSceneConfiguration configuration, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var row in scene.Rows)
            if (row.RequiredExtent < ArchitectureV7PhysicalSceneSizing.RowMinimum(row.LogicalIndex, placement, configuration)) Add(findings, "ROW-EXTENT-UNDERFLOW", "track-sizing", "Physical row is smaller than its role-specific configured minimum.", row.LogicalIndex.ToString(), Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
        foreach (var column in scene.Columns)
            if (column.RequiredExtent < configuration.BaseCellWidth) Add(findings, "COLUMN-EXTENT-UNDERFLOW", "track-sizing", "Physical column is smaller than its configured base extent.", column.LogicalIndex.ToString(), Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), Array.Empty<string>());
        foreach (var node in scene.Nodes)
            if (node.Bounds.Right <= node.Bounds.Left || node.Bounds.Bottom <= node.Bounds.Top) Add(findings, "NODE-PHYSICAL-BOUNDS-INVALID", "track-sizing", "Node physical bounds are not positive.", node.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { node.Provenance });
        _ = allocation;
    }

    private static void ValidateRetainedDiagnostics(ArchitectureV7LogicalRouteFreeze routes, ArchitectureV7CollectiveAllocationFreeze allocation,
        ArchitectureV7PhysicalSceneFreeze scene, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var diagnostic in routes.Diagnostics) Add(findings, diagnostic.Code, "logical-route", diagnostic.Message, null, diagnostic.AttemptedCells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "route-freeze=" + routes.RouteFingerprint });
        foreach (var route in routes.Routes) foreach (var diagnostic in route.Diagnostics) if (diagnostic.IsHardFailure) Add(findings, diagnostic.Code, "logical-route", diagnostic.Message, route.PhysicalLinkId, diagnostic.AttemptedCells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { route.Provenance });
        foreach (var diagnostic in allocation.Diagnostics) if (diagnostic.IsHardFailure) Add(findings, diagnostic.Code, "allocation", diagnostic.Message, diagnostic.PhysicalLinkId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { diagnostic.RunId ?? "allocation" });
        foreach (var diagnostic in scene.Diagnostics) if (diagnostic.IsHardFailure) Add(findings, diagnostic.Code, "scene", diagnostic.Message, diagnostic.PhysicalLinkId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "scene=" + scene.PhysicalSceneFingerprint });
    }

    private static bool StrictlyCrossesNode(ArchitectureV7PhysicalSegment segment, ArchitectureV7PhysicalBounds bounds)
    {
        if (segment.Start.Y == segment.End.Y) return segment.Start.Y > bounds.Top && segment.Start.Y < bounds.Bottom && Math.Max(segment.Start.X, segment.End.X) > bounds.Left && Math.Min(segment.Start.X, segment.End.X) < bounds.Right;
        if (segment.Start.X == segment.End.X) return segment.Start.X > bounds.Left && segment.Start.X < bounds.Right && Math.Max(segment.Start.Y, segment.End.Y) > bounds.Top && Math.Min(segment.Start.Y, segment.End.Y) < bounds.Bottom;
        return true;
    }
    private static bool CollinearOverlap(ArchitectureV7PhysicalSegment a, ArchitectureV7PhysicalSegment b, out double distance)
    {
        distance = 0;
        if (a.Start.Y == a.End.Y && b.Start.Y == b.End.Y && a.Start.Y == b.Start.Y) { var overlap = Math.Min(Math.Max(a.Start.X, a.End.X), Math.Max(b.Start.X, b.End.X)) - Math.Max(Math.Min(a.Start.X, a.End.X), Math.Min(b.Start.X, b.End.X)); distance = overlap; return overlap > 0; }
        if (a.Start.X == a.End.X && b.Start.X == b.End.X && a.Start.X == b.Start.X) { var overlap = Math.Min(Math.Max(a.Start.Y, a.End.Y), Math.Max(b.Start.Y, b.End.Y)) - Math.Max(Math.Min(a.Start.Y, a.End.Y), Math.Min(b.Start.Y, b.End.Y)); distance = overlap; return overlap > 0; }
        return false;
    }
    private static bool ParallelOverlap(ArchitectureV7PhysicalSegment a, ArchitectureV7PhysicalSegment b, out double distance)
    {
        distance = 0;
        if (a.Start.Y == a.End.Y && b.Start.Y == b.End.Y) { var overlap = Math.Min(Math.Max(a.Start.X, a.End.X), Math.Max(b.Start.X, b.End.X)) - Math.Max(Math.Min(a.Start.X, a.End.X), Math.Min(b.Start.X, b.End.X)); if (overlap > 0 && a.Start.Y != b.Start.Y) { distance = Math.Abs(a.Start.Y - b.Start.Y); return true; } }
        if (a.Start.X == a.End.X && b.Start.X == b.End.X) { var overlap = Math.Min(Math.Max(a.Start.Y, a.End.Y), Math.Max(b.Start.Y, b.End.Y)) - Math.Max(Math.Min(a.Start.Y, a.End.Y), Math.Min(b.Start.Y, b.End.Y)); if (overlap > 0 && a.Start.X != b.Start.X) { distance = Math.Abs(a.Start.X - b.Start.X); return true; } }
        return false;
    }
    private static void Check(bool condition, string code, string subject, string message, string provenance, ICollection<ArchitectureV7AcceptanceFinding> findings)
    { if (!condition) Add(findings, code, subject, message, subject, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { provenance }); }
    private static void Add(ICollection<ArchitectureV7AcceptanceFinding> findings, string code, string stage, string message, string? subject,
        IReadOnlyList<ArchitectureV7RouteCell> cells, IReadOnlyList<ArchitectureV7PhysicalPoint> points, IReadOnlyList<string> provenance) => findings.Add(new(code, stage, message, subject, cells, points, provenance));
    private static string PairKey(string left, string right) => string.CompareOrdinal(left, right) < 0 ? left + "\u001f" + right : right + "\u001f" + left;

    private sealed class ValidationIndexes
    {
        private const double BucketSize = 256d;
        private readonly Dictionary<long, List<SegmentRef>> horizontalBuckets = new();
        private readonly Dictionary<long, List<SegmentRef>> verticalBuckets = new();
        private readonly Dictionary<long, List<SegmentRef>> verticalCoordinateBuckets = new();
        private readonly Dictionary<(long X, long Y), List<string>> nodeBuckets = new();
        private readonly List<SegmentRef> horizontal = new();
        private readonly List<SegmentRef> vertical = new();
        public readonly Dictionary<string, ArchitectureV7LogicalRoute> LogicalRouteById;
        public readonly Dictionary<string, ArchitectureV7PhysicalRoute> RouteById;
        public readonly Dictionary<string, ArchitectureV7PhysicalSceneNode> NodeById;
        public readonly Dictionary<string, IReadOnlyList<ArchitectureV7PhysicalTerminal>> TerminalsByRoute;
        public readonly Dictionary<string, IReadOnlyList<ArchitectureV7BendAllocation>> BendsByRoute;
        public readonly Dictionary<string, IReadOnlyList<ArchitectureV7EndpointHandoff>> HandoffsByRoute;
        public readonly HashSet<string> CrossingResourcePairs;
        public readonly MetricsBuilder MetricsBuilder;
        public ArchitectureV7AcceptanceValidationMetrics Metrics => MetricsBuilder.Freeze();

        public ValidationIndexes(ArchitectureV7LogicalRouteFreeze routes, ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneFreeze scene)
        {
            LogicalRouteById = routes.Routes.ToDictionary(x => x.PhysicalLinkId, StringComparer.Ordinal);
            RouteById = scene.Routes.ToDictionary(x => x.PhysicalLinkId, StringComparer.Ordinal);
            NodeById = scene.Nodes.ToDictionary(x => x.PhysicalNodeId, StringComparer.Ordinal);
            TerminalsByRoute = scene.Terminals.GroupBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7PhysicalTerminal>)x.ToArray(), StringComparer.Ordinal);
            BendsByRoute = allocation.Bends.GroupBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7BendAllocation>)x.ToArray(), StringComparer.Ordinal);
            HandoffsByRoute = allocation.Handoffs.GroupBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7EndpointHandoff>)x.ToArray(), StringComparer.Ordinal);
            CrossingResourcePairs = new HashSet<string>(allocation.Crossings.Select(x => x.HorizontalPhysicalLinkId + "\u001f" + x.VerticalPhysicalLinkId), StringComparer.Ordinal);
            MetricsBuilder = new MetricsBuilder(scene.Routes.Sum(x => x.Segments.Count));
            foreach (var route in scene.Routes)
                for (var index = 0; index < route.Segments.Count; index++) AddSegment(new SegmentRef(route.PhysicalLinkId, index, route.Segments[index]));
            foreach (var node in scene.Nodes) AddNode(node);
        }

        public IEnumerable<(SegmentRef Left, SegmentRef Right)> PhysicalGeometryPairs()
        {
            foreach (var current in horizontal.Concat(vertical).OrderBy(x => x.RouteId, StringComparer.Ordinal).ThenBy(x => x.Index))
                foreach (var candidate in Candidates(current))
                    if (string.CompareOrdinal(candidate.RouteId, current.RouteId) > 0)
                        yield return (current, candidate);
        }

        public IEnumerable<(SegmentRef Horizontal, SegmentRef Vertical)> CrossingPairs()
        {
            foreach (var h in horizontal.OrderBy(x => x.RouteId, StringComparer.Ordinal).ThenBy(x => x.Index))
                foreach (var v in CrossingCandidates(h).Where(x => x.RouteId != h.RouteId).OrderBy(x => x.RouteId, StringComparer.Ordinal).ThenBy(x => x.Index))
                    yield return (h, v);
        }

        public IEnumerable<KeyValuePair<string, ArchitectureV7PhysicalSceneNode>> NodesForSegment(ArchitectureV7PhysicalSegment segment)
        {
            MetricsBuilder.NodeBoundCandidates += 0;
            var minX = Math.Min(segment.Start.X, segment.End.X); var maxX = Math.Max(segment.Start.X, segment.End.X);
            var minY = Math.Min(segment.Start.Y, segment.End.Y); var maxY = Math.Max(segment.Start.Y, segment.End.Y);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var x in BucketRange(minX, maxX)) foreach (var y in BucketRange(minY, maxY)) if (nodeBuckets.TryGetValue((x, y), out var bucket)) foreach (var id in bucket) ids.Add(id);
            foreach (var id in ids.OrderBy(x => x, StringComparer.Ordinal))
                if (NodeById.TryGetValue(id, out var node) && node.Bounds.Right >= minX && node.Bounds.Left <= maxX && node.Bounds.Bottom >= minY && node.Bounds.Top <= maxY)
                { MetricsBuilder.NodeBoundCandidates++; yield return new KeyValuePair<string, ArchitectureV7PhysicalSceneNode>(id, node); }
        }

        private IEnumerable<SegmentRef> Candidates(SegmentRef current)
        {
            var set = new HashSet<(string, int)>();
            var min = current.MinAxis; var max = current.MaxAxis;
            var buckets = current.IsHorizontal ? horizontalBuckets : verticalBuckets;
            foreach (var key in BucketRange(min, max)) if (buckets.TryGetValue(key, out var bucket)) foreach (var candidate in bucket)
                if (candidate.RouteId != current.RouteId && candidate.MaxAxis >= min && candidate.MinAxis <= max && set.Add((candidate.RouteId, candidate.Index))) yield return candidate;
        }

        private IEnumerable<SegmentRef> CrossingCandidates(SegmentRef current)
        {
            var buckets = verticalCoordinateBuckets;
            var set = new HashSet<(string, int)>();
            foreach (var key in BucketRange(current.MinAxis, current.MaxAxis)) if (buckets.TryGetValue(key, out var bucket)) foreach (var candidate in bucket)
                if (candidate.RouteId != current.RouteId && candidate.Coordinate >= Math.Min(current.Segment.Start.X, current.Segment.End.X) && candidate.Coordinate <= Math.Max(current.Segment.Start.X, current.Segment.End.X) && current.Coordinate >= candidate.MinAxis && current.Coordinate <= candidate.MaxAxis && set.Add((candidate.RouteId, candidate.Index)))
                    yield return candidate;
        }

        private void AddSegment(SegmentRef item)
        {
            var target = item.IsHorizontal ? horizontal : vertical; target.Add(item);
            var buckets = item.IsHorizontal ? horizontalBuckets : verticalBuckets;
            foreach (var key in BucketRange(item.MinAxis, item.MaxAxis)) { if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new List<SegmentRef>(); bucket.Add(item); }
            if (!item.IsHorizontal)
                foreach (var key in BucketRange(item.Segment.Start.X, item.Segment.Start.X)) { if (!verticalCoordinateBuckets.TryGetValue(key, out var bucket)) verticalCoordinateBuckets[key] = bucket = new List<SegmentRef>(); bucket.Add(item); }
        }
        private void AddNode(ArchitectureV7PhysicalSceneNode node)
        {
            foreach (var x in BucketRange(node.Bounds.Left, node.Bounds.Right)) foreach (var y in BucketRange(node.Bounds.Top, node.Bounds.Bottom)) { if (!nodeBuckets.TryGetValue((x, y), out var bucket)) nodeBuckets[(x, y)] = bucket = new List<string>(); bucket.Add(node.PhysicalNodeId); }
        }
        private static IEnumerable<long> BucketRange(double min, double max) { var first = (long)Math.Floor(min / BucketSize); var last = (long)Math.Floor(max / BucketSize); for (var value = first; value <= last; value++) yield return value; }
    }

    private sealed class MetricsBuilder
    {
        public MetricsBuilder(int total) { TotalPhysicalSegments = total; }
        public int TotalPhysicalSegments { get; }
        public long PhysicalGeometryCandidateSegmentPairs;
        public long SegmentPairPredicateEvaluations;
        public long NodeBoundCandidates;
        public long SegmentNodePredicateEvaluations;
        public long CrossingCandidates;
        public long CrossingPredicateEvaluations;
        public long AccountingLookups;
        public ArchitectureV7AcceptanceValidationMetrics Freeze() => new(TotalPhysicalSegments, PhysicalGeometryCandidateSegmentPairs, SegmentPairPredicateEvaluations, NodeBoundCandidates, SegmentNodePredicateEvaluations, CrossingCandidates, CrossingPredicateEvaluations, AccountingLookups, 0);
    }

    private sealed record SegmentRef(string RouteId, int Index, ArchitectureV7PhysicalSegment Segment)
    {
        public bool IsHorizontal => Segment.Start.Y == Segment.End.Y;
        public double Coordinate => IsHorizontal ? Segment.Start.Y : Segment.Start.X;
        public double MinAxis => IsHorizontal ? Math.Min(Segment.Start.X, Segment.End.X) : Math.Min(Segment.Start.Y, Segment.End.Y);
        public double MaxAxis => IsHorizontal ? Math.Max(Segment.Start.X, Segment.End.X) : Math.Max(Segment.Start.Y, Segment.End.Y);
    }
}
