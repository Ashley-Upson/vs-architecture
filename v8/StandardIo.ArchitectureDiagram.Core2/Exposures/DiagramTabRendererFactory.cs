using System;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal interface IDiagramTabRendererFactory { IDiagramRenderer Create(string namedKey); }
internal sealed class DiagramTabRendererFactory(IServiceProvider provider) : IDiagramTabRendererFactory
{
    public IDiagramRenderer Create(string namedKey) => provider.GetRequiredKeyedService<IDiagramRenderer>(namedKey);
}
