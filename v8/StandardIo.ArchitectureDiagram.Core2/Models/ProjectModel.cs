// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class ProjectModel
{
    internal bool IsSplitTree { get; set; }
    internal string? RootTypeName { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public DefinedType[]? Types { get; set; }
    public TypeRelationship[]? Dependencies { get; set; }
}