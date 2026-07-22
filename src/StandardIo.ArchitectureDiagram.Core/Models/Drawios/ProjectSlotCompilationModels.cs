using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ProjectSlotCompilation(
    IReadOnlyDictionary<string, LinkLayout> Links,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyDictionary<string, AssignedLinkSegment> Assignments,
    VerticalLinkColumnAssignment VerticalColumns,
    IReadOnlyDictionary<string, string> ReturnSideByRouteId,
    IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> RequiredLayerExpansion,
    int InterLayerCount,
    int ExpandedInterLayerCount,
    IReadOnlyList<PipelineStageMetric> Timings);

internal readonly record struct ProjectLayerExpansionIdentity(string ProjectId, int LowerDepth);
