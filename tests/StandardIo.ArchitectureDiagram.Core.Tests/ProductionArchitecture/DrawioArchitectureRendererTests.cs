using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioArchitectureRendererTests
{
    [Fact]
    public void Render_accepts_typed_architecture_model_and_returns_only_a_page()
    {
        var model = Graph();

        var page = new DrawioArchitectureRenderer().Render(model, Settings());

        Assert.Equal("Architecture", page.SuggestedName);
        Assert.Equal("architecture", page.StablePageKey);
        Assert.Equal("mxGraphModel", page.GraphModel.Name.LocalName);
        Assert.Null(page.GraphModel.Ancestors("mxfile").SingleOrDefault());
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("value") == "Service");
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell =>
            ((string?)cell.Attribute("logicalEdgeId"))?.Contains("edge", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Production_page_geometry_matches_canonical_project_region_exporter_for_equivalent_input()
    {
        var graph = Graph();
        var typed = new DrawioArchitectureRenderer().Render(graph, Settings());
        var legacySettings = DiagramSettings.CreateDefault();
        legacySettings.Layout = Settings().Layout;
        var canonicalPage = new DeterministicDrawioExporter()
            .GenerateArchitectureProjectRegionResult(graph, legacySettings).Page.GraphModel;

        Assert.Equal(canonicalPage.ToString(SaveOptions.DisableFormatting),
            typed.GraphModel.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void Production_and_development_modes_use_the_same_canonical_geometry_authority()
    {
        var renderer = new DrawioArchitectureRenderer();

        var production = renderer.RenderWithDiagnostics(Graph(), Settings(), ArchitectureRenderingMode.Production);
        var development = renderer.RenderWithDiagnostics(Graph(), Settings(), ArchitectureRenderingMode.DevelopmentProjectRegion);

        Assert.Equal(
            development.Page.GraphModel.ToString(SaveOptions.DisableFormatting),
            production.Page.GraphModel.ToString(SaveOptions.DisableFormatting));
        Assert.Equal(
            development.Routes.Select(RouteSignature),
            production.Routes.Select(RouteSignature));
        Assert.Equal(development.LogicalFindings, production.LogicalFindings);
        Assert.Equal(development.PhysicalFindings, production.PhysicalFindings);
    }

    private static string RouteSignature(GeneratedRoute route) =>
        route.LogicalRouteId + ":" + string.Join(";", route.Points.Select(point => $"{point.X},{point.Y}"));

    private static ArchitectureDiagramModel Model() => new(
        [new ArchitectureProject("project", "Fixture", [
            new ArchitectureNode("root", "project", "Root", "Fixture.Root", "Class", "", []),
            new ArchitectureNode("service", "project", "Service", "Fixture.Service", "Class", "", [])], "")],
        [], [new ArchitectureLink("edge", "root", "service", "internal")], null);

    private static ArchitectureRenderGraph Graph() =>
        new ArchitectureTopologyProjector().Project(Model(), Settings().NodeDuplication);

    private static ArchitectureRenderSettings Settings()
    {
        var defaults = DiagramSettings.CreateDefault();
        return new ArchitectureRenderSettings
        {
            Canvas = defaults.Canvas,
            Layout = defaults.Layout,
            StyleRules = defaults.StyleRules,
            Overrides = defaults.Overrides,
            ShowProjectContainers = defaults.ShowProjectContainers,
            ProjectContainerStyle = defaults.ProjectContainerStyle,
            ExternalDependencyStyle = defaults.ExternalDependencyStyle,
            Connector = defaults.Connector,
            NodeDuplication = defaults.NodeDuplication
        };
    }
}
