using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record ProjectContainer(
    string Id,
    string Name,
    IReadOnlyList<TypeNode> Types,
    string UniqueId = "");
