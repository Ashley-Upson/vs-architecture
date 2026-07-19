using System;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectRegionRendererTests
{
    [Fact]
    public void Generates_complete_project_without_legacy_layout()
    {
        var result = new DeterministicDrawioExporter().GenerateProjectRegion(Fixture(), DiagramSettings.CreateDefault());
        var report = JsonDocument.Parse(result.InvariantJson).RootElement;
        var xml = XDocument.Parse(result.Document);

        Assert.False(report.GetProperty("legacyRenderLayoutUsed").GetBoolean());
        Assert.False(report.GetProperty("legacyCoordinatesUsed").GetBoolean());
        Assert.False(report.GetProperty("legacyPathsUsed").GetBoolean());
        Assert.True(report.GetProperty("interLayerSlotAllocationUsed").GetBoolean());
        Assert.False(report.GetProperty("developmentTrialUsed").GetBoolean());
        Assert.True(report.GetProperty("fallbackOccursOutsideRenderer").GetBoolean());
        Assert.Equal("DeterministicSlotAllocator", report.GetProperty("horizontalSegmentYAuthority").GetString());
        Assert.Equal("VerticalLinkColumnAllocator / ReturnColumnAllocator", report.GetProperty("verticalColumnXAuthority").GetString());
        Assert.Equal("CanonicalTopologyFamilySelector", report.GetProperty("topologySelectionAuthority").GetString());
        Assert.Equal(15, report.GetProperty("topologyFamilies").EnumerateObject().Sum(item => item.Value.GetInt32()));
        Assert.True(report.GetProperty("interLayersDiscovered").GetInt32() > 0);
        Assert.True(report.GetProperty("slotDemands").GetInt32() >= 15);
        Assert.Equal(report.GetProperty("slotDemands").GetInt32(), report.GetProperty("slotsAssigned").GetInt32());
        Assert.Equal(0, report.GetProperty("corridorLaneYAssignmentsRemaining").GetInt32());
        Assert.Equal(0, report.GetProperty("repairBasedHorizontalOffsetsRemaining").GetInt32());
        Assert.Equal(0, report.GetProperty("corridorLaneXAssignmentsRemaining").GetInt32());
        Assert.Equal(0, report.GetProperty("repairBasedVerticalOffsetsRemaining").GetInt32());
        Assert.Equal("EdgeTraversalCompiler", report.GetProperty("obstacleCompilationAuthority").GetString());
        Assert.Equal(12, report.GetProperty("semanticNodeCount").GetInt32());
        Assert.Equal(15, report.GetProperty("semanticLinkCount").GetInt32());
        Assert.Equal(15, xml.Descendants("mxCell")
            .Select(item => (string?)item.Attribute("logicalEdgeId"))
            .Where(item => item is not null)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public void Generation_is_byte_deterministic()
    {
        var exporter = new DeterministicDrawioExporter();
        var first = exporter.GenerateProjectRegion(Fixture(), DiagramSettings.CreateDefault());
        var second = exporter.GenerateProjectRegion(Fixture(), DiagramSettings.CreateDefault());

        Assert.Equal(first.Document, second.Document);
    }

    [Fact]
    public void Unsupported_topology_explains_whole_project_fallback()
    {
        var diagram = new DiagramModel(
            new[] { new ProjectContainer("p", "Project", new[] { Node("a"), Node("b") }) },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("self", "a", "a", "Dependency") });

        var result = new DeterministicDrawioExporter().GenerateProjectRegion(diagram, DiagramSettings.CreateDefault());

        Assert.False(result.Eligible);
        Assert.Contains("UnsupportedSelfLoop:self", result.FallbackReasons);
    }

    [Fact]
    public void Boundary_link_preserves_semantic_node_endpoints()
    {
        var result = new DeterministicDrawioExporter().GenerateProjectRegion(Fixture(), DiagramSettings.CreateDefault());
        var edge = XDocument.Parse(result.Document).Descendants("mxCell")
            .Single(item => (string?)item.Attribute("logicalEdgeId") == "e15");

        Assert.Equal("b2", (string?)edge.Attribute("semanticSourceId"));
        Assert.Equal("outside", (string?)edge.Attribute("semanticTargetId"));
    }

    internal static DiagramModel Fixture()
    {
        var nodes = new[] { "root", "a", "b", "c", "a1", "a2", "b1", "b2", "c1", "c2", "shared" }
            .Select(Node).ToArray();
        var edges = new[]
        {
            Edge("e01", "root", "a"), Edge("e02", "root", "b"), Edge("e03", "root", "c"),
            Edge("e04", "a", "a1"), Edge("e05", "a1", "a2"), Edge("e06", "b", "b1"),
            Edge("e07", "b", "b2"), Edge("e08", "c", "c1"), Edge("e09", "c1", "c2"),
            Edge("e10", "a1", "b1"), Edge("e11", "c2", "a"), Edge("e12", "root", "shared"),
            Edge("e13", "c1", "shared"), Edge("e14", "a2", "shared"), Edge("e15", "b2", "outside")
        };
        return new DiagramModel(
            new[] { new ProjectContainer("p", "Project Region", nodes) },
            new[] { new ExternalDependencyNode("outside", "External", "External", "outside", "External.Outside", "[External]") },
            edges);
    }

    private static TypeNode Node(string id) => new(id, "p", id, $"Fixture.{id}", "Class");
    private static DependencyEdge Edge(string id, string source, string target) =>
        new(id, source, target, "Dependency");
}
