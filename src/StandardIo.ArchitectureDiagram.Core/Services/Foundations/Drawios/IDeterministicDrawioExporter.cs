using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

/// <summary>
/// Compatibility contract for legacy semantic models. New typed Architecture generation projects an
/// ArchitectureRenderGraph and calls DeterministicDrawioExporter through DrawioArchitectureRenderer.
/// </summary>
public interface IDeterministicDrawioExporter
{
    DrawioGenerationResult GenerateResult(
        DiagramModel diagram,
        DiagramSettings settings,
        System.Collections.Generic.IReadOnlyList<PipelineStageMetric>? upstreamTimings = null);

    DrawioDiagnosticExportResult ExportDiagnostic(DrawioGenerationResult generationResult);

    void ValidateStrict(DrawioGenerationResult generationResult);
}
