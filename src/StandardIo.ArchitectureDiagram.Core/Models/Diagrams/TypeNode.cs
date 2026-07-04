using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record TypeNode(
    string Id,
    string ProjectId,
    string Name,
    string FullName,
    string Kind,
    string UniqueId = "",
    IReadOnlyList<string>? Interfaces = null,
    IReadOnlyList<TypeProperty>? Properties = null,
    int MethodCount = 0);
