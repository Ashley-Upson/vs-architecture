using System;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Renderers;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class JsonDiagramRendererTests
{
    [Fact]
    public void Render_emits_valid_diagram_model_json()
    {
        var renderer = new JsonDiagramRenderer();

        using var document = JsonDocument.Parse(renderer.Render(CreateDiagram(), DiagramSettings.CreateDefault()));

        Assert.Equal("project_a", document.RootElement.GetProperty("projects")[0].GetProperty("id").GetString());
        Assert.Equal("type_controller", document.RootElement.GetProperty("projects")[0].GetProperty("types")[0].GetProperty("id").GetString());
        Assert.Equal("external_sql", document.RootElement.GetProperty("externalDependencies")[0].GetProperty("id").GetString());
        Assert.Equal("edge_controller_sql", document.RootElement.GetProperty("edges")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void Render_does_not_emit_drawio_xml_or_visual_cells()
    {
        var output = new JsonDiagramRenderer().Render(CreateDiagram(), DiagramSettings.CreateDefault());

        Assert.DoesNotContain("mxCell", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mxGraphModel", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"edges\"", output);
    }

    [Fact]
    public void Render_handles_duplicate_node_ids_as_exported_data()
    {
        var diagram = new DiagramModel(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_duplicate", "project_a", "First", "App.First", "Class"),
                    new TypeNode("type_duplicate", "project_a", "Second", "App.Second", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>(),
            new DiagramMetadata());

        var imported = DiagramModelSerializer.Import(new JsonDiagramRenderer().Render(diagram, DiagramSettings.CreateDefault()));

        Assert.Equal(2, imported.Projects.Single().Types.Count(type => type.Id == "type_duplicate"));
    }

    [Fact]
    public void Render_handles_edges_that_reference_missing_nodes_as_exported_data()
    {
        var diagram = new DiagramModel(
            Array.Empty<ProjectContainer>(),
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_missing", "missing_source", "missing_target", "internal") },
            new DiagramMetadata());

        var imported = DiagramModelSerializer.Import(new JsonDiagramRenderer().Render(diagram, DiagramSettings.CreateDefault()));

        Assert.Equal("missing_source", imported.Edges.Single().SourceId);
        Assert.Equal("missing_target", imported.Edges.Single().TargetId);
    }

    [Fact]
    public void Drawio_renderer_handles_empty_model()
    {
        var diagram = new DiagramModel(
            Array.Empty<ProjectContainer>(),
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>(),
            new DiagramMetadata());

        var output = new DrawioDiagramRenderer().Render(diagram, DiagramSettings.CreateDefault());

        Assert.Contains("mxGraphModel", output);
    }

    private static DiagramModel CreateDiagram()
    {
        return new DiagramModel(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class")
                })
            },
            new[] { new ExternalDependencyNode("external_sql", "SqlClient", "SqlClient") },
            new[] { new DependencyEdge("edge_controller_sql", "type_controller", "external_sql", "external") },
            new DiagramMetadata());
    }
}
