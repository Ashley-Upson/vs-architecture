using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
internal interface IDiagramTabBroker { byte[] Render(RenderModel model, DiagramFormats format); }
internal sealed class DiagramTabBroker(IDiagramTabRendererFactory factory) : IDiagramTabBroker
{
    public byte[] Render(RenderModel model, DiagramFormats format) => factory.Create($"{format}_{model.DiagramType}").Render(model);
}
