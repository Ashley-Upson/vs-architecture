// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class TypeRelationship
{
    public DependencyType DependencyType { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsComposition { get; set; }
    /// <summary>Full .NET name of the dependent type, without assembly qualification.</summary>
    public string? FromType { get; set; }
    /// <summary>Full .NET name of the referenced type, without assembly qualification.</summary>
    public string? ToType { get; set; }
    /// <summary>Calling method name; null for inheritance or a field/property initializer.</summary>
    public string? FromMethod { get; set; }
    /// <summary>Called method name; null for inheritance.</summary>
    public string? ToMethod { get; set; }
}