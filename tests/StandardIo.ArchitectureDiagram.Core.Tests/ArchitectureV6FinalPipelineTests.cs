using System;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6FinalPipelineTests
{
    [Fact]
    public void Completed_scene_validates_and_has_no_diagonal_hard_findings()
    {
        var plan = Plan();
        var result = new ArchitectureDiagramV6Validator().Validate(plan);

        Assert.True(result.IsValid, string.Join(Environment.NewLine,
            result.Findings.Select(item => item.Code + ": " + item.Message)));
        Assert.DoesNotContain(result.Findings, item => item.Code == "PhysicalSceneDiagonalSegment");
        Assert.Equal(0, plan.PhysicalScene!.Metrics.DiagonalSegmentCount);
    }

    [Fact]
    public void Renderer_emits_exact_physical_node_and_relationship_counts()
    {
        var plan = Plan();
        var page = Render(plan);
        var cells = page.GraphModel.Descendants("mxCell").ToArray();

        Assert.Equal(plan.PhysicalNodes.Count, cells.Count(cell => (string?)cell.Attribute("physicalNodeId") is not null));
        Assert.Equal(plan.PhysicalLinks.Count, cells.Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.DoesNotContain(page.Diagnostics, item => item.Code == "V6RendererAccountingFailure");
    }

    [Fact]
    public void Renderer_preserves_source_target_ids_and_reduced_waypoint_fidelity()
    {
        var plan = Plan();
        var page = Render(plan);
        var cells = page.GraphModel.Descendants("mxCell").ToArray();

        foreach (var link in plan.PhysicalLinks)
        {
            var edge = Assert.Single(cells, cell => (string?)cell.Attribute("physicalLinkId") == link.PhysicalLinkId);
            Assert.Equal("1", (string?)edge.Attribute("edge"));
            Assert.False(string.IsNullOrWhiteSpace((string?)edge.Attribute("source")));
            Assert.False(string.IsNullOrWhiteSpace((string?)edge.Attribute("target")));
        }

        Assert.DoesNotContain(page.Diagnostics, item => item.Code == "FinalRenderedDiagonal");
    }

    [Fact]
    public void Renderer_output_is_deterministic_and_xml_escapes_labels()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "A&B <Source>", "p")
            .Node("target", "Target", "p")
            .Link("link", "source", "target")
            .BuildRequest());

        var first = Render(plan).GraphModel.ToString(SaveOptions.DisableFormatting);
        var second = Render(plan).GraphModel.ToString(SaveOptions.DisableFormatting);
        Assert.Equal(first, second);
        Assert.Contains("A&amp;B", first);
        Assert.Contains("&lt;Source&gt;", first);
    }

    [Fact]
    public void Normal_mode_keeps_invalid_route_accounting_visible()
    {
        var plan = Plan();
        var malformed = new PlannedArchitectureDiagram(
            plan.Request, plan.PhysicalNodes, plan.PhysicalLinks, plan.DiagramGrid, plan.ProjectGrids,
            plan.NodePlacements, Array.Empty<PlannedGridRoute>(), plan.Sizing, plan.Diagnostics,
            plan.Projection, plan.NodeMetadata, plan.LinkMetadata, plan.SubtreeReservations,
            plan.StageStatus, placementFreeze: plan.PlacementFreeze)
        {
            Geometry = plan.Geometry,
            PhysicalScene = plan.PhysicalScene
        };

        var validation = new ArchitectureDiagramV6Validator().Validate(malformed);
        var page = Render(malformed);

        Assert.Contains(validation.Findings, item => item.Code == "PhysicalLinkRouteCount");
        Assert.Equal(plan.PhysicalLinks.Count, page.GraphModel.Descendants("mxCell")
            .Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.DoesNotContain(page.Diagnostics, item => item.Code == "V6RelationshipOmitted");
    }

    [Fact]
    public void Strict_validation_rejects_hard_findings_while_normal_mode_remains_eligible()
    {
        var plan = Plan();
        var malformed = new PlannedArchitectureDiagram(
            plan.Request, plan.PhysicalNodes, plan.PhysicalLinks, plan.DiagramGrid, plan.ProjectGrids,
            plan.NodePlacements, Array.Empty<PlannedGridRoute>(), plan.Sizing, plan.Diagnostics,
            plan.Projection, plan.NodeMetadata, plan.LinkMetadata, plan.SubtreeReservations,
            plan.StageStatus, placementFreeze: plan.PlacementFreeze)
        {
            Geometry = plan.Geometry,
            PhysicalScene = plan.PhysicalScene
        };

        var validation = new ArchitectureDiagramV6Validator().Validate(malformed);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Findings, item => item.Severity == ArchitecturePlanningDiagnosticSeverity.Error);
    }

    [Fact]
    public void Empty_plan_still_emits_a_valid_architecture_page_shell()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .BuildRequest());
        var page = Render(plan);

        Assert.NotNull(page.GraphModel.Element("root"));
        Assert.Empty(page.GraphModel.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1"));
    }

    private static PlannedArchitectureDiagram Plan() => new ArchitectureDiagramV6Planner().Plan(
        new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest());

    private static DrawioPage Render(PlannedArchitectureDiagram plan) =>
        new DrawioArchitectureV6Renderer().Render(plan,
            new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
}
