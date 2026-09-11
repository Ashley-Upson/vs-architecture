// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class Property
{
    /// <summary>Full .NET type name without assembly qualification or C# aliases.</summary>
    public string? Type { get; set; }
    public string? Name { get; set; }
}