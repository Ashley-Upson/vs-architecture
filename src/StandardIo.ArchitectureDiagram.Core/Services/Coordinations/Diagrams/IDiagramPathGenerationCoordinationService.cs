using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;

public interface IDiagramPathGenerationCoordinationService
{
    Task<DiagramPathGenerationResult> GenerateAsync(
        string inputPath,
        DiagramSettings settings,
        string? outputPath = null,
        string? projectFilter = null,
        CancellationToken cancellationToken = default);
}
