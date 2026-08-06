using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class ArchitectureDiagramV6Validator : IPlannedArchitectureDiagramValidator
{
    public PlannedArchitectureValidationResult Validate(PlannedArchitectureDiagram diagram)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        var findings = new List<ArchitecturePlanningDiagnostic>();
        var anchors = new HashSet<PlanningGridCellId>();
        var occupied = new Dictionary<PlanningGridCellId, string>();
        var nodesById = diagram.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var gridsById = diagram.ProjectGrids.ToDictionary(grid => grid.Grid.Id.Value, StringComparer.Ordinal);
        var allGridsById = diagram.ProjectGrids.Select(item => item.Grid).Concat(new[] { diagram.DiagramGrid.Grid })
            .ToDictionary(grid => grid.Id.Value, StringComparer.Ordinal);
        var occupancy = new ArchitectureV6OccupancyAuthority(diagram.PhysicalNodes, diagram.NodePlacements,
            diagram.ProjectGrids.Select(item => item.Grid).Append(diagram.DiagramGrid.Grid));
        findings.AddRange(occupancy.Diagnostics);
        foreach (var placement in diagram.NodePlacements)
        {
            if (!nodesById.ContainsKey(placement.PhysicalNodeId))
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownPhysicalNodePlacement", "A placement references an unknown physical node.", PlanningDiagnosticSubject.PhysicalNode, placement.PhysicalNodeId));
            if (!anchors.Add(placement.AnchorCellId))
                findings.Add(new ArchitecturePlanningDiagnostic("DuplicateNodeAnchor", "A node anchor cell belongs to more than one physical node.", PlanningDiagnosticSubject.Cell, placement.AnchorCellId.ToString()));
            if (!placement.Footprint.Contains(placement.AnchorCellId))
                findings.Add(new ArchitecturePlanningDiagnostic("AnchorOutsideFootprint", "A node anchor must be contained by its footprint.", PlanningDiagnosticSubject.Cell, placement.AnchorCellId.ToString()));
            if (placement.Footprint.Count == 0 || placement.Footprint.Count % 2 == 0)
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidNodeFootprint", "A node footprint must be positive and odd-width.", PlanningDiagnosticSubject.PhysicalNode, placement.PhysicalNodeId));
            foreach (var cell in placement.Footprint)
            {
                if (occupied.ContainsKey(cell))
                    findings.Add(new ArchitecturePlanningDiagnostic("LogicalFootprintOverlap", $"Node footprint overlaps physical node '{occupied[cell]}'.", PlanningDiagnosticSubject.Cell, cell.ToString()));
                else
                    occupied[cell] = placement.PhysicalNodeId;
            }
            if (!gridsById.ContainsKey(placement.GridId.Value))
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownProjectGrid", "A node placement references a project grid that does not exist.", PlanningDiagnosticSubject.Grid, placement.GridId.Value));
        }
        foreach (var route in diagram.Routes)
        {
            var routeLink = diagram.PhysicalLinks.SingleOrDefault(link => link.PhysicalLinkId == route.PhysicalLinkId);
            if (routeLink is null)
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownPhysicalLink", "A planned route does not reference a physical link.", PlanningDiagnosticSubject.RouteStep, route.PhysicalLinkId));
            if (route.Source.Side != GridSide.Bottom || route.Destination.Side != GridSide.Top)
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidRouteEndpointSide", "Abstract routes must leave node bottoms and enter node tops.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
            var previousOrder = -1;
            foreach (var step in route.Steps)
            {
                if (step.EntrySide == step.ExitSide)
                    findings.Add(new ArchitecturePlanningDiagnostic("InvalidRouteStepSides", "A route step must change direction or represent a valid turn.", PlanningDiagnosticSubject.RouteStep, route.PhysicalLinkId));
                if (step.Order <= previousOrder)
                    findings.Add(new ArchitecturePlanningDiagnostic("UnorderedRouteSteps", "Route steps must have deterministic increasing order.", PlanningDiagnosticSubject.RouteStep, route.PhysicalLinkId));
                previousOrder = step.Order;
                if (!allGridsById.TryGetValue(step.GridId.Value, out var grid) || !grid.Cells.TryGetValue(step.CellId, out var cell))
                    findings.Add(new ArchitecturePlanningDiagnostic("UnknownRouteCell", "A route step must reference an existing grid cell.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
                else
                {
                    if ((cell.Capabilities & CellCapability.RoutingAllowed) == 0)
                        findings.Add(new ArchitecturePlanningDiagnostic("RouteCellNotRoutable", "A route step uses a cell without routing capability.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
                    var resolution = occupancy.Resolve(step.CellId);
                    if (resolution.Status is ArchitectureV6OccupancyStatus.Ambiguous or ArchitectureV6OccupancyStatus.Inconsistent)
                        findings.Add(new ArchitecturePlanningDiagnostic("InvalidNodeFootprintOwnership",
                            resolution.Message ?? "A route cell has invalid node footprint ownership.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
                    else if (routeLink is not null && resolution.IsOccupied && !occupancy.IsExactEndpointCell(step.CellId, step, routeLink))
                        findings.Add(new ArchitecturePlanningDiagnostic("RouteEntersUnrelatedFootprint", "A route may not pass through an occupied node footprint.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
                }
            }
            var physicalLink = diagram.PhysicalLinks.SingleOrDefault(link => link.PhysicalLinkId == route.PhysicalLinkId);
            if (physicalLink is not null && physicalLink.SourceProjectId == physicalLink.DestinationProjectId && route.Steps.Any(step => step.GridId.Value == diagram.DiagramGrid.Grid.Id.Value))
                findings.Add(new ArchitecturePlanningDiagnostic("LocalRouteEnteredDiagramGrid", "Project-local routes must remain within their project grid.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
            if (physicalLink is not null && physicalLink.SourceProjectId != physicalLink.DestinationProjectId && route.Transitions.Count == 0)
                findings.Add(new ArchitecturePlanningDiagnostic("MissingProjectTransition", "Cross-project routes require explicit grid transitions.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
        }
        foreach (var link in diagram.PhysicalLinks)
            if (diagram.Routes.Count(route => route.PhysicalLinkId == link.PhysicalLinkId) != 1)
                findings.Add(new ArchitecturePlanningDiagnostic("PhysicalLinkRouteCount", "Every physical link must have exactly one abstract route or explicit unsupported result.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
        foreach (var link in diagram.PhysicalLinks)
        {
            if (!nodesById.ContainsKey(link.SourcePhysicalNodeId) || !nodesById.ContainsKey(link.DestinationPhysicalNodeId))
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownPhysicalLinkEndpoint", "A physical link references an unknown endpoint.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
        }
        foreach (var node in diagram.PhysicalNodes)
        {
            var placements = diagram.NodePlacements.Count(placement => placement.PhysicalNodeId == node.PhysicalNodeId);
            if (placements != 1)
                findings.Add(new ArchitecturePlanningDiagnostic("PhysicalNodePlacementCount", "Every physical node must have exactly one placement.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            if (node.ProjectionMode == PhysicalNodeProjectionMode.DuplicateBranch && node.DuplicationProvenance is null)
                findings.Add(new ArchitecturePlanningDiagnostic("MissingDuplicationProvenance", "Duplicate physical nodes require duplication provenance.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }
        if (diagram.Projection is not null && diagram.Request.NodeProjection.Mode == NodeProjectionMode.Canonical)
            foreach (var mapping in diagram.Projection.SemanticNodeToPhysicalNodeIds)
                if (mapping.Value.Count != 1)
                    findings.Add(new ArchitecturePlanningDiagnostic("CanonicalProjectionCount", "Canonical mode must produce one physical node per semantic node.", PlanningDiagnosticSubject.SemanticNode, mapping.Key));
        ValidateGeometry(diagram, findings);
        return new PlannedArchitectureValidationResult(findings.Count == 0, findings);
    }

    private static void ValidateGeometry(PlannedArchitectureDiagram diagram, ICollection<ArchitecturePlanningDiagnostic> findings)
    {
        if (diagram.Geometry is null) return;
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in diagram.Geometry.Nodes)
        {
            if (!nodeIds.Add(node.PhysicalNodeId))
                findings.Add(new ArchitecturePlanningDiagnostic("DuplicateNodeGeometry", "A physical node has more than one geometry record.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            if (node.RelativeBounds.Width <= 0 || node.RelativeBounds.Height <= 0 || node.AbsoluteBounds.Width <= 0 || node.AbsoluteBounds.Height <= 0)
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidGeometryDimension", "Geometry dimensions must be positive.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }
        foreach (var physicalNode in diagram.PhysicalNodes)
            if (!nodeIds.Contains(physicalNode.PhysicalNodeId))
                findings.Add(new ArchitecturePlanningDiagnostic("MissingNodeGeometry", "Every physical node must have geometry once geometry is complete.", PlanningDiagnosticSubject.PhysicalNode, physicalNode.PhysicalNodeId));
        foreach (var project in diagram.Geometry.Projects)
        {
            var owned = diagram.Geometry.Nodes.Where(node => node.ProjectId == project.ProjectId).ToArray();
            foreach (var node in owned)
                if (!Contains(project.RelativeBounds, node.RelativeBounds))
                    findings.Add(new ArchitecturePlanningDiagnostic("GeometryContainmentViolation", "A project does not contain an owned node geometry.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            for (var left = 0; left < owned.Length; left++)
                for (var right = left + 1; right < owned.Length; right++)
                    if (Intersects(owned[left].RelativeBounds, owned[right].RelativeBounds))
                        findings.Add(new ArchitecturePlanningDiagnostic("GeometryCollision", "Physical node geometries overlap.", PlanningDiagnosticSubject.PhysicalNode, owned[left].PhysicalNodeId + ":" + owned[right].PhysicalNodeId));
        }
        var bounds = diagram.Geometry.AbsoluteDiagramBounds;
        foreach (var node in diagram.Geometry.Nodes)
            if (!Contains(bounds, node.AbsoluteBounds))
                findings.Add(new ArchitecturePlanningDiagnostic("GeometryBoundsError", "A node geometry falls outside the diagram bounds.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
    }

    private static bool Contains(RelativeRectangle outer, RelativeRectangle inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static bool Contains(AbsoluteRectangle outer, AbsoluteRectangle inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static bool Intersects(RelativeRectangle left, RelativeRectangle right) =>
        left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
}
