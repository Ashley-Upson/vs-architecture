using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioArchitectureRendererTests
{
    [Fact]
    public void Render_accepts_typed_architecture_model_and_returns_only_a_page()
    {
        var model = Model();

        var page = new DrawioArchitectureRenderer().Render(model, Settings());

        Assert.Equal("Architecture", page.SuggestedName);
        Assert.Equal("architecture", page.StablePageKey);
        Assert.Equal("mxGraphModel", page.GraphModel.Name.LocalName);
        Assert.Null(page.GraphModel.Ancestors("mxfile").SingleOrDefault());
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "service");
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("logicalEdgeId") == "edge");
    }

    [Fact]
    public void Typed_page_geometry_matches_legacy_exporter_architecture_page_for_equivalent_input()
    {
        var typed = new DrawioArchitectureRenderer().Render(Model(), Settings());
        var legacySettings = DiagramSettings.CreateDefault();
        legacySettings.Layout = Settings().Layout;
        var legacyModel = new DiagramModel(
            [new ProjectContainer("project", "Fixture", [
                new TypeNode("root", "project", "Root", "Fixture.Root", "Class"),
                new TypeNode("service", "project", "Service", "Fixture.Service", "Class")])],
            [], [new DependencyEdge("edge", "root", "service", "internal")]);
        var document = new DeterministicDrawioExporter().GenerateResult(legacyModel, legacySettings).Document;
        var legacyPage = XDocument.Parse(document).Root!.Elements("diagram").First().Element("mxGraphModel")!;

        Assert.Equal(legacyPage.ToString(SaveOptions.DisableFormatting),
            typed.GraphModel.ToString(SaveOptions.DisableFormatting));
    }

    private static ArchitectureDiagramModel Model() => new(
        [new ArchitectureProject("project", "Fixture", [
            new ArchitectureNode("root", "project", "Root", "Fixture.Root", "Class", "", []),
            new ArchitectureNode("service", "project", "Service", "Fixture.Service", "Class", "", [])], "")],
        [], [new ArchitectureLink("edge", "root", "service", "internal")], null);

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
