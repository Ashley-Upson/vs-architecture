using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public interface IArchitectureDiagnosticRenderer
{
    ArchitectureRenderResult RenderWithDiagnostics(
        ArchitectureDiagramModel model,
        ArchitectureRenderSettings settings,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        CancellationToken cancellationToken = default);
}
