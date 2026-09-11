using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;

public interface IArchitectureAnalyser
{
    Task<ArchitectureDiagramModel> AnalyseAsync(
        IEnumerable<Project> selectedProjects,
        ArchitectureAnalysisSettings settings,
        CancellationToken cancellationToken = default);
}
