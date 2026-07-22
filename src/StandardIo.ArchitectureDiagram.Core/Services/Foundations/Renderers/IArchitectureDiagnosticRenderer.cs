using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public interface IArchitectureDiagnosticRenderer
{
    ArchitectureRenderResult RenderWithDiagnostics(
        ArchitectureRenderGraph graph,
        ArchitectureRenderSettings settings,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        CancellationToken cancellationToken = default);
}
