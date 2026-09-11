using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public static class DataModelDiagramSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Export(DataModelDiagram diagram)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        return JsonSerializer.Serialize(Normalize(diagram), Options);
    }

    public static DataModelDiagram Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Data Model JSON is empty.");
        return Normalize(JsonSerializer.Deserialize<DataModelDiagram>(json, Options)
            ?? throw new InvalidDataException("Data Model JSON did not contain a diagram object."));
    }

    private static DataModelDiagram Normalize(DataModelDiagram diagram) => new(
        (diagram.Entities ?? Array.Empty<DataModelEntity>())
            .Select(entity => entity with
            {
                Properties = (entity.Properties ?? Array.Empty<DataModelProperty>())
                    .OrderBy(property => property.OrderKey, StringComparer.Ordinal).ToArray()
            })
            .OrderBy(entity => entity.OrderKey, StringComparer.Ordinal).ToArray(),
        (diagram.Relationships ?? Array.Empty<DataModelRelationship>())
            .OrderBy(relationship => relationship.OrderKey, StringComparer.Ordinal).ToArray(),
        (diagram.Diagnostics ?? Array.Empty<DataModelDiagnostic>())
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.SemanticId, StringComparer.Ordinal).ToArray());
}
