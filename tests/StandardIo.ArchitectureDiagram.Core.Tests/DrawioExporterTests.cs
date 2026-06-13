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
    public void Export_centres_parent_above_child_span()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_parent", "project_a", "Parent", "App.Parent", "Class"),
                    new TypeNode("type_left", "project_a", "LeftChild", "App.LeftChild", "Class"),
                    new TypeNode("type_right", "project_a", "RightChild", "App.RightChild", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_parent", "type_left", "internal"),
                new DependencyEdge("edge_2", "type_parent", "type_right", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));
        var parentCenter = CenterX(document, "type_parent");
        var childSpanCenter = (CenterX(document, "type_left") + CenterX(document, "type_right")) / 2;

        Assert.Equal(childSpanCenter, parentCenter);
    }

    [Fact]
    public void Export_keeps_discovery_order_and_places_dependencies_below_parent()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_z_controller", "project_a", "ZController", "App.ZController", "Class"),
                    new TypeNode("type_b_cache", "project_a", "BCache", "App.BCache", "Class"),
                    new TypeNode("type_a_repository", "project_a", "ARepository", "App.ARepository", "Class"),
                    new TypeNode("type_worker", "project_a", "Worker", "App.Worker", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_z_controller", "type_b_cache", "internal"),
                new DependencyEdge("edge_2", "type_z_controller", "type_a_repository", "internal"),
                new DependencyEdge("edge_3", "type_b_cache", "type_worker", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));

        Assert.True(Y(document, "type_b_cache") > Y(document, "type_z_controller"));
        Assert.Equal(Y(document, "type_b_cache"), Y(document, "type_a_repository"));
        Assert.True(Y(document, "type_worker") > Y(document, "type_b_cache"));
        Assert.True(X(document, "type_b_cache") < X(document, "type_a_repository"));
    }

    [Fact]
    public void Export_prefers_coordination_roots_over_lower_layer_selected_roots()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_a", "App", new[]
                {
                    new TypeNode("type_coordination", "project_a", "PaymentCoordinationService", "App.Services.Coordination.PaymentCoordinationService", "Class"),
                    new TypeNode("type_orchestration", "project_a", "PaymentOrchestrationService", "App.Services.Orchestration.PaymentOrchestrationService", "Class"),
                    new TypeNode("type_foundation", "project_a", "PaymentFoundationService", "App.Services.Foundation.PaymentFoundationService", "Class"),
                    new TypeNode("type_broker", "project_a", "PaymentBroker", "App.Services.Broker.PaymentBroker", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_coordination", "type_orchestration", "internal"),
                new DependencyEdge("edge_2", "type_foundation", "type_broker", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));

        Assert.True(Y(document, "type_coordination") < Y(document, "type_orchestration"));
        Assert.True(Y(document, "type_coordination") < Y(document, "type_foundation"));
    }

    [Fact]
    public void Export_starts_dependent_project_nodes_below_the_depending_layer()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_selected", "Api", new[]
                {
                    new TypeNode("type_controller", "project_selected", "Controller", "Api.Controller", "Class"),
                    new TypeNode("type_orchestration", "project_selected", "Orchestration", "Api.Orchestration", "Class"),
                    new TypeNode("type_processing", "project_selected", "Processing", "Api.Processing", "Class"),
                    new TypeNode("type_worker", "project_selected", "Worker", "Api.Worker", "Class")
                }),
                new ProjectContainer("project_dependency", "Domain", new[]
                {
                    new TypeNode("type_domain_service", "project_dependency", "DomainService", "Domain.DomainService", "Class"),
                    new TypeNode("type_domain_helper", "project_dependency", "DomainHelper", "Domain.DomainHelper", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_orchestration", "internal"),
                new DependencyEdge("edge_2", "type_orchestration", "type_processing", "internal"),
                new DependencyEdge("edge_3", "type_processing", "type_worker", "internal"),
                new DependencyEdge("edge_4", "type_worker", "type_domain_service", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));

        Assert.True(AbsoluteY(document, "type_domain_service") > AbsoluteY(document, "type_worker"));
        Assert.Equal(AbsoluteY(document, "type_domain_service"), AbsoluteY(document, "type_domain_helper"));
        Assert.True(AbsoluteY(document, "type_domain_helper") > AbsoluteY(document, "type_controller"));
    }

    [Fact]
    public void Export_keeps_project_containers_horizontally_separated()
    {
        var settings = DiagramSettings.CreateDefault();
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    new TypeNode("type_controller", "project_api", "Controller", "Api.Controller", "Class"),
                    new TypeNode("type_worker", "project_api", "Worker", "Api.Worker", "Class")
                }),
                new ProjectContainer("project_domain", "Domain", new[]
                {
                    new TypeNode("type_domain_service", "project_domain", "DomainService", "Domain.DomainService", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_worker", "internal"),
                new DependencyEdge("edge_2", "type_worker", "type_domain_service", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, settings));
        var api = Rect(document, "project_api");
        var domain = Rect(document, "project_domain");

        Assert.False(Overlaps(api, domain));
    }

    [Fact]
    public void Export_prevents_project_container_overlap_across_all_rows()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    new TypeNode("type_controller", "project_api", "Controller", "Api.Controller", "Class"),
                    new TypeNode("type_orchestration", "project_api", "Orchestration", "Api.Orchestration", "Class"),
                    new TypeNode("type_processor", "project_api", "Processor", "Api.Processor", "Class"),
                    new TypeNode("type_worker", "project_api", "Worker", "Api.Worker", "Class")
                }),
                new ProjectContainer("project_domain", "Domain", new[]
                {
                    new TypeNode("type_domain_service", "project_domain", "DomainService", "Domain.DomainService", "Class")
                }),
                new ProjectContainer("project_data", "Data", new[]
                {
                    new TypeNode("type_repository", "project_data", "Repository", "Data.Repository", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_orchestration", "internal"),
                new DependencyEdge("edge_2", "type_orchestration", "type_processor", "internal"),
                new DependencyEdge("edge_3", "type_processor", "type_worker", "internal"),
                new DependencyEdge("edge_4", "type_controller", "type_domain_service", "internal"),
                new DependencyEdge("edge_5", "type_worker", "type_repository", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));
        var projects = new[] { "project_api", "project_domain", "project_data" }
            .Select(id => Rect(document, id))
            .ToArray();

        for (var i = 0; i < projects.Length; i++)
        {
            for (var j = i + 1; j < projects.Length; j++)
            {
                Assert.False(Overlaps(projects[i], projects[j]));
            }
        }
    }

    [Fact]
    public void Export_keeps_external_dependency_diamonds_outside_project_containers()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    new TypeNode("type_controller", "project_api", "Controller", "Api.Controller", "Class"),
                    new TypeNode("type_service", "project_api", "Service", "Api.Service", "Class"),
                    new TypeNode("type_worker", "project_api", "Worker", "Api.Worker", "Class")
                })
            },
            new[]
            {
                new ExternalDependencyNode("external_logging", "Logging", "Logging"),
                new ExternalDependencyNode("external_queue", "Queue", "Queue")
            },
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_2", "type_service", "type_worker", "internal"),
                new DependencyEdge("edge_3", "type_service", "external_logging", "external"),
                new DependencyEdge("edge_4", "type_service", "external_queue", "external")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));
        var project = Rect(document, "project_api");
        var logging = Rect(document, "external_logging");
        var queue = Rect(document, "external_queue");

        Assert.False(Overlaps(project, logging));
        Assert.False(Overlaps(project, queue));
        Assert.False(Overlaps(logging, queue));
    }

    [Fact]
    public void Export_routes_edges_with_explicit_top_and_bottom_connection_points()
    {
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    new TypeNode("type_controller", "project_api", "Controller", "Api.Controller", "Class"),
                    new TypeNode("type_service", "project_api", "Service", "Api.Service", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_controller", "type_service", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, DiagramSettings.CreateDefault()));
        var edge = document.Descendants("mxCell").Single(e => (string?)e.Attribute("id") == "edge_1");
        var style = (string)edge.Attribute("style")!;
        var points = edge.Element("mxGeometry")!.Element("Array")!.Elements("mxPoint").ToArray();

        Assert.Contains("exitX=0.5;exitY=1", style);
        Assert.Contains("entryX=0.5;entryY=0", style);
        Assert.True(points.Length >= 2);
    }

    [Fact]
    public void Export_keeps_cycles_on_bounded_vertical_layers()
    {
        var settings = DiagramSettings.CreateDefault();
        var graph = new ArchitectureGraph(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    new TypeNode("type_a", "project_api", "A", "Api.A", "Class"),
                    new TypeNode("type_b", "project_api", "B", "Api.B", "Class"),
                    new TypeNode("type_c", "project_api", "C", "Api.C", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_1", "type_a", "type_b", "internal"),
                new DependencyEdge("edge_2", "type_b", "type_c", "internal"),
                new DependencyEdge("edge_3", "type_c", "type_a", "internal")
            });

        var document = XDocument.Parse(new DrawioExporter().Export(graph, settings));
        var maxY = new[] { "type_a", "type_b", "type_c" }.Max(id => AbsoluteY(document, id));
        var minY = new[] { "type_a", "type_b", "type_c" }.Min(id => AbsoluteY(document, id));

        Assert.True(maxY - minY <= settings.Layout.NodeHeight + settings.Layout.VerticalSpacing);
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

    private static int AbsoluteY(XDocument document, string id)
    {
        var cell = document.Descendants("mxCell").Single(e => (string?)e.Attribute("id") == id);
        var y = Geometry(document, id, "y");
        var parentId = (string?)cell.Attribute("parent");
        if (string.IsNullOrWhiteSpace(parentId) || parentId == "1")
        {
            return y;
        }

        return y + Geometry(document, parentId, "y");
    }

    private static int CenterX(XDocument document, string id)
    {
        return X(document, id) + Geometry(document, id, "width") / 2;
    }

    private static bool Overlaps(RectInfo left, RectInfo right)
    {
        return left.X < right.X + right.Width &&
            left.X + left.Width > right.X &&
            left.Y < right.Y + right.Height &&
            left.Y + left.Height > right.Y;
    }

    private static RectInfo Rect(XDocument document, string id)
    {
        return new RectInfo(
            Geometry(document, id, "x"),
            Geometry(document, id, "y"),
            Geometry(document, id, "width"),
            Geometry(document, id, "height"));
    }

    private static int Geometry(XDocument document, string id, string attributeName)
    {
        var cell = document.Descendants("mxCell").Single(e => (string?)e.Attribute("id") == id);
        var geometry = cell.Element("mxGeometry")!;
        return int.Parse((string)geometry.Attribute(attributeName)!);
    }

    private readonly record struct RectInfo(int X, int Y, int Width, int Height);
}
