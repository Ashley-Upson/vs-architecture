using System;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ProjectRegionLayout(
    RenderGraph Graph,
    IReadOnlyDictionary<string, NodeLayout> Nodes,
    IReadOnlyDictionary<string, ProjectLayout> Projects,
    IReadOnlyDictionary<string, LinkLayout> Links,
    TraceabilityValidationResult Traceability,
    IReadOnlyList<PipelineStageMetric> StageTimings,
    LayoutRevision LayoutRevision,
    IReadOnlyDictionary<string, CanonicalTopologyPlan> CanonicalTopologyPlans,
    ProjectSlotCompilation ProjectSlotCompilation)
{
    public ProjectRegionLayout WithProjects(IReadOnlyDictionary<string, ProjectLayout> projects) =>
        this with { Projects = projects ?? throw new ArgumentNullException(nameof(projects)) };
}

internal sealed record DiagramSerializationLayout(
    RenderGraph Graph,
    IReadOnlyDictionary<string, NodeLayout> Nodes,
    IReadOnlyDictionary<string, ProjectLayout> Projects,
    IReadOnlyDictionary<string, LinkLayout> Links,
    LayoutRevision LayoutRevision);

internal sealed record LegacyRouteSerializationMetadata(
    CorridorPathSelectionResult? PathSelection,
    RegionalPathSelectionResult? RegionalPathSelection,
    EdgeTraversalCompilation Traversals);
