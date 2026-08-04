using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ReplacementArchitectureRendererTests
{
    [Fact]
    public void Render_produces_a_complete_drawio_page_and_provenance_artifacts()
    {
        var result = new ReplacementArchitectureRenderer().Render(Graph(), DiagramSettings.CreateDefault());

        Assert.NotNull(result.Page.GraphModel);
        Assert.Equal(2, result.Routes.Count);
        Assert.NotNull(result.DevelopmentArtifacts);
        Assert.Contains("planning.json", result.DevelopmentArtifacts!.NamedJsonArtifacts.Keys);
        Assert.Contains("scene.json", result.DevelopmentArtifacts.NamedJsonArtifacts.Keys);
        Assert.Contains("page-model.json", result.DevelopmentArtifacts.NamedJsonArtifacts.Keys);
        Assert.Contains("expansion", result.DevelopmentArtifacts.NamedJsonArtifacts["planning.json"]);
        Assert.Contains("longestRoutes", result.DevelopmentArtifacts.NamedJsonArtifacts["planning.json"]);
        var root = result.Page.GraphModel.Element("root")!;
        Assert.Equal("0", root.Element("mxCell")?.Attribute("id")?.Value);
        Assert.Null(root.Element("mxCell")?.Attribute("vertex"));
        Assert.Equal("0", root.Elements("mxCell").Skip(1).First().Attribute("parent")?.Value);
        Assert.Null(root.Elements("mxCell").Skip(1).First().Attribute("vertex"));
        Assert.Contains(result.Page.GraphModel.Descendants("mxCell"), cell => cell.Attribute("edge")?.Value == "1");
        Assert.DoesNotContain(result.LogicalFindings, finding => finding.Category == "SharedSegment");
        Assert.DoesNotContain(result.LogicalFindings, finding => finding.Category == "LinkNodeIntersection");
    }

    [Fact]
    public void Render_rejects_no_diagonal_or_zero_length_route_segments()
    {
        var result = new ReplacementArchitectureRenderer().Render(Graph(includeStandalone: true), DiagramSettings.CreateDefault());

        foreach (var route in result.Routes)
        foreach (var segment in route.Points.Zip(route.Points.Skip(1), (start, end) => (start, end)))
        {
            Assert.True(segment.start.X == segment.end.X || segment.start.Y == segment.end.Y);
            Assert.NotEqual(segment.start, segment.end);
        }
    }

    [Fact]
    public void Render_centres_an_unobstructed_parent_over_its_direct_children()
    {
        var result = new ReplacementArchitectureRenderer().Render(ChildFanoutGraph(), DiagramSettings.CreateDefault());
        var nodes = result.Page.GraphModel.Descendants("mxCell")
            .Where(cell => cell.Attribute("vertex")?.Value == "1" && cell.Attribute("semanticNodeId") is not null)
            .ToDictionary(cell => cell.Attribute("semanticNodeId")!.Value, StringComparer.Ordinal);

        var parent = Bounds(nodes["parent"]);
        var children = new[] { Bounds(nodes["left"]), Bounds(nodes["right"]) };
        var childSpanCentre = (children.Min(child => child.X) + children.Max(child => child.Right)) / 2;

        Assert.Equal(childSpanCentre, parent.CenterX);
        Assert.All(children, child => Assert.True(child.Y > parent.Bottom));
    }

    [Fact]
    public void Composed_document_preserves_drawio_structural_root_cells()
    {
        var result = new ReplacementArchitectureRenderer().Render(Graph(), DiagramSettings.CreateDefault());
        var document = new DrawioDocumentComposer().Compose(
            new[] { result.Page }, new DrawioDocumentSettings()).Content;
        var root = XDocument.Parse(document).Descendants("root").Single();
        var cells = root.Elements("mxCell").Take(2).ToArray();

        Assert.Equal("0", cells[0].Attribute("id")?.Value);
        Assert.Null(cells[0].Attribute("vertex"));
        Assert.Equal("1", cells[1].Attribute("id")?.Value);
        Assert.Equal("0", cells[1].Attribute("parent")?.Value);
        Assert.Null(cells[1].Attribute("vertex"));
    }

    [Fact]
    public void Render_is_deterministic_for_the_same_graph_and_settings()
    {
        var settings = DiagramSettings.CreateDefault();
        var first = new ReplacementArchitectureRenderer().Render(Graph(), settings);
        var second = new ReplacementArchitectureRenderer().Render(Graph(), settings);

        Assert.Equal(first.Page.GraphModel.ToString(SaveOptions.DisableFormatting),
            second.Page.GraphModel.ToString(SaveOptions.DisableFormatting));
        Assert.Equal(first.Routes.Select(RouteSignature), second.Routes.Select(RouteSignature));
    }

    [Fact]
    public void Render_keeps_dependency_endpoints_bottom_to_top()
    {
        var result = new ReplacementArchitectureRenderer().Render(Graph(), DiagramSettings.CreateDefault());

        foreach (var route in result.Routes)
            Assert.True(route.Points.Count >= 2);
    }

    [Fact]
    public void Render_produces_a_page_and_all_routes_when_strict_findings_remain()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ParallelLaneSpacing = 0;
        settings.Layout.LinkPadding = 0;

        var result = new ReplacementArchitectureRenderer().Render(DenseFanoutGraph(), settings);

        Assert.True(result.SceneProduced);
        Assert.True(result.SerializationSucceeded);
        Assert.Equal(6, result.Routes.Count);
        Assert.NotNull(result.Page.GraphModel);
        Assert.NotNull(result.Findings);
    }

    [Fact]
    public void Render_uses_collective_topology_terminal_and_slot_allocations()
    {
        var result = new ReplacementArchitectureRenderer().Render(Graph(), DiagramSettings.CreateDefault());

        Assert.NotNull(result.RoutingEvidence);
        Assert.Equal(2, result.RoutingEvidence!.TopologyPlanCount);
        Assert.Equal(4, result.RoutingEvidence.TerminalCount);
        Assert.True(result.RoutingEvidence.InterLayerDemandCount > 0);
        Assert.True(result.RoutingEvidence.InterLayerSlotCount > 0);
        Assert.Equal(0, result.RoutingEvidence.UnsupportedPlanCount);
    }

    [Fact]
    public void Render_emits_terminal_ratios_and_semantic_provenance()
    {
        var result = new ReplacementArchitectureRenderer().Render(Graph(), DiagramSettings.CreateDefault());
        var edge = result.Page.GraphModel.Descendants("mxCell")
            .Single(cell => cell.Attribute("logicalEdgeId")?.Value == "root-child");

        Assert.NotNull(edge.Attribute("exitX"));
        Assert.NotNull(edge.Attribute("entryX"));
        Assert.Equal("root", edge.Attribute("semanticSourceId")?.Value);
        Assert.Equal("child", edge.Attribute("semanticTargetId")?.Value);
    }

    [Fact]
    public void Render_places_standalone_nodes_in_a_grid()
    {
        var graph = Graph(includeStandalone: true);
        var result = new ReplacementArchitectureRenderer().Render(graph, DiagramSettings.CreateDefault());
        var cells = result.Page.GraphModel.Descendants("mxCell").ToArray();

        Assert.Contains(cells, cell => cell.Attribute("id")?.Value == "standalone");
        Assert.DoesNotContain(result.LogicalFindings, finding => finding.Category == "NodeOverlap");
    }

    [Fact]
    public void Render_keeps_positional_subtrees_contiguous_and_in_discovery_order()
    {
        var result = new ReplacementArchitectureRenderer().Render(SubtreeGraph(), DiagramSettings.CreateDefault());
        var nodes = NodeBounds(result);

        Assert.True(nodes["a"].X < nodes["b"].X);
        Assert.True(nodes["a-child"].X < nodes["b"].X);
        Assert.True(nodes["a-child"].Right < nodes["b"].X || nodes["b"].Right < nodes["a-child"].X);
        Assert.True(nodes["a"].Y < nodes["a-child"].Y);
        Assert.Equal(nodes["a"].CenterX, (nodes["a-child"].X + nodes["a-child"].Right) / 2);
    }

    [Fact]
    public void Render_centres_a_single_parent_external_terminal_directly_below_it()
    {
        var result = new ReplacementArchitectureRenderer().Render(BaselineGraph(), DiagramSettings.CreateDefault());
        var nodes = NodeBounds(result);

        Assert.Equal(nodes["root"].CenterX, nodes["external"].CenterX);
        Assert.Equal(nodes["root"].Bottom + DiagramSettings.CreateDefault().Layout.VerticalSpacing, nodes["external"].Y);
    }

    [Fact]
    public void Render_keeps_standalone_region_outside_connected_subtrees()
    {
        var result = new ReplacementArchitectureRenderer().Render(StandaloneGridGraph(), DiagramSettings.CreateDefault());
        var nodes = NodeBounds(result);
        var connectedRight = new[] { nodes["root"], nodes["child"] }.Max(node => node.Right);
        var standalone = new[] { nodes["standalone-a"], nodes["standalone-b"], nodes["standalone-c"], nodes["standalone-d"] };

        Assert.All(standalone, node => Assert.True(node.X > connectedRight));
        Assert.True(standalone.Max(node => node.X) - standalone.Min(node => node.X) < 3 * DiagramSettings.CreateDefault().Layout.HorizontalSpacing + 3 * DiagramSettings.CreateDefault().Layout.NodeWidth);
    }

    [Fact]
    public void Render_locks_baseline_nodes_and_places_single_external_below_parent()
    {
        var settings = DiagramSettings.CreateDefault();
        var result = new ReplacementArchitectureRenderer().Render(BaselineGraph(), settings);
        var cells = result.Page.GraphModel.Descendants("mxCell")
            .Where(cell => cell.Attribute("vertex")?.Value == "1")
            .ToDictionary(cell => cell.Attribute("semanticNodeId")?.Value ?? string.Empty, StringComparer.Ordinal);
        var baselineYs = new[] { cells["root"], cells["baseline"] }
            .Select(cell => (int)cell.Element("mxGeometry")!.Attribute("y")!).Distinct().ToArray();
        var rootY = (int)cells["root"].Element("mxGeometry")!.Attribute("y")!;
        var externalY = (int)cells["external"].Element("mxGeometry")!.Attribute("y")!;

        Assert.Single(baselineYs);
        Assert.Equal(settings.Layout.NodeHeight + settings.Layout.VerticalSpacing, externalY - rootY);
        Assert.DoesNotContain(result.LogicalFindings, finding => finding.Category == "NodeOverlap");
    }

    private static ArchitectureRenderGraph Graph(bool includeStandalone = false)
    {
        var nodes = new[]
        {
            new ArchitectureRenderNode("root", "root", "project", "RootOrchestrationService", "RootOrchestrationService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 0),
            new ArchitectureRenderNode("child", "child", "project", "ChildProcessingService", "ChildProcessingService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, "root", 1)
        }.ToList();
        if (includeStandalone)
            nodes.Add(new ArchitectureRenderNode("standalone", "standalone", "project", "StandaloneService", "StandaloneService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 2));

        return new ArchitectureRenderGraph(
            new[] { new ArchitectureRenderProject("project", "Project", 0) },
            nodes,
            new[] { new ArchitectureRenderLink("root-child", "root-child", "root", "child", "root", "child", "Dependency", 0), new ArchitectureRenderLink("child-root", "child-root", "child", "root", "child", "root", "Dependency", 1) },
            new[] { "root" },
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
            {
                ["root"] = new[] { "root" },
                ["child"] = new[] { "child" }
            });
    }

    private static ArchitectureRenderGraph BaselineGraph() => new(
        new[] { new ArchitectureRenderProject("project", "Project", 0) },
        new[]
        {
            new ArchitectureRenderNode("root", "root", "project", "RootOrchestrationService", "RootOrchestrationService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 0),
            new ArchitectureRenderNode("external", "external", "project", "ExternalDependency", "ExternalDependency", "External", true, "[External]", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, "root", 1),
            new ArchitectureRenderNode("baseline", "baseline", "project", "OtherOrchestrationService", "OtherOrchestrationService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 2)
        },
        new[] { new ArchitectureRenderLink("root-external", "root-external", "root", "external", "root", "external", "Dependency", 0) },
        new[] { "root", "baseline" },
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
        {
            ["root"] = new[] { "root" },
            ["external"] = new[] { "external" },
            ["baseline"] = new[] { "baseline" }
        });

    private static ArchitectureRenderGraph ChildFanoutGraph() => new(
        new[] { new ArchitectureRenderProject("project", "Project", 0) },
        new[]
        {
            new ArchitectureRenderNode("parent", "parent", "project", "ParentOrchestrationService", "ParentOrchestrationService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 0),
            new ArchitectureRenderNode("left", "left", "project", "LeftProcessingService", "LeftProcessingService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, "parent", 1),
            new ArchitectureRenderNode("right", "right", "project", "RightProcessingService", "RightProcessingService", "Class", false, "", InterfaceResolutionStatus.NotApplicable, null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, "parent", 2)
        },
        new[]
        {
            new ArchitectureRenderLink("parent-left", "parent-left", "parent", "left", "parent", "left", "Dependency", 0),
            new ArchitectureRenderLink("parent-right", "parent-right", "parent", "right", "parent", "right", "Dependency", 1)
        },
        new[] { "parent" },
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
        {
            ["parent"] = new[] { "parent" },
            ["left"] = new[] { "left" },
            ["right"] = new[] { "right" }
        });

    private static ArchitectureRenderGraph DenseFanoutGraph() => new(
        new[] { new ArchitectureRenderProject("project", "Project", 0) },
        new[]
        {
            Node("root", "RootOrchestrationService", null, 0),
            Node("left", "LeftProcessingService", "root", 1),
            Node("right", "RightProcessingService", "root", 2)
        },
        new[]
        {
            Link("root-left", "root", "left", 0),
            Link("root-right", "root", "right", 1),
            Link("left-root", "left", "root", 2),
            Link("right-root", "right", "root", 3),
            Link("left-right", "left", "right", 4),
            Link("right-left", "right", "left", 5)
        },
        new[] { "root" },
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
        {
            ["root"] = new[] { "root" }, ["left"] = new[] { "left" }, ["right"] = new[] { "right" }
        });

    private static Rect Bounds(XElement cell)
    {
        var geometry = cell.Element("mxGeometry")!;
        return new Rect(
            (int)geometry.Attribute("x")!, (int)geometry.Attribute("y")!,
            (int)geometry.Attribute("width")!, (int)geometry.Attribute("height")!);
    }

    private static System.Collections.Generic.Dictionary<string, Rect> NodeBounds(ArchitectureRenderResult result) =>
        result.Page.GraphModel.Descendants("mxCell")
            .Where(cell => cell.Attribute("vertex")?.Value == "1" && cell.Attribute("semanticNodeId") is not null)
            .ToDictionary(cell => cell.Attribute("semanticNodeId")!.Value, Bounds, StringComparer.Ordinal);

    private static ArchitectureRenderGraph SubtreeGraph() => new(
        new[] { new ArchitectureRenderProject("project", "Project", 0) },
        new[]
        {
            Node("a", "AOrchestrationService", null, 0),
            Node("a-child", "AChildProcessingService", "a", 1),
            Node("b", "BOrchestrationService", null, 2),
            Node("b-child", "BChildProcessingService", "b", 3)
        },
        new[]
        {
            Link("a-a-child", "a", "a-child", 0),
            Link("b-b-child", "b", "b-child", 1)
        },
        new[] { "a", "b" },
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
        {
            ["a"] = new[] { "a" }, ["a-child"] = new[] { "a-child" },
            ["b"] = new[] { "b" }, ["b-child"] = new[] { "b-child" }
        });

    private static ArchitectureRenderGraph StandaloneGridGraph() => new(
        new[] { new ArchitectureRenderProject("project", "Project", 0) },
        new[]
        {
            Node("root", "RootOrchestrationService", null, 0),
            Node("child", "ChildProcessingService", "root", 1),
            Node("standalone-a", "StandaloneAService", null, 2),
            Node("standalone-b", "StandaloneBService", null, 3),
            Node("standalone-c", "StandaloneCService", null, 4),
            Node("standalone-d", "StandaloneDService", null, 5)
        },
        new[] { Link("root-child", "root", "child", 0) },
        new[] { "root" },
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
        {
            ["root"] = new[] { "root" }, ["child"] = new[] { "child" },
            ["standalone-a"] = new[] { "standalone-a" }, ["standalone-b"] = new[] { "standalone-b" },
            ["standalone-c"] = new[] { "standalone-c" }, ["standalone-d"] = new[] { "standalone-d" }
        });

    private static ArchitectureRenderNode Node(string id, string name, string? parent, int order) =>
        new(id, id, "project", name, name, "Class", false, string.Empty, InterfaceResolutionStatus.NotApplicable,
            null, null, 0, ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, parent, order);

    private static ArchitectureRenderLink Link(string id, string source, string target, int order) =>
        new(id, id, source, target, source, target, "Dependency", order);

    private static string RouteSignature(GeneratedRoute route) =>
        route.LogicalRouteId + ":" + string.Join(";", route.Points.Select(point => $"{point.X},{point.Y}"));
}
