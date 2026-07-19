using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ProjectSlotCompilation(
    IReadOnlyDictionary<string, LinkLayout> Links,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyDictionary<string, AssignedLinkSegment> Assignments,
    int InterLayerCount,
    int ExpandedInterLayerCount);
