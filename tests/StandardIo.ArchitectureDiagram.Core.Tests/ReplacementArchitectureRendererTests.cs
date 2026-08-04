using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
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

    private static string RouteSignature(GeneratedRoute route) =>
        route.LogicalRouteId + ":" + string.Join(";", route.Points.Select(point => $"{point.X},{point.Y}"));
}
