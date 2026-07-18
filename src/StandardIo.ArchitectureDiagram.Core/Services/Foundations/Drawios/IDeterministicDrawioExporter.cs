using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public interface IDeterministicDrawioExporter
{
    DrawioGenerationResult GenerateResult(
        DiagramModel diagram,
        DiagramSettings settings,
        System.Collections.Generic.IReadOnlyList<PipelineStageMetric>? upstreamTimings = null);

    DrawioDiagnosticExportResult ExportDiagnostic(DrawioGenerationResult generationResult);

    void ValidateStrict(DrawioGenerationResult generationResult);
}
