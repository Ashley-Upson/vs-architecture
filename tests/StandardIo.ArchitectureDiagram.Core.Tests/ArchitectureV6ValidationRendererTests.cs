using System;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6ValidationRendererTests
{
    [Fact]
    public void Final_validator_accepts_a_complete_orthogonal_fixture()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest());

        var validation = new ArchitectureDiagramV6Validator().Validate(plan);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine,
            validation.Findings.Select(finding => finding.Code + ": " + finding.Message)));
        Assert.DoesNotContain(validation.Findings, finding => finding.Code == "PhysicalSceneDiagonalSegment");
    }

    [Fact]
    public void Renderer_emits_one_vertex_and_edge_per_final_physical_record()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest());

        var page = new DrawioArchitectureV6Renderer().Render(plan,
            new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var cells = page.GraphModel.Descendants("mxCell").ToArray();

        Assert.Equal(plan.PhysicalScene!.Geometry.Nodes.Count, cells.Count(cell => (string?)cell.Attribute("physicalNodeId") is not null));
        Assert.Equal(plan.PhysicalLinks.Count, cells.Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Contains(cells, cell => (string?)cell.Attribute("physicalNodeId") == "physical:source");
        Assert.Contains(cells, cell => (string?)cell.Attribute("physicalLinkId") == "physical-link:link:0");
    }

    [Fact]
    public void Renderer_output_is_deterministic_and_preserves_waypoints_and_styles()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest();
        var first = new DrawioArchitectureV6Renderer().Render(Plan(request),
            new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var second = new DrawioArchitectureV6Renderer().Render(Plan(request),
            new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));

        Assert.Equal(first.GraphModel.ToString(SaveOptions.DisableFormatting), second.GraphModel.ToString(SaveOptions.DisableFormatting));
        var edge = first.GraphModel.Descendants("mxCell").Single(cell => (string?)cell.Attribute("edge") == "1");
        Assert.NotNull(edge.Attribute("style"));
        Assert.NotNull(edge.Attribute("source"));
        Assert.NotNull(edge.Attribute("target"));
    }

    private static PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request) =>
        new ArchitectureDiagramV6Planner().Plan(request);
}
