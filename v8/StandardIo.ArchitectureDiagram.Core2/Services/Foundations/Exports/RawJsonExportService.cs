// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Exports;
internal sealed class RawJsonExportService : IRawJsonExportService
{
    private static readonly JsonSerializerOptions serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public byte[] Export(DiagramTypes diagramType, ProjectModel[] projects)
    {
        ArgumentNullException.ThrowIfNull(argument: projects);

        var export = new
        {
            SchemaVersion = 1,
            DiagramType = diagramType.ToString(),
            Projects = projects
                .OrderBy(keySelector: project => project.Path, comparer: StringComparer.Ordinal)
                .ThenBy(keySelector: project => project.Name, comparer: StringComparer.Ordinal)
                .Select(selector: project => new
                {
                    project.Name,
                    project.Path,
                    Types = (project.Types ?? Array.Empty<DefinedType>())
                        .OrderBy(keySelector: type => type.Name, comparer: StringComparer.Ordinal)
                        .Select(selector: type => new
                        {
                            type.Name,
                            type.AssemblyName,
                            type.IsDataType,
                            type.BaseTypeName,
                            InterfaceNames = (type.InterfaceNames ?? Array.Empty<string>())
                                .OrderBy(keySelector: name => name, comparer: StringComparer.Ordinal),
                            FrameworkType = type.FrameworkType.ToString(),
                            type.IsInternal,
                            Fields = (type.Fields ?? Array.Empty<Field>())
                                .OrderBy(keySelector: field => field.Name, comparer: StringComparer.Ordinal)
                                .ThenBy(keySelector: field => field.Type, comparer: StringComparer.Ordinal),
                            Properties = (type.Properties ?? Array.Empty<Property>())
                                .OrderBy(keySelector: property => property.Name, comparer: StringComparer.Ordinal)
                                .ThenBy(keySelector: property => property.Type, comparer: StringComparer.Ordinal),
                            Methods = (type.Methods ?? Array.Empty<Method>())
                                .OrderBy(keySelector: method => method.Name, comparer: StringComparer.Ordinal),
                        }),
                    Edges = (project.Dependencies ?? Array.Empty<TypeRelationship>())
                        .OrderBy(keySelector: edge => edge.FromType, comparer: StringComparer.Ordinal)
                        .ThenBy(keySelector: edge => edge.ToType, comparer: StringComparer.Ordinal)
                        .ThenBy(keySelector: edge => edge.DependencyType)
                        .ThenBy(keySelector: edge => edge.FromMethod, comparer: StringComparer.Ordinal)
                        .ThenBy(keySelector: edge => edge.ToMethod, comparer: StringComparer.Ordinal)
                        .Select(selector: edge => ExportEdge(project: project, edge: edge)),
                }),
        };

        return JsonSerializer.SerializeToUtf8Bytes(value: export, options: serializerOptions);
    }

    private static object ExportEdge(ProjectModel project, TypeRelationship edge)
    {
        DefinedType? target = (project.Types ?? Array.Empty<DefinedType>())
            .FirstOrDefault(predicate: type => string.Equals(a: type.Name, b: edge.ToType, comparisonType: StringComparison.Ordinal));

        return new
        {
            Kind = edge.DependencyType.ToString(),
            edge.FromType,
            edge.ToType,
            Evidence = new
            {
                edge.FromMethod,
                edge.ToMethod,
            },
            Target = new
            {
                TargetScope = target is null ? "Unknown" : target.IsInternal ? "Source" : "External",
                target?.AssemblyName,
            },
        };
    }
}
