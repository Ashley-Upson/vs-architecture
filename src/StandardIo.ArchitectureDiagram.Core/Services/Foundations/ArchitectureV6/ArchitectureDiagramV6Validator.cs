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
            if (!diagram.PhysicalLinks.Any(link => link.PhysicalLinkId == route.PhysicalLinkId))
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownPhysicalLink", "A planned route does not reference a physical link.", PlanningDiagnosticSubject.RouteStep, route.PhysicalLinkId));
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
        return new PlannedArchitectureValidationResult(findings.Count == 0, findings);
    }
}
