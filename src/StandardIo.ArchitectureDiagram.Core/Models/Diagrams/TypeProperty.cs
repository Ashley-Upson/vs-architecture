namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record TypeProperty(
    string Name,
    string TypeName,
    string? TypeFullName = null,
    string? TypeId = null);
