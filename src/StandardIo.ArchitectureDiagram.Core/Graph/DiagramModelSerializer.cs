using System;
using System.IO;
using System.Text.Json;

namespace StandardIo.ArchitectureDiagram.Core.Graph;

public static class DiagramModelSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Export(DiagramModel diagram)
    {
        if (diagram is null)
        {
            throw new ArgumentNullException(nameof(diagram));
        }

        return JsonSerializer.Serialize(Normalize(diagram), Options);
    }

    public static DiagramModel Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Diagram JSON is empty.");
        }

        var diagram = JsonSerializer.Deserialize<DiagramModel>(json, Options)
            ?? throw new InvalidDataException("Diagram JSON did not contain a diagram object.");

        return Normalize(diagram);
    }

    private static DiagramModel Normalize(DiagramModel diagram)
    {
        return diagram with
        {
            Projects = diagram.Projects ?? Array.Empty<ProjectContainer>(),
            ExternalDependencies = diagram.ExternalDependencies ?? Array.Empty<ExternalDependencyNode>(),
            Edges = diagram.Edges ?? Array.Empty<DependencyEdge>(),
            Metadata = diagram.Metadata ?? new DiagramMetadata()
        };
    }
}
