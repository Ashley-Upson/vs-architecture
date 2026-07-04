using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public interface IDiagramRendererRegistry
{
    IReadOnlyList<IDiagramRenderer> Renderers { get; }

    IDiagramRenderer Resolve(string? rendererId);
}
