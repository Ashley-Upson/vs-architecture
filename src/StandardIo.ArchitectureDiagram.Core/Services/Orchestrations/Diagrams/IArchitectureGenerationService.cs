using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public interface IArchitectureGenerationService
{
    Task<TypedArchitectureGenerationResult> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        int serializationRepeatCount = 0,
        CancellationToken cancellationToken = default);

    Task<TypedArchitectureGenerationResult> GenerateAsync(
        ArchitectureDiagramModel diagram,
        ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        int serializationRepeatCount = 0,
        CancellationToken cancellationToken = default);
}
