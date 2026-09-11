using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.DataModels;

public enum DataModelRelationshipKind
{
    PropertyReference,
    CollectionPropertyReference,
    Inheritance,
    Unknown
}

public sealed record DataModelSourceLocation(string FilePath, int Line, int Column);

public sealed record DataModelProperty(
    string Id,
    string DeclaringEntityId,
    string Name,
    string DeclaredTypeDisplay,
    string? TargetTypeId,
    string Accessibility,
    bool IsStatic,
    bool HasGetter,
    bool HasSetter,
    bool HasInit,
    bool IsNullable,
    bool IsCollection,
    string? CollectionElementTypeId,
    string OrderKey);

public sealed record DataModelEntity(
    string Id,
    string ProjectId,
    string ProjectName,
    string Name,
    string FullName,
    string Namespace,
    string TypeKind,
    string Accessibility,
    bool IsAbstract,
    bool IsSealed,
    bool IsStatic,
    DataModelSourceLocation? SourceLocation,
    string SelectionReason,
    string OrderKey,
    IReadOnlyList<DataModelProperty> Properties);

public sealed record DataModelRelationship(
    string Id,
    string SourceEntityId,
    string TargetEntityId,
    string SourcePropertyId,
    DataModelRelationshipKind Kind,
    bool IsCollection,
    bool IsNullable,
    string Evidence,
    string OrderKey);

public sealed record DataModelDiagnostic(string Code, string Message, string? SemanticId = null);

public sealed record DataModelDiagram(
    IReadOnlyList<DataModelEntity> Entities,
    IReadOnlyList<DataModelRelationship> Relationships,
    IReadOnlyList<DataModelDiagnostic> Diagnostics);
