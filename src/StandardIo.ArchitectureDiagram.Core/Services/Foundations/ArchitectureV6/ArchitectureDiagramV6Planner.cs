using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class ArchitectureDiagramV6Planner : IArchitectureDiagramPlanner
{
    public PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var diagramGrid = EmptyGrid(new PlanningGridId("diagram"));
        var projectGrids = request.SemanticModel.Projects
            .Select(project => new ProjectRoutingGrid(project.Id, EmptyGrid(new PlanningGridId($"project:{project.Id}")),
                Array.Empty<SubtreeReservation>(), null))
            .ToArray();
        var routingGrid = new DiagramRoutingGrid(diagramGrid, Array.Empty<RelativeRectangle>(), Array.Empty<GridTransition>());
        var metrics = new ArchitecturePlanningMetrics(
            request.SemanticModel.Projects.Sum(project => project.Nodes.Count) + request.SemanticModel.ExternalNodes.Count,
            request.SemanticModel.Links.Count,
            0, 0, projectGrids.Length, 0, 0, 0, 0,
            new Dictionary<string, int>(StringComparer.Ordinal), 0, 0, 0, null, 0);
        var diagnostics = new ArchitecturePlanningDiagnostics(
            new[] { new ArchitecturePlanningDiagnostic(
                "V6PlanningNotImplemented",
                "V6 planning structures are active; physical projection and geometry planning are intentionally deferred.",
                PlanningDiagnosticSubject.Grid, diagramGrid.Id.Value) }, metrics);
        return new PlannedArchitectureDiagram(
            request, Array.Empty<PlannedPhysicalNode>(), Array.Empty<PlannedPhysicalLink>(), routingGrid,
            projectGrids, Array.Empty<PlannedNodePlacement>(), Array.Empty<PlannedGridRoute>(),
            new GridTrackSizingPlan(Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
                Array.Empty<GridTrackConstraint>(), null), diagnostics);
    }

    private static PlanningGrid EmptyGrid(PlanningGridId id) => new(
        id, Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
        new Dictionary<PlanningGridCellId, PlanningGridCell>(), new GridTransform(id, new RelativePoint(0, 0)));
}
