using System;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramModelSerializerTests
{
    [Fact]
    public void Export_uses_camel_case_json_shape()
    {
        var diagram = CreateDiagram();

        using var document = JsonDocument.Parse(DiagramModelSerializer.Export(diagram));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("projects", out _));
        Assert.True(root.TryGetProperty("externalDependencies", out _));
        Assert.True(root.TryGetProperty("edges", out _));
        Assert.True(root.TryGetProperty("metadata", out _));
    }

    [Fact]
    public void Import_round_trips_stable_ids_projects_nodes_edges_and_externals()
    {
        var diagram = CreateDiagram();

        var imported = DiagramModelSerializer.Import(DiagramModelSerializer.Export(diagram));

        Assert.Equal("project_a", imported.Projects.Single().Id);
        Assert.Equal("project-guid", imported.Projects.Single().UniqueId);
        Assert.Equal("type_controller", imported.Projects.Single().Types.First().Id);
        Assert.Equal("type-guid", imported.Projects.Single().Types.First().UniqueId);
        Assert.Equal("external_sql", imported.ExternalDependencies.Single().Id);
        Assert.Equal("external-guid", imported.ExternalDependencies.Single().UniqueId);
        Assert.Equal("[External]", imported.ExternalDependencies.Single().Tag);
        Assert.Equal("edge_controller_sql", imported.Edges.Single().Id);
        Assert.Equal(1, imported.Metadata?.SchemaVersion);
    }

    [Fact]
    public void Empty_collections_export_and_import_as_empty_arrays()
    {
        var diagram = new DiagramModel(
            Array.Empty<ProjectContainer>(),
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>(),
            new DiagramMetadata());

        var imported = DiagramModelSerializer.Import(DiagramModelSerializer.Export(diagram));

        Assert.Empty(imported.Projects);
        Assert.Empty(imported.ExternalDependencies);
        Assert.Empty(imported.Edges);
    }

    [Fact]
    public void Export_rejects_null_diagram()
    {
        Assert.Throws<ArgumentNullException>(() => DiagramModelSerializer.Export(null!));
    }

    [Fact]
    public void Import_rejects_empty_json()
    {
        Assert.Throws<System.IO.InvalidDataException>(() => DiagramModelSerializer.Import(""));
    }

    private static DiagramModel CreateDiagram()
    {
        return new DiagramModel(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class", "type-guid")
                }, "project-guid")
            },
            new[] { new ExternalDependencyNode("external_sql", "SqlClient", "SqlClient", "external-guid", "System.Data.SqlClient.SqlConnection", "[External]") },
            new[] { new DependencyEdge("edge_controller_sql", "type_controller", "external_sql", "external") },
            new DiagramMetadata());
    }
}
