using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public interface IDeterministicDrawioExporter
{
    string Export(DiagramModel diagram, DiagramSettings settings);

    DrawioGenerationResult GenerateResult(DiagramModel diagram, DiagramSettings settings);

    DrawioDiagnosticExportResult ExportDiagnostic(DiagramModel diagram, DiagramSettings settings);

    DrawioDiagnosticExportResult ExportDiagnostic(DrawioGenerationResult generationResult);
}
