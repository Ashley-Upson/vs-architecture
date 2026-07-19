using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ProjectSlotCompilation(
    IReadOnlyDictionary<string, LinkLayout> Links,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyDictionary<string, AssignedLinkSegment> Assignments,
    VerticalLinkColumnAssignment VerticalColumns,
    IReadOnlyDictionary<string, string> ReturnSideByRouteId,
    IReadOnlyDictionary<int, int> RequiredLayerExpansionByLowerDepth,
    int InterLayerCount,
    int ExpandedInterLayerCount);
