using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IDiagramTabService { RenderedDiagramTab Render(RenderModel source, DiagramTypes type, DiagramFormats format); }
internal sealed class DiagramTabService(IDiagramTabBroker broker, IProjectModelTreeService trees) : IDiagramTabService
{
    public RenderedDiagramTab Render(RenderModel source, DiagramTypes type, DiagramFormats format)
    {
        var projects = type == DiagramTypes.Architecture && !source.Configuration.NoDuplicates
            ? source.ProjectModels.SelectMany(p => trees.Split(p)).ToArray() : source.ProjectModels;
        return new(type, broker.Render(new RenderModel(projects, source.Configuration, type), format));
    }
}
