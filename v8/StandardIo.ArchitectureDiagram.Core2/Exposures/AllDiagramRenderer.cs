using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal sealed class AllDiagramRenderer(IDiagramDocumentOrchestrationService service, DiagramFormats format) : IDiagramRenderer
{
    public byte[] Render(RenderModel model) => service.Render(model, format);
}
