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
        ValidateNodeSpanJustification(sizing, placement, findings);
        ValidateLogicalRoutes(projection, placement, routes, findings);
        ValidatePhysicalGeometry(placement, routes, allocation, scene, configuration, indexes, findings);
        ValidateEndpointLaneOrdering(allocation, scene, indexes, findings);
        ValidateDirectCentreAuthority(allocation, scene, placement, indexes, findings);
        ValidateTerminalFinalLaneAuthority(allocation, scene, indexes, findings);
        ValidateEndpointRegion(allocation, scene, configuration, indexes, findings);
        ValidateCrossings(allocation, scene, indexes, findings);
        ValidateTrackSizing(placement, allocation, scene, configuration, findings);
        ValidateRetainedDiagnostics(routes, allocation, scene, findings);
        var counts = findings.GroupBy(x => x.Code, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var normalEligible = !findings.Any(x => x.Code is "MISSING-RELATIONSHIP-ACCOUNTING" or "FINGERPRINT-MISMATCH" or "MISSING-SCENE-ROUTE");
        var metrics = indexes.Metrics;
        return new ArchitectureV7AcceptanceReport(findings, counts, projection.FreezeFingerprint, ownership.FreezeFingerprint, sizing.FreezeFingerprint,
            reservation.Table.Fingerprint, placement.PlacementFingerprint, routes.RouteFingerprint, allocation.AllocationFingerprint, scene.PhysicalSceneFingerprint, normalEligible, metrics);
    }

    private static void ValidateNodeSpanJustification(ArchitectureV7NodeSpanSizingResult sizing,
        ArchitectureV7PlacementFreeze placement, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var requirement in sizing.Requirements)
        {
            var chosen = placement.Nodes.FirstOrDefault(node => node.PhysicalNodeId == requirement.PhysicalNodeId)?.LogicalSpan ?? requirement.LogicalSpan;
            if (chosen <= requirement.MinimumLegalSpan) continue;
            Add(findings, "UNJUSTIFIED-NODE-SPAN", "pre-routing-sizing", "The frozen node span exceeds the minimum legal capacity-driven odd span.",
                requirement.PhysicalNodeId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[]
                {
                    "chosen-span=" + chosen,
                    "minimum-required-span=" + requirement.MinimumLegalSpan,
                    "incoming-terminal-count=" + requirement.IncomingTerminalCount,
                    "outgoing-terminal-count=" + requirement.OutgoingTerminalCount,
                    "required-edge-extent=" + requirement.RequiredPhysicalEdgeExtent,
                    "available-edge-extent=" + requirement.AvailablePhysicalEdgeExtent
                });
        }
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
        var endpointDerivedScene = scene.AllocationFingerprint.StartsWith(allocation.AllocationFingerprint + "|endpoint-geometry:", StringComparison.Ordinal);
        Check(string.Equals(scene.AllocationFingerprint, allocation.AllocationFingerprint, StringComparison.Ordinal) || endpointDerivedScene,
            "FINGERPRINT-MISMATCH", "scene", "Scene allocation fingerprint differs.", scene.PhysicalSceneFingerprint, findings);
    }

    private static void ValidateAccounting(ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7PhysicalSceneFreeze scene, ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var routeIds = new HashSet<string>(routes.Routes.Select(x => x.PhysicalLinkId), StringComparer.Ordinal);
        var sceneIds = new HashSet<string>(scene.Routes.Select(x => x.PhysicalLinkId), StringComparer.Ordinal);
        var accountedIds = new HashSet<string>(scene.AccountedPhysicalLinkIds, StringComparer.Ordinal);
        foreach (var linkId in routeIds.Except(accountedIds, StringComparer.Ordinal))
            Add(findings, "MISSING-RELATIONSHIP-ACCOUNTING", "accounting", "Compiled scene retained no accounting identity for a route.", linkId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "scene-accounted=" + scene.AccountedPhysicalLinkIds.Count });
        foreach (var linkId in accountedIds.Except(routeIds, StringComparer.Ordinal))
            Add(findings, "UNEXPECTED-SCENE-ROUTE", "accounting", "Scene accounting contains a relationship absent from the route freeze.", linkId, Array.Empty<ArchitectureV7RouteCell>(), Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { "scene-accounted=" + scene.AccountedPhysicalLinkIds.Count });
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
            if (HasCollinearReversal(route.Points)) Add(findings, "PHYSICAL-COLLINEAR-REVERSAL", "physical-geometry", "Endpoint-inclusive physical geometry reverses direction along one straight axis.", route.PhysicalLinkId, frozen.Cells, route.Points, route.Points.Select(point => point.Provenance).ToArray());
            if (HasOvershootReturn(route.Points)) Add(findings, "PHYSICAL-OVERSHOOT-RETURN", "physical-geometry", "Endpoint-inclusive physical geometry overshoots a straight-run boundary and returns across it.", route.PhysicalLinkId, frozen.Cells, route.Points, route.Points.Select(point => point.Provenance).ToArray());
            foreach (var segment in route.Segments)
            {
                if (segment.Start.X != segment.End.X && segment.Start.Y != segment.End.Y) Add(findings, "PHYSICAL-DIAGONAL", "physical-geometry", "Physical scene contains a diagonal segment.", route.PhysicalLinkId, segment.LogicalCells, new[] { segment.Start, segment.End }, new[] { segment.RunId, segment.LaneId });
                ValidatePhysicalResourceProvenance(route, frozen, allocation, segment, findings);
                foreach (var node in indexes.NodesForSegment(segment))
                {
                    indexes.MetricsBuilder.SegmentNodePredicateEvaluations++;
                    var sourceNodeId = source.PhysicalNodeId;
                    var destinationNodeId = destination.PhysicalNodeId;
                    var isEndpointNode = node.Key == sourceNodeId || node.Key == destinationNodeId;
                    var crosses = isEndpointNode
                        ? StrictlyCrossesNode(segment, node.Value.Bounds)
                        : IntersectsNodeBoundaryOrInterior(segment, node.Value.Bounds);
                    if (crosses)
                    {
                        var message = isEndpointNode
                            ? "Physical route traverses the interior of its source or target node beyond the allocated terminal."
                            : "Physical route intersects the boundary or interior of an unrelated node.";
                        Add(findings, "ROUTE-THROUGH-NODE", "physical-geometry", message, route.PhysicalLinkId,
                            segment.LogicalCells, new[] { segment.Start, segment.End }, new[]
                            {
                                "source=" + sourceNodeId,
                                "target=" + destinationNodeId,
                                "intersected-node=" + node.Key,
                                "segment=" + Bounds(segment.Start, segment.End),
                                "node-rectangle=" + Bounds(node.Value.Bounds),
                                "route-provenance=" + route.Provenance,
                                "resource-provenance=" + segment.AllocationProvenance
                            });
                        if (!isEndpointNode)
                            Add(findings, "PHYSICAL-NODE-BODY-CROSSING", "physical-geometry", "Physical route crosses an unrelated node body.", route.PhysicalLinkId, segment.LogicalCells, new[] { segment.Start, segment.End }, new[] { node.Key });
                    }
                }
                if (segment.AllocationProvenance is null || segment.RunId.Length == 0 || segment.LaneId.Length == 0) Add(findings, "MISSING-PHYSICAL-PROVENANCE", "physical-geometry", "Physical segment lacks run/lane provenance.", route.PhysicalLinkId, segment.LogicalCells, new[] { segment.Start, segment.End }, Array.Empty<string>());
            }
            var bends = route.Points.Zip(route.Points.Skip(1), (a, b) => (a, b)).Zip(route.Points.Skip(2), (pair, c) => (pair.a, pair.b, c)).Count(x => (x.a.X == x.b.X) != (x.b.X == x.c.X));
            var allocatedBends = indexes.BendsByRoute.TryGetValue(route.PhysicalLinkId, out var routeBends) ? routeBends.Count : 0;
            var handoffs = indexes.HandoffsByRoute.TryGetValue(route.PhysicalLinkId, out var routeHandoffs) ? routeHandoffs.Count : 0;
            var endpointZBends = indexes.EndpointZBendsByRoute.TryGetValue(route.PhysicalLinkId, out var routeZBends) ? routeZBends.Count : 0;
            if (bends > allocatedBends + handoffs + 2 * endpointZBends) Add(findings, "UNALLOCATED-Z-GEOMETRY", "physical-geometry", "Physical polyline contains more bends than frozen bend/handoff/endpoint-Z allocation.", route.PhysicalLinkId, frozen.Cells, route.Points, new[] { "allocated-bends=" + allocatedBends, "allocated-endpoint-z-bends=" + endpointZBends + ";bend-allowance=2-per-endpoint-z" });
            var nodes = indexes.NodeById;
            ValidateTerminalEdge(source, nodes[source.PhysicalNodeId].Bounds, "SOURCE-NOT-EDGE", route, frozen, findings);
            ValidateTerminalEdge(destination, nodes[destination.PhysicalNodeId].Bounds, "DESTINATION-NOT-EDGE", route, frozen, findings);
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

    private static void ValidateEndpointLaneOrdering(ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneFreeze scene,
        ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var endpointGroups = new Dictionary<(string Node, ArchitectureV7EndpointKind Kind, int DirectionGroup), List<(ArchitectureV7TerminalSlotAssignment Terminal, ArchitectureV7PhysicalPoint Adjacent)>>();
        foreach (var terminal in allocation.Terminals)
        {
            if (!indexes.RouteById.TryGetValue(terminal.PhysicalLinkId, out var route) || route.Points.Count < 2) continue;
            var adjacent = FinalApproach(route, terminal.EndpointKind).End;
            var axisIsX = Math.Abs(adjacent.Y - (scene.Terminals.First(item => item.PhysicalLinkId == terminal.PhysicalLinkId && item.EndpointKind == terminal.EndpointKind).Position.Y)) > 0.001;
            var group = axisIsX
                ? terminal.Direction is ArchitectureV7EndpointDirection.Up or ArchitectureV7EndpointDirection.Down ? 1 : terminal.Direction == ArchitectureV7EndpointDirection.Left ? 0 : 2
                : terminal.Direction is ArchitectureV7EndpointDirection.Left or ArchitectureV7EndpointDirection.Right ? 1 : terminal.Direction == ArchitectureV7EndpointDirection.Up ? 0 : 2;
            var key = (terminal.PhysicalNodeId, terminal.EndpointKind, group);
            if (!endpointGroups.TryGetValue(key, out var entries)) endpointGroups[key] = entries = new();
            entries.Add((terminal, adjacent));
        }

        foreach (var group in endpointGroups)
        {
            var physicalTerminals = group.Value.Select(item =>
            {
                var terminal = scene.Terminals.First(item2 => item2.PhysicalLinkId == item.Terminal.PhysicalLinkId && item2.EndpointKind == item.Terminal.EndpointKind);
                var axis = Math.Abs(item.Adjacent.Y - terminal.Position.Y) > 0.001 ? terminal.Position.X : terminal.Position.Y;
                var adjacentAxis = Math.Abs(item.Adjacent.Y - terminal.Position.Y) > 0.001 ? item.Adjacent.X : item.Adjacent.Y;
                return (item.Terminal, terminal, adjacentAxis, axis);
            }).OrderBy(item => item.adjacentAxis).ThenBy(item => item.Terminal.PhysicalLinkId, StringComparer.Ordinal).ToArray();
            for (var index = 1; index < physicalTerminals.Length; index++)
            {
                var previous = physicalTerminals[index - 1];
                var current = physicalTerminals[index];
                if (previous.axis > current.axis)
                    Add(findings, "ENDPOINT-LANE-ORDER-INVERSION", "endpoint-allocation", "Physical adjacent lane order is inverted at the node terminal edge.", group.Key.Node,
                        Array.Empty<ArchitectureV7RouteCell>(), new[] { previous.terminal.Position, current.terminal.Position }, new[]
                        {
                            "node=" + group.Key.Node,
                            "edge=" + group.Key.Kind,
                            "relationship-a=" + previous.Terminal.PhysicalLinkId,
                            "relationship-b=" + current.Terminal.PhysicalLinkId,
                            "adjacent-axis=" + previous.adjacentAxis + "," + current.adjacentAxis,
                            "terminal-axis=" + previous.axis + "," + current.axis,
                            "direction-group=" + group.Key.DirectionGroup
                        });
            }
        }
    }

    private static void ValidateDirectCentreAuthority(ArchitectureV7CollectiveAllocationFreeze allocation,
        ArchitectureV7PhysicalSceneFreeze scene, ArchitectureV7PlacementFreeze placement, ValidationIndexes indexes,
        ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var directGroups = new Dictionary<(string Node, ArchitectureV7EndpointKind Kind), List<(string Link, ArchitectureV7LogicalRoute Frozen, ArchitectureV7PhysicalTerminal Terminal, (ArchitectureV7PhysicalPoint Start, ArchitectureV7PhysicalPoint End) Approach, double CentreX)>>();
        foreach (var terminal in allocation.Terminals)
        {
            if (!indexes.LogicalRouteById.TryGetValue(terminal.PhysicalLinkId, out var frozen) ||
                !indexes.RouteById.TryGetValue(terminal.PhysicalLinkId, out var route) ||
                !indexes.NodeById.TryGetValue(terminal.PhysicalNodeId, out var node) ||
                placement.Nodes.FirstOrDefault(x => x.PhysicalNodeId == terminal.PhysicalNodeId) is not { } logicalNode) continue;
            var endpointCell = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? frozen.Cells.FirstOrDefault()
                : frozen.Cells.LastOrDefault();
            var adjacentRun = terminal.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture
                ? allocation.Runs.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId && x.StartRouteIndex == 0)
                : allocation.Runs.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId && x.EndRouteIndex == frozen.Cells.Count - 1);
            if (endpointCell is null || adjacentRun?.Orientation != ArchitectureV7RunOrientation.Vertical || endpointCell.Column != logicalNode.CentreCell ||
                !terminal.Direction.Equals(ArchitectureV7EndpointDirection.Up) && !terminal.Direction.Equals(ArchitectureV7EndpointDirection.Down)) continue;
            var physicalTerminal = scene.Terminals.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId && x.EndpointKind == terminal.EndpointKind);
            var physicalRoute = scene.Routes.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
            if (physicalTerminal is null || physicalRoute is null) continue;
            var approach = FinalApproach(physicalRoute, terminal.EndpointKind);
            var centreX = (node.Bounds.Left + node.Bounds.Right) / 2d;
            var key = (terminal.PhysicalNodeId, terminal.EndpointKind);
            if (!directGroups.TryGetValue(key, out var group)) directGroups[key] = group = new();
            group.Add((terminal.PhysicalLinkId, frozen, physicalTerminal, approach, centreX));
        }
        foreach (var group in directGroups)
        {
            var ordered = group.Value.OrderBy(x => x.Terminal.Position.X).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var leftOffset = ordered[index].Terminal.Position.X - ordered[index].CentreX;
                var mirrorOffset = ordered[ordered.Length - index - 1].Terminal.Position.X - ordered[index].CentreX;
                if (Math.Abs(leftOffset + mirrorOffset) <= .001 &&
                    Math.Abs(ordered[index].Approach.Start.X - ordered[index].Terminal.Position.X) <= .001 &&
                    Math.Abs(ordered[index].Approach.End.X - ordered[index].Terminal.Position.X) <= .001) continue;
                Add(findings, "DIRECT-CENTRE-VIOLATION", "endpoint-allocation", "The direct vertical endpoint group is not centred symmetrically through the node and its final approaches.",
                    ordered[index].Link, ordered[index].Frozen.Cells, new[] { ordered[index].Terminal.Position, ordered[index].Approach.Start, ordered[index].Approach.End },
                    new[] { "node=" + group.Key.Node, "node-centre-x=" + ordered[index].CentreX, "terminal-x=" + ordered[index].Terminal.Position.X, "approach-x=" + ordered[index].Approach.Start.X });
            }
        }
    }

    private static void ValidateTerminalFinalLaneAuthority(ArchitectureV7CollectiveAllocationFreeze allocation,
        ArchitectureV7PhysicalSceneFreeze scene, ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var terminal in allocation.Terminals)
        {
            var physicalRoute = scene.Routes.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId);
            var physicalTerminal = scene.Terminals.FirstOrDefault(x => x.PhysicalLinkId == terminal.PhysicalLinkId && x.EndpointKind == terminal.EndpointKind);
            if (physicalRoute is null || physicalTerminal is null || !indexes.NodeById.TryGetValue(terminal.PhysicalNodeId, out var node)) continue;
            var approach = FinalApproach(physicalRoute, terminal.EndpointKind);
            var topBottom = Math.Abs(physicalTerminal.Position.Y - node.Bounds.Top) < .001 || Math.Abs(physicalTerminal.Position.Y - node.Bounds.Bottom) < .001;
            var aligned = topBottom
                ? approach.Start.X == approach.End.X && Math.Abs(approach.Start.X - physicalTerminal.Position.X) < .001
                : approach.Start.Y == approach.End.Y && Math.Abs(approach.Start.Y - physicalTerminal.Position.Y) < .001;
            if (!aligned)
                Add(findings, "TERMINAL-FINAL-LANE-AUTHORITY-VIOLATION", "endpoint-allocation", "The complete final perpendicular approach does not use the terminal coordinate.",
                    terminal.PhysicalLinkId, Array.Empty<ArchitectureV7RouteCell>(), new[] { physicalTerminal.Position, approach.Start, approach.End },
                    new[] { "terminal=" + physicalTerminal.Position, "approach-start=" + approach.Start, "approach-end=" + approach.End });
        }
    }

    private static void ValidateEndpointRegion(ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSceneFreeze scene,
        ArchitectureV7PhysicalSceneConfiguration configuration, ValidationIndexes indexes, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        foreach (var group in scene.Terminals.GroupBy(x => (x.PhysicalNodeId, x.EndpointKind)))
        {
            var node = indexes.NodeById[group.Key.PhysicalNodeId];
            var horizontal = group.All(x => Math.Abs(x.Position.Y - node.Bounds.Top) < .001 || Math.Abs(x.Position.Y - node.Bounds.Bottom) < .001);
            var terminals = group.OrderBy(x => horizontal ? x.Position.X : x.Position.Y).ToArray();
            for (var i = 1; i < terminals.Length; i++)
            {
                var distance = horizontal ? terminals[i].Position.X - terminals[i - 1].Position.X : terminals[i].Position.Y - terminals[i - 1].Position.Y;
                if (distance + .001 < configuration.TerminalPortSpacing)
                    Add(findings, "ENDPOINT-TERMINAL-SPACING", "endpoint-allocation", "Adjacent terminals on one node edge are closer than configured spacing.", group.Key.PhysicalNodeId,
                        Array.Empty<ArchitectureV7RouteCell>(), new[] { terminals[i - 1].Position, terminals[i].Position }, new[] { "required=" + configuration.TerminalPortSpacing, "actual=" + distance });
            }

            var capacityDemand = allocation.Terminals.Where(x => x.PhysicalNodeId == group.Key.PhysicalNodeId && x.EndpointKind == group.Key.EndpointKind)
                .Select(x => x.TerminalCapacityRequirement).DefaultIfEmpty(0).Max();
            var expanded = capacityDemand > configuration.BaseCellWidth &&
                (horizontal ? node.Bounds.Right - node.Bounds.Left : node.Bounds.Bottom - node.Bounds.Top) > configuration.BaseCellWidth;
            var occupied = horizontal ? terminals[terminals.Length - 1].Position.X - terminals[0].Position.X : terminals[terminals.Length - 1].Position.Y - terminals[0].Position.Y;
            if (expanded && terminals.Length >= 3 && occupied <= configuration.BaseCellWidth)
                Add(findings, "ENDPOINT-EDGE-UTILISATION", "endpoint-allocation", "Expanded node edge is not being used by a busy terminal population.", group.Key.PhysicalNodeId,
                    Array.Empty<ArchitectureV7RouteCell>(), terminals.Select(x => x.Position).ToArray(), new[] { "occupied=" + occupied, "edge=" + (horizontal ? node.Bounds.Right - node.Bounds.Left : node.Bounds.Bottom - node.Bounds.Top) });
        }

        foreach (var group in allocation.Terminals.GroupBy(x => (x.PhysicalNodeId, x.EndpointKind)))
        {
            var compiled = group.Select(item => (Allocation: item, Route: indexes.RouteById.TryGetValue(item.PhysicalLinkId, out var route) ? route : null,
                Frozen: indexes.LogicalRouteById.TryGetValue(item.PhysicalLinkId, out var frozen) ? frozen : null))
                .Where(x => x.Route is not null && x.Route.Points.Count >= 2 && x.Frozen is not null).ToArray();
            foreach (var item in compiled)
            {
                var physical = scene.Routes.FirstOrDefault(x => x.PhysicalLinkId == item.Allocation.PhysicalLinkId);
                var terminal = scene.Terminals.FirstOrDefault(x => x.PhysicalLinkId == item.Allocation.PhysicalLinkId && x.EndpointKind == item.Allocation.EndpointKind);
                if (physical is null || terminal is null || physical.Points.Count < 2) continue;
                var adjacent = FinalApproach(physical, item.Allocation.EndpointKind).End;
                var node = indexes.NodeById[item.Allocation.PhysicalNodeId];
                var horizontal = Math.Abs(terminal.Position.Y - node.Bounds.Top) < .001 || Math.Abs(terminal.Position.Y - node.Bounds.Bottom) < .001;
                var terminalAxis = horizontal ? terminal.Position.X : terminal.Position.Y;
                var dropAxis = horizontal ? adjacent.X : adjacent.Y;
                if (Math.Abs(terminalAxis - dropAxis) > .001)
                    Add(findings, "ENDPOINT-DROP-COORDINATE-MISMATCH", "endpoint-allocation", "Final perpendicular drop does not use the authoritative terminal coordinate.", item.Allocation.PhysicalLinkId,
                        item.Frozen!.Cells, new[] { terminal.Position, adjacent }, new[] { "terminal-axis=" + terminalAxis, "drop-axis=" + dropAxis });

            }

            var approaches = compiled.Select(item =>
            {
                var route = scene.Routes.First(x => x.PhysicalLinkId == item.Allocation.PhysicalLinkId);
                var terminal = scene.Terminals.First(x => x.PhysicalLinkId == item.Allocation.PhysicalLinkId && x.EndpointKind == item.Allocation.EndpointKind);
                var approach = FinalApproach(route, item.Allocation.EndpointKind);
                return (item, terminal, start: approach.Start, end: approach.End);
            }).ToArray();
            for (var i = 0; i < approaches.Length; i++)
                for (var j = i + 1; j < approaches.Length; j++)
                {
                    var a = approaches[i]; var b = approaches[j];
                    if (a.start.X == a.end.X && b.start.X == b.end.X)
                    {
                        var overlap = IntervalsOverlap(a.start.Y, a.end.Y, b.start.Y, b.end.Y);
                        var distance = Math.Abs(a.start.X - b.start.X);
                        if (overlap && distance < configuration.ParallelLaneSpacing)
                            Add(findings, "ENDPOINT-FINAL-LANE-OVERLAP", "endpoint-allocation", "Final vertical endpoint approach lanes are closer than configured spacing.", group.Key.PhysicalNodeId,
                                Array.Empty<ArchitectureV7RouteCell>(), new[] { a.start, b.start }, new[] { "required=" + configuration.ParallelLaneSpacing, "actual=" + distance });
                        if (overlap && distance < .001)
                            Add(findings, "ENDPOINT-FINAL-LANE-OVERLAP", "endpoint-allocation", "Final vertical endpoint approach lanes overlap.", group.Key.PhysicalNodeId,
                                Array.Empty<ArchitectureV7RouteCell>(), new[] { a.start, a.end, b.start, b.end }, new[] { a.item.Allocation.PhysicalLinkId, b.item.Allocation.PhysicalLinkId });
                    }
                    else if (a.start.Y == a.end.Y && b.start.Y == b.end.Y)
                    {
                        var overlap = IntervalsOverlap(a.start.X, a.end.X, b.start.X, b.end.X);
                        var distance = Math.Abs(a.start.Y - b.start.Y);
                        if (overlap && distance < configuration.ParallelLaneSpacing)
                            Add(findings, "ENDPOINT-FINAL-LANE-OVERLAP", "endpoint-allocation", "Final horizontal endpoint approach lanes are closer than configured spacing.", group.Key.PhysicalNodeId,
                                Array.Empty<ArchitectureV7RouteCell>(), new[] { a.start, b.start }, new[] { "required=" + configuration.ParallelLaneSpacing, "actual=" + distance });
                        if (overlap && distance < .001)
                            Add(findings, "ENDPOINT-FINAL-LANE-OVERLAP", "endpoint-allocation", "Final horizontal endpoint approach lanes overlap.", group.Key.PhysicalNodeId,
                                Array.Empty<ArchitectureV7RouteCell>(), new[] { a.start, a.end, b.start, b.end }, new[] { a.item.Allocation.PhysicalLinkId, b.item.Allocation.PhysicalLinkId });
                    }
                    else if (Intersects(a.start, a.end, b.start, b.end))
                        Add(findings, "ENDPOINT-APPROACH-CROSSING", "endpoint-allocation", "Endpoint approach segments cross within one node edge approach region.", group.Key.PhysicalNodeId,
                            Array.Empty<ArchitectureV7RouteCell>(), new[] { a.start, a.end, b.start, b.end }, new[] { a.item.Allocation.PhysicalLinkId, b.item.Allocation.PhysicalLinkId });
                }
        }
    }

    private static bool IntervalsOverlap(double aStart, double aEnd, double bStart, double bEnd) =>
        Math.Max(Math.Min(aStart, aEnd), Math.Min(bStart, bEnd)) < Math.Min(Math.Max(aStart, aEnd), Math.Max(bStart, bEnd)) - .001;

    private static (ArchitectureV7PhysicalPoint Start, ArchitectureV7PhysicalPoint End) FinalApproach(
        ArchitectureV7PhysicalRoute route, ArchitectureV7EndpointKind endpointKind)
    {
        var points = route.Points;
        if (points.Count < 2) return (points[0], points[points.Count - 1]);
        if (endpointKind == ArchitectureV7EndpointKind.SourceDeparture)
        {
            var start = 0;
            var horizontal = points[0].Y == points[1].Y;
            var index = 1;
            while (index + 1 < points.Count &&
                   (horizontal ? points[index].Y == points[index + 1].Y : points[index].X == points[index + 1].X)) index++;
            return (points[start], points[index]);
        }

        var end = points.Count - 1;
        var reverseHorizontal = points[points.Count - 1].Y == points[points.Count - 2].Y;
        var reverseIndex = points.Count - 2;
        while (reverseIndex - 1 >= 0 &&
               (reverseHorizontal ? points[reverseIndex].Y == points[reverseIndex - 1].Y : points[reverseIndex].X == points[reverseIndex - 1].X)) reverseIndex--;
        return (points[reverseIndex], points[end]);
    }

    private static bool Intersects(ArchitectureV7PhysicalPoint a, ArchitectureV7PhysicalPoint b, ArchitectureV7PhysicalPoint c, ArchitectureV7PhysicalPoint d)
    {
        if (a.X == b.X && c.Y == d.Y) return c.X > Math.Min(a.X, b.X) && c.X < Math.Max(a.X, b.X) && a.Y > Math.Min(c.Y, d.Y) && a.Y < Math.Max(c.Y, d.Y);
        if (a.Y == b.Y && c.X == d.X) return a.X > Math.Min(c.X, d.X) && a.X < Math.Max(c.X, d.X) && c.Y > Math.Min(a.Y, b.Y) && c.Y < Math.Max(a.Y, b.Y);
        return false;
    }

    private static void ValidateTerminalEdge(ArchitectureV7PhysicalTerminal terminal, ArchitectureV7PhysicalBounds bounds, string code,
        ArchitectureV7PhysicalRoute route, ArchitectureV7LogicalRoute frozen, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var onEdge = terminal.Position.X == bounds.Left || terminal.Position.X == bounds.Right || terminal.Position.Y == bounds.Top || terminal.Position.Y == bounds.Bottom;
        if (!onEdge) Add(findings, code, "physical-geometry", "Terminal is not on any edge of its node.", route.PhysicalLinkId, frozen.Cells, new[] { terminal.Position }, Array.Empty<string>());
    }

    private static string Bounds(ArchitectureV7PhysicalPoint start, ArchitectureV7PhysicalPoint end) =>
        $"[{start.X},{start.Y}]-[{end.X},{end.Y}]";

    private static string Bounds(ArchitectureV7PhysicalBounds bounds) =>
        $"[{bounds.Left},{bounds.Top}]-[{bounds.Right},{bounds.Bottom}]";

    private static bool IntersectsNodeBoundaryOrInterior(ArchitectureV7PhysicalSegment segment, ArchitectureV7PhysicalBounds bounds)
    {
        var left = Math.Min(segment.Start.X, segment.End.X);
        var right = Math.Max(segment.Start.X, segment.End.X);
        var top = Math.Min(segment.Start.Y, segment.End.Y);
        var bottom = Math.Max(segment.Start.Y, segment.End.Y);
        return left <= bounds.Right && right >= bounds.Left && top <= bounds.Bottom && bottom >= bounds.Top;
    }

    private static void ValidatePhysicalResourceProvenance(ArchitectureV7PhysicalRoute route, ArchitectureV7LogicalRoute frozen,
        ArchitectureV7CollectiveAllocationFreeze allocation, ArchitectureV7PhysicalSegment segment, ICollection<ArchitectureV7AcceptanceFinding> findings)
    {
        var run = allocation.Runs.FirstOrDefault(item => item.RunId == segment.RunId);
        var assignment = allocation.RunAssignments.FirstOrDefault(item => item.RunId == segment.RunId);
        var validIndexes = segment.RouteCellIndices.Count > 0 && segment.RouteCellIndices.All(index => index >= 0 && index < frozen.Cells.Count);
        var expectedCells = validIndexes
            ? segment.RouteCellIndices.Distinct().Select(index => frozen.Cells[index]).ToArray()
            : Array.Empty<ArchitectureV7RouteCell>();
        if (segment.AllocationProvenance.StartsWith("canonical-simplification;", StringComparison.Ordinal))
        {
            // Canonicalisation may merge adjacent allocated resources (for
            // example a terminal handoff and its straight run). Its aggregate
            // provenance must still identify the contributing resources and its
            // frozen route-cell projection must remain exact, but one physical
            // segment no longer has one run/assignment owner.
            var aggregateNamesResources = segment.AllocationProvenance.Contains("resource=", StringComparison.Ordinal) ||
                segment.AllocationProvenance.Contains("run=", StringComparison.Ordinal);
            var aggregateNamesLanes = segment.AllocationProvenance.Contains("lane=", StringComparison.Ordinal);
            if (!validIndexes || !segment.LogicalCells.SequenceEqual(expectedCells) || !aggregateNamesResources || !aggregateNamesLanes)
                Add(findings, "PHYSICAL-RESOURCE-PROVENANCE-MISMATCH", "physical-geometry", "Canonical physical segment provenance does not identify its contributing allocation resources and authoritative route cells.", route.PhysicalLinkId,
                    segment.LogicalCells, new[] { segment.Start, segment.End }, new[] { segment.RunId, segment.LaneId, segment.AllocationProvenance });
            return;
        }
        var cellsMatch = validIndexes && segment.LogicalCells.SequenceEqual(expectedCells) && expectedCells.All(cell => run?.Cells.Contains(cell) == true);
        var endpointResource = segment.RunId.StartsWith("handoff:", StringComparison.Ordinal) || segment.RunId.StartsWith("terminal:", StringComparison.Ordinal) || segment.RunId.StartsWith("endpoint-z-bend:", StringComparison.Ordinal);
        if (endpointResource)
            cellsMatch = validIndexes && segment.LogicalCells.SequenceEqual(expectedCells);
        var laneMatches = assignment is not null && string.Equals(assignment.LaneId, segment.LaneId, StringComparison.Ordinal);
        var orientationMatches = run is not null && ((segment.Start.Y == segment.End.Y && run.Orientation == ArchitectureV7RunOrientation.Horizontal) ||
            (segment.Start.X == segment.End.X && run.Orientation == ArchitectureV7RunOrientation.Vertical));
        var provenanceNamesResource = segment.AllocationProvenance.Contains("run=" + segment.RunId, StringComparison.Ordinal) &&
            segment.AllocationProvenance.Contains("lane=" + segment.LaneId, StringComparison.Ordinal);
        if (endpointResource)
            provenanceNamesResource = segment.AllocationProvenance.Contains("resource=" + segment.RunId, StringComparison.Ordinal)
                && segment.AllocationProvenance.Contains("lane=" + segment.LaneId, StringComparison.Ordinal);
        if ((!endpointResource && (run is null || assignment is null || !laneMatches || !orientationMatches)) || !cellsMatch || !provenanceNamesResource)
            Add(findings, "PHYSICAL-RESOURCE-PROVENANCE-MISMATCH", "physical-geometry", "Physical segment provenance does not identify the allocation run, lane and authoritative route cells that produced it.", route.PhysicalLinkId,
                segment.LogicalCells, new[] { segment.Start, segment.End }, new[] { segment.RunId, segment.LaneId, segment.AllocationProvenance });
    }

    private static bool HasCollinearReversal(IReadOnlyList<ArchitectureV7PhysicalPoint> points)
    {
        for (var index = 2; index < points.Count; index++)
        {
            var first = points[index - 2];
            var middle = points[index - 1];
            var last = points[index];
            if (first.Y == middle.Y && middle.Y == last.Y && Math.Sign(middle.X - first.X) != 0 && Math.Sign(middle.X - first.X) != Math.Sign(last.X - middle.X)) return true;
            if (first.X == middle.X && middle.X == last.X && Math.Sign(middle.Y - first.Y) != 0 && Math.Sign(middle.Y - first.Y) != Math.Sign(last.Y - middle.Y)) return true;
        }
        return false;
    }

    private static bool HasOvershootReturn(IReadOnlyList<ArchitectureV7PhysicalPoint> points)
    {
        var start = 0;
        while (start < points.Count - 1)
        {
            var first = points[start];
            var second = points[start + 1];
            if (first.X == second.X && first.Y == second.Y) { start++; continue; }
            var horizontal = first.Y == second.Y;
            var end = start + 1;
            while (end < points.Count - 1)
            {
                var current = points[end];
                var next = points[end + 1];
                if ((horizontal && current.Y != next.Y) || (!horizontal && current.X != next.X)) break;
                end++;
            }
            var initial = horizontal ? first.X : first.Y;
            var final = horizontal ? points[end].X : points[end].Y;
            var lower = Math.Min(initial, final);
            var upper = Math.Max(initial, final);
            for (var index = start + 1; index < end; index++)
            {
                var coordinate = horizontal ? points[index].X : points[index].Y;
                if (coordinate < lower || coordinate > upper) return true;
            }
            start = end;
        }
        return false;
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
        foreach (var route in routes.Routes)
        {
            foreach (var selection in route.AttemptEvidence.Where(item => item.Scenario == "continuation-selection"))
            {
                var selectedCost = ParseCost(selection.Provenance, "cost=");
                var smaller = selection.Candidates
                    .Where(candidate => candidate.Accepted)
                    .Select(candidate => ParseCost(candidate.RejectionReason, "cost="))
                    .Where(cost => cost.HasValue)
                    .Any(cost => selectedCost.HasValue && cost.Value < selectedCost.Value);
                if (smaller)
                    Add(findings, "SELECTED-NONMINIMAL-CONTINUATION", "logical-route", "Continuation selection did not choose the minimum finite candidate cost.", route.PhysicalLinkId,
                        selection.AttemptedCells, Array.Empty<ArchitectureV7PhysicalPoint>(), new[] { selection.Provenance });
            }
        }
    }

    private static int? ParseCost(string value, string marker)
    {
        var start = value.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = start;
        while (end < value.Length && char.IsDigit(value[end])) end++;
        return int.TryParse(value.Substring(start, end - start), out var cost) ? cost : null;
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
        public readonly Dictionary<string, IReadOnlyList<ArchitectureV7EndpointZBend>> EndpointZBendsByRoute;
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
            EndpointZBendsByRoute = allocation.EndpointZBends.GroupBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => (IReadOnlyList<ArchitectureV7EndpointZBend>)x.ToArray(), StringComparer.Ordinal);
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
