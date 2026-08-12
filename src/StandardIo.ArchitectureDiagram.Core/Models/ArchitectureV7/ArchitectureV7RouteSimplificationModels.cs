using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7RouteSimplificationEvidence(
    string PhysicalLinkId,
    int PointsBefore,
    int PointsAfter,
    int RedundantPointsRemoved,
    int ReversalTransitions,
    int OvershootGroups,
    int SameAxisLaneMismatchCount,
    int DiagonalSegmentCount,
    bool Simplified,
    IReadOnlyList<string> ResourceProvenance);

public sealed class ArchitectureV7RouteSimplificationResult
{
    public ArchitectureV7RouteSimplificationResult(
        ArchitectureV7PhysicalSceneFreeze scene,
        IReadOnlyList<ArchitectureV7RouteSimplificationEvidence> evidence)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Evidence = Array.AsReadOnly((evidence ?? Array.Empty<ArchitectureV7RouteSimplificationEvidence>()).ToArray());
    }

    public ArchitectureV7PhysicalSceneFreeze Scene { get; }
    public IReadOnlyList<ArchitectureV7RouteSimplificationEvidence> Evidence { get; }
}
