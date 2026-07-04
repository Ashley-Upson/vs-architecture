namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record DiagramMetadata(
    int SchemaVersion = 1,
    string GeneratedBy = "StandardIo.ArchitectureDiagram");
