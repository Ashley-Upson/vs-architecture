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
    IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> RequiredExtentByBand,
    int InterLayerCount,
    int ExpandedInterLayerCount,
    int RefinementIterations,
    bool RefinementFallbackUsed,
    IReadOnlyList<PipelineStageMetric> Timings,
    IReadOnlyList<ProjectLayerExpansionIteration>? ExpansionIterations = null,
    bool ExpansionCycleResolutionUsed = false);

internal readonly record struct ProjectLayerExpansionIdentity(string ProjectId, int LowerDepth);

internal sealed record ProjectLayerExpansionIteration(
    int Iteration,
    string ExpansionMapHash,
    IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> ActualGapByBand,
    IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> RequiredExtentByBand,
    IReadOnlyList<ProjectLayerExpansionIdentity> ChangedBands,
    string ChangeKind,
    bool CycleHandlingUsed);
