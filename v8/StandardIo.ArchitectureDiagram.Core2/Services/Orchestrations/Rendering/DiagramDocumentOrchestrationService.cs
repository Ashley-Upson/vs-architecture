using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
internal interface IDiagramDocumentOrchestrationService { byte[] Render(RenderModel model, DiagramFormats format); }
internal sealed class DiagramDocumentOrchestrationService(IDiagramTabService tabs, IDocumentCompilationService compiler) : IDiagramDocumentOrchestrationService
{
    public byte[] Render(RenderModel model, DiagramFormats format) => compiler.Compile(new[] { DiagramTypes.Architecture, DiagramTypes.Composition, DiagramTypes.CallChain, DiagramTypes.DataModel }.Select(type => tabs.Render(model, type, format)).ToArray(), format);
}
