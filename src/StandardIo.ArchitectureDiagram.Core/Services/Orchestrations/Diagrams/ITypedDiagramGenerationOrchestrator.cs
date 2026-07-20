using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public interface ITypedDiagramGenerationOrchestrator
{
    Task<TypedDiagramGenerationResult> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        DiagramGenerationRequest request,
        CancellationToken cancellationToken = default);
}
