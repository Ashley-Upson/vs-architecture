using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record DisconnectedNodeProjectLayout(
    RenderProject Project,
    ProjectLayout ProjectLayout,
    IReadOnlyDictionary<string, NodeLayout> Nodes,
    int NodesPerLayer,
    IReadOnlyList<string> NodeIds);
