using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
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
    public void Incoming_edge_uses_target_resolved_fill_instead_of_source_fill()
    {
        var settings = Settings();
        settings.StyleRules =
        [
            new StyleRule { Match = "Source*", Style = new NodeStyle { FillColor = "#112233" } },
            new StyleRule { Match = "Target*", Style = new NodeStyle { FillColor = "#445566" } }
        ];

        var page = new DrawioArchitectureRenderer().Render(StyledGraph(), settings);

        Assert.Contains("strokeColor=#445566", Edge(page).Attribute("style")!.Value);
    }

    [Fact]
    public void Incoming_edge_uses_target_node_specific_override()
    {
        var settings = Settings();
        settings.Overrides =
        [
            new StyleOverride
            {
                FullName = "Fixture.TargetBroker",
                Style = new NodeStyle { FillColor = "#abcdef" }
            }
        ];

        var page = new DrawioArchitectureRenderer().Render(StyledGraph(), settings);

        Assert.Contains("strokeColor=#abcdef", Edge(page).Attribute("style")!.Value);
    }

    [Fact]
    public void Incoming_edge_to_external_uses_external_fill()
    {
        var settings = Settings();
        settings.ExternalDependencyStyle = new NodeStyle { FillColor = "#fedcba", Shape = "rhombus" };
        var page = new DrawioArchitectureRenderer().Render(ExternalGraph(), settings);

        Assert.Contains("strokeColor=#fedcba", Edge(page).Attribute("style")!.Value);
    }

    [Fact]
    public void Invalid_target_fill_uses_existing_connector_fallback()
    {
        var settings = Settings();
        settings.Connector = new ConnectorStyle { StrokeColor = "#010203", StrokeWidth = 2 };
        settings.Overrides =
        [
            new StyleOverride
            {
                FullName = "Fixture.TargetBroker",
                Style = new NodeStyle { FillColor = "not-a-colour" }
            }
        ];

        var page = new DrawioArchitectureRenderer().Render(StyledGraph(), settings);

        Assert.Contains("strokeColor=#010203", Edge(page).Attribute("style")!.Value);
    }

    [Fact]
    public void Link_colour_does_not_change_geometry_or_edge_count_and_is_enumeration_stable()
    {
        var graph = StyledGraph();
        var settings = Settings();
        var first = new DrawioArchitectureRenderer().Render(graph, settings);
        var reversed = new DrawioArchitectureRenderer().Render(graph with
        {
            Nodes = graph.Nodes.Reverse().ToArray(),
            Links = graph.Links.Reverse().ToArray()
        }, settings);

        Assert.Single(first.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("edge") == "1");
        Assert.Equal(GeometrySignature(first), GeometrySignature(reversed));
    }

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

    private static ArchitectureRenderGraph StyledGraph() =>
        new ArchitectureTopologyProjector().Project(new ArchitectureDiagramModel(
            [new ArchitectureProject("project", "Fixture",
            [
                new ArchitectureNode("source", "project", "SourceController", "Fixture.SourceController", "Class", "", []),
                new ArchitectureNode("target", "project", "TargetBroker", "Fixture.TargetBroker", "Class", "", [])
            ], "")], [], [new ArchitectureLink("edge", "source", "target", "internal")], null),
            Settings().NodeDuplication);

    private static ArchitectureRenderGraph ExternalGraph() =>
        new ArchitectureTopologyProjector().Project(new ArchitectureDiagramModel(
            [new ArchitectureProject("project", "Fixture",
            [new ArchitectureNode("source", "project", "SourceController", "Fixture.SourceController", "Class", "", [])], "")],
            [new ArchitectureExternalNode("external", "ILogger", "Logging", "", "Logging.ILogger", "[External]")],
            [new ArchitectureLink("edge", "source", "external", "external")], null),
            Settings().NodeDuplication);

    private static XElement Edge(DrawioPage page) =>
        Assert.Single(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("edge") == "1");

    private static string[] GeometrySignature(DrawioPage page) =>
        page.GraphModel.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1")
            .OrderBy(cell => (string?)cell.Attribute("id"), StringComparer.Ordinal)
            .Select(cell => cell.Element("mxGeometry")!.ToString(SaveOptions.DisableFormatting))
            .ToArray();

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
