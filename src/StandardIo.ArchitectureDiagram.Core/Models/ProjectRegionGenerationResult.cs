using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record ProjectRegionGenerationResult(
    bool Eligible,
    IReadOnlyList<string> FallbackReasons,
    string Document,
    string InvariantJson,
    IReadOnlyList<PipelineStageMetric> StageTimings,
    int NodeCount,
    int LinkCount);
