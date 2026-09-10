// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

namespace StandardIo.ArchitectureDiagram.Core2.Models;

public sealed class DefinedType
{
    /// <summary>Full .NET type name without assembly qualification.</summary>
    public string? Name { get; set; }

    public FrameworkType FrameworkType { get; set; }

    /// <summary>Defined in a selected project, regardless of C# accessibility; false marks a traversal boundary.</summary>
    public bool IsInternal { get; set; }

    public Field[]? Fields { get; set; }

    public Property[]? Properties { get; set; }

    public Method[]? Methods { get; set; }
}