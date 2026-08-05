using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public interface IArchitectureDiagramRenderer<out TOutput>
{
    TOutput Render(PlannedArchitectureDiagram diagram, ArchitectureRenderRequest request);
}

public sealed record ArchitectureRenderRequest(
    ArchitectureValidationMode ValidationMode,
    string OutputRenderer,
    bool IncludeDiagnostics);
