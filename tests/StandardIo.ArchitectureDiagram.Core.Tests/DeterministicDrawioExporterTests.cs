using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Renderers;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DeterministicDrawioExporterTests
{
    [Fact]
    public void Render_aligns_nodes_by_dependency_depth()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_service", "project_api", "Service")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_job_service", "type_job", "type_service", "internal")
            }));

        Assert.Equal(AbsoluteY(document, "type_controller"), AbsoluteY(document, "type_job"));
        Assert.True(AbsoluteY(document, "type_service") > AbsoluteY(document, "type_controller"));
    }

    [Fact]
    public void Render_reuses_shared_dependency_node()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_service", "project_api", "Service")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_job_service", "type_job", "type_service", "internal")
            }));

        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "type_service");
        Assert.Equal(2, document.Descendants("mxCell").Count(cell => (string?)cell.Attribute("target") == "type_service"));
    }

    [Fact]
    public void Render_keeps_cycles_finite()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_a", "project_api", "A"),
                    Node("type_b", "project_api", "B")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_a_b", "type_a", "type_b", "internal"),
                new DependencyEdge("edge_b_a", "type_b", "type_a", "internal")
            }));

        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "type_a");
        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "type_b");
        Assert.Contains(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "edge_a_b");
        Assert.Contains(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "edge_b_a");
    }

    [Fact]
    public void Render_routes_links_from_bottom_to_top_with_configurable_ports()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.EdgePortSpacing = 18;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_cache", "project_api", "Cache"),
                    Node("type_repository", "project_api", "Repository")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_cache", "type_controller", "type_cache", "internal"),
                new DependencyEdge("edge_repository", "type_controller", "type_repository", "internal")
            }),
            settings);
        var cache = Cell(document, "edge_cache");
        var repository = Cell(document, "edge_repository");

        Assert.Equal("1", StyleValue(cache, "exitY"));
        Assert.Equal("0", StyleValue(cache, "entryY"));
        Assert.NotEqual(StyleValue(cache, "exitX"), StyleValue(repository, "exitX"));
    }

    [Fact]
    public void Render_separates_overlapping_corners()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ParallelLaneSpacing = 20;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_a", "project_api", "A"),
                    Node("type_b", "project_api", "B"),
                    Node("type_c", "project_api", "C")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_a_c", "type_a", "type_c", "internal"),
                new DependencyEdge("edge_b_c", "type_b", "type_c", "internal")
            }),
            settings);
        var corners = document.Descendants("mxPoint")
            .Select(point => $"{(string?)point.Attribute("x")}:{(string?)point.Attribute("y")}")
            .ToArray();

        Assert.Equal(corners.Length, corners.Distinct().Count());
    }

    [Fact]
    public void Render_places_standalone_nodes_away_from_main_tree()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.StandaloneGroupSpacing = 300;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_service", "project_api", "Service"),
                    Node("type_standalone", "project_api", "Standalone")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal") }),
            settings);

        Assert.True(AbsoluteX(document, "type_standalone") - AbsoluteX(document, "type_controller") >= settings.Layout.StandaloneGroupSpacing);
    }

    [Fact]
    public void Render_labels_external_boundary_nodes_with_configured_tag()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.ExternalDependencyTag = "[Outside]";
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller")
                })
            },
            new[] { new ExternalDependencyNode("external_domain", "DomainService", "Domain", "external-guid", "Domain.DomainService", "[Outside]") },
            new[] { new DependencyEdge("edge_external", "type_controller", "external_domain", "external") }),
            settings);
        var value = (string?)Cell(document, "external_domain").Attribute("value");

        Assert.Contains("[Outside]", value);
        Assert.Contains("Domain.DomainService", value);
    }

    [Fact]
    public void Render_keeps_project_container_as_border_parent()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>()));

        Assert.Equal("1", (string?)Cell(document, "project_api").Attribute("vertex"));
        Assert.Equal("project_api", (string?)Cell(document, "type_controller").Attribute("parent"));
    }

    private static XDocument Render(DiagramModel diagram, DiagramSettings? settings = null)
    {
        return XDocument.Parse(new DrawioDiagramRenderer().Render(diagram, settings ?? DiagramSettings.CreateDefault()));
    }

    private static TypeNode Node(string id, string projectId, string name)
    {
        return new TypeNode(id, projectId, name, $"Api.{name}", "Class");
    }

    private static XElement Cell(XDocument document, string id)
    {
        return document.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == id);
    }

    private static int AbsoluteX(XDocument document, string id)
    {
        var cell = Cell(document, id);
        var x = Geometry(document, id, "x");
        var parentId = (string?)cell.Attribute("parent");
        return string.IsNullOrWhiteSpace(parentId) || parentId == "1"
            ? x
            : x + Geometry(document, parentId, "x");
    }

    private static int AbsoluteY(XDocument document, string id)
    {
        var cell = Cell(document, id);
        var y = Geometry(document, id, "y");
        var parentId = (string?)cell.Attribute("parent");
        return string.IsNullOrWhiteSpace(parentId) || parentId == "1"
            ? y
            : y + Geometry(document, parentId, "y");
    }

    private static int Geometry(XDocument document, string id, string attributeName)
    {
        return int.Parse((string)Cell(document, id).Element("mxGeometry")!.Attribute(attributeName)!);
    }

    private static string StyleValue(XElement edge, string key)
    {
        var prefix = key + "=";
        return ((string)edge.Attribute("style")!)
            .Split(';')
            .Single(part => part.StartsWith(prefix, StringComparison.Ordinal))
            .Substring(prefix.Length);
    }
}
