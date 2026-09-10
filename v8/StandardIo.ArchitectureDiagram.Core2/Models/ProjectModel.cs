// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

namespace StandardIo.ArchitectureDiagram.Core2.Models;

public sealed class ProjectModel
{
    public string? Name { get; set; }

    public string? Path { get; set; }

    public DefinedType[]? Types { get; set; }

    public Dependency[]? Dependencies { get; set; }
}