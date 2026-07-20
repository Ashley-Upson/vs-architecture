using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public interface IArchitectureRenderer<out TPage>
{
    TPage Render(
        ArchitectureDiagramModel model,
        ArchitectureRenderSettings settings,
        CancellationToken cancellationToken = default);
}
