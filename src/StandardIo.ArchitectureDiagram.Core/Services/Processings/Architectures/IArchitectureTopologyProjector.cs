using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;

public interface IArchitectureTopologyProjector
{
    ArchitectureRenderGraph Project(ArchitectureDiagramModel diagram, NodeDuplicationSettings settings);
}
