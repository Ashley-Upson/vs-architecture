using StandardIo.ArchitectureDiagram.Core.Drawio;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;
using System.Xml.Linq;
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

    [Fact]
    public void Export_lays_out_dependencies_below_parents()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class"),
                    new TypeNode("type_service", "project_a", "PaymentService", "App.PaymentService", "Class"),
                    new TypeNode("type_repository", "project_a", "PaymentRepository", "App.PaymentRepository", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_2", "type_service", "type_repository", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));

        Assert.True(Y(document, "type_controller") < Y(document, "type_service"));
        Assert.True(Y(document, "type_service") < Y(document, "type_repository"));
    }

    [Fact]
    public void Export_aligns_top_level_nodes_horizontally()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class"),
                    new TypeNode("type_job", "project_a", "NightlyJob", "App.NightlyJob", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>());

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));

        Assert.Equal(Y(document, "type_controller"), Y(document, "type_job"));
        Assert.NotEqual(X(document, "type_controller"), X(document, "type_job"));
    }

    [Fact]
    public void Export_uses_consistent_spacing_for_adjacent_nodes_in_same_layer()
    {
        var settings = DiagramSettings.CreateDefault();
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class"),
                    new TypeNode("type_cache", "project_a", "Cache", "App.Cache", "Class"),
                    new TypeNode("type_repository", "project_a", "Repository", "App.Repository", "Class"),
                    new TypeNode("type_service", "project_a", "Service", "App.Service", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_cache", "internal"),
                new DependencyEdge("edge_2", "type_controller", "type_repository", "internal"),
                new DependencyEdge("edge_3", "type_controller", "type_service", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, settings));
        var layerNodes = new[] { "type_cache", "type_repository", "type_service" }
            .OrderBy(id => X(document, id))
            .ToArray();
        var expectedDelta = settings.Layout.NodeWidth + settings.Layout.HorizontalSpacing;

        Assert.Equal(Y(document, layerNodes[0]), Y(document, layerNodes[1]));
        Assert.Equal(Y(document, layerNodes[1]), Y(document, layerNodes[2]));
        Assert.Equal(expectedDelta, X(document, layerNodes[1]) - X(document, layerNodes[0]));
        Assert.Equal(expectedDelta, X(document, layerNodes[2]) - X(document, layerNodes[1]));
    }

    [Fact]
    public void Export_reuses_shared_dependency_nodes()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_controller", "project_a", "HomeController", "App.HomeController", "Class"),
                    new TypeNode("type_job", "project_a", "NightlyJob", "App.NightlyJob", "Class"),
                    new TypeNode("type_service", "project_a", "PaymentService", "App.PaymentService", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_2", "type_job", "type_service", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));
        var serviceVertices = document.Descendants("mxCell")
            .Where(e => (string?)e.Attribute("id") == "type_service" && (string?)e.Attribute("vertex") == "1")
            .ToList();
        var incomingEdges = document.Descendants("mxCell")
            .Where(e => (string?)e.Attribute("target") == "type_service")
            .ToList();

        Assert.Single(serviceVertices);
        Assert.Equal(2, incomingEdges.Count);
    }

    [Fact]
    public void Export_handles_dense_shared_dependency_graph_without_overflow()
    {
        var types = new List<TypeNode>
        {
            new("type_root", "project_a", "Root", "App.Root", "Class")
        };
        var edges = new List<DependencyEdge>();

        for (var layer = 0; layer < 32; layer++)
        {
            types.Add(new TypeNode($"type_{layer}_a", "project_a", $"Layer{layer}A", $"App.Layer{layer}A", "Class"));
            types.Add(new TypeNode($"type_{layer}_b", "project_a", $"Layer{layer}B", $"App.Layer{layer}B", "Class"));
        }

        edges.Add(new DependencyEdge("edge_root_a", "type_root", "type_0_a", "internal"));
        edges.Add(new DependencyEdge("edge_root_b", "type_root", "type_0_b", "internal"));

        for (var layer = 0; layer < 31; layer++)
        {
            foreach (var suffix in new[] { "a", "b" })
            {
                edges.Add(new DependencyEdge($"edge_{layer}_{suffix}_next_a", $"type_{layer}_{suffix}", $"type_{layer + 1}_a", "internal"));
                edges.Add(new DependencyEdge($"edge_{layer}_{suffix}_next_b", $"type_{layer}_{suffix}", $"type_{layer + 1}_b", "internal"));
            }
        }

        var graph = new ArchitectureGraph(
            new[] { new ProjectContainer("project_a", "App", types) },
            Array.Empty<ExternalDependencyNode>(),
            edges);

        var xml = new DrawioExporter().Export(graph, DiagramSettings.CreateDefault());

        Assert.Contains("type_root", xml);
        Assert.Contains("type_31_b", xml);
    }

    private static int X(XDocument document, string id)
    {
        return Geometry(document, id, "x");
    }

    private static int Y(XDocument document, string id)
    {
        return Geometry(document, id, "y");
    }

    private static int Geometry(XDocument document, string id, string attributeName)
    {
        var cell = document.Descendants("mxCell").Single(e => (string?)e.Attribute("id") == id);
        var geometry = cell.Element("mxGeometry")!;
        return int.Parse((string)geometry.Attribute(attributeName)!);
    }
}
