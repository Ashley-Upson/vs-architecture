using StandardIo.ArchitectureDiagram.Core.Drawio;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioExporterTests
{
    [Fact]
    public void Export_contains_project_container_child_nodes_external_nodes_and_edges()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class"),
                    new TypeNode("type_service", "project_a", "PaymentService", "App.PaymentService", "Class")
                })
            },
            new[] { new ExternalDependencyNode("external_sql", "SqlClient", "SqlClient") },
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_2", "type_service", "external_sql", "external")
            });

        var xml = new DrawioExporter().Export(graph, DiagramSettings.CreateDefault());

        Assert.Contains("project_a", xml);
        Assert.Contains("parent=\"project_a\"", xml);
        Assert.Contains("external_sql", xml);
        Assert.Contains("source=\"type_controller\"", xml);
        Assert.Contains("target=\"type_service\"", xml);
    }

    [Fact]
    public void Export_emits_special_shape_styles()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Overrides.Add(new StyleOverride
        {
            FullName = "App.EventHub",
            Style = new NodeStyle { Shape = "rhombus", FillColor = "#f36c21", StrokeColor = "#a43b08", FontColor = "#111111" }
        });
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_hub", "project_a", "EventHub", "App.EventHub", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>());

        var xml = new DrawioExporter().Export(graph, settings);

        Assert.Contains("shape=rhombus", xml);
    }
}
