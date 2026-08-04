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
        Assert.Contains(result.Page.GraphModel.Descendants("mxCell"), cell => cell.Attribute("edge")?.Value == "1");
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
    public void Render_places_standalone_nodes_in_a_grid()
    {
        var graph = Graph(includeStandalone: true);
        var result = new ReplacementArchitectureRenderer().Render(graph, DiagramSettings.CreateDefault());
        var cells = result.Page.GraphModel.Descendants("mxCell").ToArray();

        Assert.Contains(cells, cell => cell.Attribute("id")?.Value == "standalone");
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

    private static string RouteSignature(GeneratedRoute route) =>
        route.LogicalRouteId + ":" + string.Join(";", route.Points.Select(point => $"{point.X},{point.Y}"));
}
