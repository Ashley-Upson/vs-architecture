using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum AdjacentDownwardRejectionReason
{
    SameLayer,
    SkippedLayer,
    UpwardOrReturn,
    CrossProject,
    ExposureTreeSpecific,
    NonOrthogonal,
    MultipleInterLayer,
    UnsupportedConnectionTopology,
    RevisionMismatch
}

internal sealed record AdjacentDownwardLinkContext(
    LinkLayout Route,
    NodeLayout Source,
    NodeLayout Target,
    LayoutRevision LayoutRevision,
    RouteRevision RouteRevision,
    IReadOnlyList<InterLayerLinkDemand> InterLayerDemands,
    IReadOnlyDictionary<InterLayerId, AxisInterval> InterLayerAxisRanges,
    bool ExposureTreeSpecific);
