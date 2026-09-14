// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Text.Json.Serialization;

namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class DefinedType
{
    /// <summary>Full .NET type name without assembly qualification.</summary>
    public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NamespaceName { get; set; }
    /// <summary>Declaring assembly for an external boundary; source types are owned by their ProjectModel.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssemblyName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDataType { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAnonymousType { get; set; }
    /// <summary>Includes non-public declared behaviour; null supports older saved models.</summary>
    public bool? HasDeclaredBehaviour { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseTypeName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? InterfaceNames { get; set; }
    public FrameworkType FrameworkType { get; set; }
    /// <summary>Defined in a selected project, regardless of C# accessibility; false marks a traversal boundary.</summary>
    public bool IsInternal { get; set; }
    public Field[]? Fields { get; set; }
    public Property[]? Properties { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompositionMember[]? CompositionMembers { get; set; }
    public Method[]? Methods { get; set; }
}