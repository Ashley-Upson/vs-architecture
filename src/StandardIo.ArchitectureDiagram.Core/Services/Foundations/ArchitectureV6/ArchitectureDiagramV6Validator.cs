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
        foreach (var placement in diagram.NodePlacements)
            if (!anchors.Add(placement.AnchorCellId))
                findings.Add(new ArchitecturePlanningDiagnostic("DuplicateNodeAnchor", "A node anchor cell belongs to more than one physical node.", PlanningDiagnosticSubject.Cell, placement.AnchorCellId.ToString()));
        foreach (var route in diagram.Routes)
            if (!diagram.PhysicalLinks.Any(link => link.PhysicalLinkId == route.PhysicalLinkId))
                findings.Add(new ArchitecturePlanningDiagnostic("UnknownPhysicalLink", "A planned route does not reference a physical link.", PlanningDiagnosticSubject.RouteStep, route.PhysicalLinkId));
        return new PlannedArchitectureValidationResult(findings.Count == 0, findings);
    }
}
