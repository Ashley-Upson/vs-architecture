using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public interface IArchitectureRenderer<out TPage>
{
    TPage Render(
        ArchitectureRenderGraph graph,
        ArchitectureRenderSettings settings,
        CancellationToken cancellationToken = default);
}
