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
    public void Render_layers_nodes_by_distance_from_exit_layer()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_processing", "project_api", "Processing"),
                    Node("type_service", "project_api", "Service"),
                    Node("type_job", "project_api", "Job")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_processing", "type_controller", "type_processing", "internal"),
                new DependencyEdge("edge_processing_service", "type_processing", "type_service", "internal"),
                new DependencyEdge("edge_job_service", "type_job", "type_service", "internal")
            }));

        Assert.True(AbsoluteY(document, "type_controller") < AbsoluteY(document, "type_processing"));
        Assert.True(AbsoluteY(document, "type_service") > AbsoluteY(document, "type_processing"));
    }

    [Fact]
    public void Render_only_forces_topmost_depth_to_same_horizontal_layer()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_busy", "project_api", "BusyProcessing"),
                    Node("type_quiet", "project_api", "QuietProcessing"),
                    Node("type_leaf_a", "project_api", "LeafA"),
                    Node("type_leaf_b", "project_api", "LeafB"),
                    Node("type_leaf_c", "project_api", "LeafC"),
                    Node("type_leaf_d", "project_api", "LeafD")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_busy", "type_controller", "type_busy", "internal"),
                new DependencyEdge("edge_job_quiet", "type_job", "type_quiet", "internal"),
                new DependencyEdge("edge_busy_leaf_a", "type_busy", "type_leaf_a", "internal"),
                new DependencyEdge("edge_busy_leaf_b", "type_busy", "type_leaf_b", "internal"),
                new DependencyEdge("edge_busy_leaf_c", "type_busy", "type_leaf_c", "internal"),
                new DependencyEdge("edge_quiet_leaf_d", "type_quiet", "type_leaf_d", "internal")
            }));

        Assert.Equal(AbsoluteY(document, "type_controller"), AbsoluteY(document, "type_job"));
        Assert.NotEqual(AbsoluteY(document, "type_busy"), AbsoluteY(document, "type_quiet"));
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
        AssertParallelSegmentsSeparated(
            EdgePoints(document, "edge_cache"),
            EdgePoints(document, "edge_repository"),
            settings.Layout.ParallelLaneSpacing);
    }

    [Fact]
    public void Render_uses_local_lane_offsets_instead_of_global_edge_order()
    {
        var unrelatedNodes = Enumerable.Range(0, 8)
            .SelectMany(index => new[]
            {
                Node($"type_unrelated_source_{index}", "project_api", $"UnrelatedSource{index}"),
                Node($"type_unrelated_target_{index}", "project_api", $"UnrelatedTarget{index}")
            })
            .ToArray();
        var unrelatedEdges = Enumerable.Range(0, 8)
            .Select(index => new DependencyEdge(
                $"edge_unrelated_{index}",
                $"type_unrelated_source_{index}",
                $"type_unrelated_target_{index}",
                "internal"))
            .ToArray();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_top", "project_api", "Top"),
                    Node("type_middle", "project_api", "Middle"),
                    Node("type_bottom", "project_api", "Bottom")
                }.Concat(unrelatedNodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            unrelatedEdges.Concat(new[]
            {
                new DependencyEdge("edge_top_middle", "type_top", "type_middle", "internal"),
                new DependencyEdge("edge_middle_bottom", "type_middle", "type_bottom", "internal"),
                new DependencyEdge("edge_top_bottom", "type_top", "type_bottom", "internal")
            }).ToArray()));

        var laneY = EdgePoints(document, "edge_top_bottom")[1].Y;

        Assert.True(laneY < AbsoluteY(document, "type_bottom") - 10);
    }

    [Fact]
    public void Render_keeps_nearby_dependency_routes_local()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_processing", "project_api", "TemplateRenderProcessingService"),
                    Node("type_far_source", "project_api", "FarSource"),
                    Node("type_far_target", "project_api", "FarTarget")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_template", "type_orchestration", "type_processing", "internal"),
                new DependencyEdge("edge_far", "type_far_source", "type_far_target", "internal")
            }));

        var localRight = Math.Max(AbsoluteRight(document, "type_orchestration"), AbsoluteRight(document, "type_processing"));
        var farLeft = AbsoluteX(document, "type_far_source");
        var routeRight = EdgePoints(document, "edge_template").Max(point => point.X);

        Assert.True(routeRight < farLeft);
        Assert.True(routeRight <= localRight + DiagramSettings.CreateDefault().Layout.HorizontalSpacing);
    }

    [Fact]
    public void Render_does_not_let_unrelated_link_density_expand_simple_vertical_gaps()
    {
        var unrelatedNodes = Enumerable.Range(0, 10)
            .SelectMany(index => new[]
            {
                Node($"type_unrelated_source_{index}", "project_api", $"UnrelatedSource{index}"),
                Node($"type_unrelated_target_{index}", "project_api", $"UnrelatedTarget{index}")
            })
            .ToArray();
        var unrelatedEdges = Enumerable.Range(0, 10)
            .Select(index => new DependencyEdge(
                $"edge_unrelated_{index}",
                $"type_unrelated_source_{index}",
                $"type_unrelated_target_{index}",
                "internal"))
            .ToArray();
        var settings = DiagramSettings.CreateDefault();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_processing", "project_api", "TemplateRenderProcessingService")
                }.Concat(unrelatedNodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_template", "type_orchestration", "type_processing", "internal")
            }.Concat(unrelatedEdges).ToArray()),
            settings);

        var verticalGap = AbsoluteY(document, "type_processing") - AbsoluteBottom(document, "type_orchestration");

        Assert.True(verticalGap <= settings.Layout.VerticalSpacing + settings.Layout.ParallelLaneSpacing);
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
    public void Render_places_standalone_nodes_in_square_root_grid()
    {
        var standaloneNodes = Enumerable.Range(0, 10)
            .Select(index => Node($"type_standalone_{index}", "project_api", $"Standalone{index}"))
            .ToArray();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_service", "project_api", "Service")
                }.Concat(standaloneNodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal") }));

        Assert.Equal(AbsoluteY(document, "type_standalone_0"), AbsoluteY(document, "type_standalone_3"));
        Assert.Equal(AbsoluteX(document, "type_standalone_0"), AbsoluteX(document, "type_standalone_4"));
        Assert.True(AbsoluteY(document, "type_standalone_4") > AbsoluteY(document, "type_standalone_0"));
        Assert.True(AbsoluteX(document, "type_standalone_9") > AbsoluteX(document, "type_standalone_8"));
        Assert.Equal(AbsoluteY(document, "type_standalone_8"), AbsoluteY(document, "type_standalone_9"));
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

    private static int AbsoluteRight(XDocument document, string id)
    {
        return AbsoluteX(document, id) + Geometry(document, id, "width");
    }

    private static int AbsoluteBottom(XDocument document, string id)
    {
        return AbsoluteY(document, id) + Geometry(document, id, "height");
    }

    private static int Geometry(XDocument document, string id, string attributeName)
    {
        return int.Parse((string)Cell(document, id).Element("mxGeometry")!.Attribute(attributeName)!);
    }

    private static IReadOnlyList<(int X, int Y)> EdgePoints(XDocument document, string id)
    {
        return Cell(document, id)
            .Descendants("mxPoint")
            .Select(point => (
                int.Parse((string)point.Attribute("x")!),
                int.Parse((string)point.Attribute("y")!)))
            .ToArray();
    }

    private static void AssertParallelSegmentsSeparated(
        IReadOnlyList<(int X, int Y)> left,
        IReadOnlyList<(int X, int Y)> right,
        int spacing)
    {
        foreach (var leftSegment in TestSegments(left))
        {
            foreach (var rightSegment in TestSegments(right))
            {
                if (leftSegment.Start.X == leftSegment.End.X &&
                    rightSegment.Start.X == rightSegment.End.X &&
                    RangesOverlap(leftSegment.Start.Y, leftSegment.End.Y, rightSegment.Start.Y, rightSegment.End.Y))
                {
                    Assert.True(Math.Abs(leftSegment.Start.X - rightSegment.Start.X) >= spacing);
                }

                if (leftSegment.Start.Y == leftSegment.End.Y &&
                    rightSegment.Start.Y == rightSegment.End.Y &&
                    RangesOverlap(leftSegment.Start.X, leftSegment.End.X, rightSegment.Start.X, rightSegment.End.X))
                {
                    Assert.True(Math.Abs(leftSegment.Start.Y - rightSegment.Start.Y) >= spacing);
                }
            }
        }
    }

    private static IEnumerable<((int X, int Y) Start, (int X, int Y) End)> TestSegments(IReadOnlyList<(int X, int Y)> points)
    {
        for (var index = 0; index < points.Count - 1; index++)
        {
            yield return (points[index], points[index + 1]);
        }
    }

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        var firstMin = Math.Min(firstStart, firstEnd);
        var firstMax = Math.Max(firstStart, firstEnd);
        var secondMin = Math.Min(secondStart, secondEnd);
        var secondMax = Math.Max(secondStart, secondEnd);
        return firstMin <= secondMax && secondMin <= firstMax;
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
