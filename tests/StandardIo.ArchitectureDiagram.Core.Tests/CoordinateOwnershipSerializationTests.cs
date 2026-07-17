using System;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CoordinateOwnershipSerializationTests
{
    [Fact]
    public void Render_serializes_cross_project_segments_with_logical_metadata_and_arrows()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                Project("project_a", Node("source", "project_a", "Source")),
                Project("project_b", Node("target", "project_b", "Target"))
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_ab", "source", "target", "internal") }));

        var segments = LogicalSegments(document, "edge_ab");

        Assert.True(segments.Length >= 2);
        Assert.Equal(Enumerable.Range(0, segments.Length), segments.Select(SegmentIndex));
        Assert.All(segments, segment =>
        {
            Assert.Equal("source", (string?)segment.Attribute("semanticSourceId"));
            Assert.Equal("target", (string?)segment.Attribute("semanticTargetId"));
        });
        Assert.Contains("endArrow=none", Style(segments[0]));
        Assert.All(segments.Skip(1).Take(segments.Length - 2), segment =>
        {
            Assert.Contains("startArrow=none", Style(segment));
            Assert.Contains("endArrow=none", Style(segment));
        });
        Assert.Contains("endArrow=block", Style(segments[^1]));
        Assert.Equal("project_a", (string?)segments[0].Attribute("ownerProjectId"));
        Assert.Equal("project_b", (string?)segments[^1].Attribute("ownerProjectId"));
        if (segments.Length > 2)
        {
            Assert.Contains(segments, segment => (string?)segment.Attribute("parent") == "1");
        }
        Assert.Single(segments, segment => (string?)segment.Attribute("labelOwner") == "1");
    }

    [Fact]
    public void Render_keeps_same_project_dependency_as_one_project_relative_edge()
    {
        var document = Render(new DiagramModel(
            new[] { Project("project_a", Node("source", "project_a", "Source"), Node("target", "project_a", "Target")) },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_internal", "source", "target", "internal") }));

        var edge = Assert.Single(LogicalSegments(document, "edge_internal"));

        Assert.Equal("edge_internal", (string?)edge.Attribute("id"));
        Assert.Equal("project_a", (string?)edge.Attribute("parent"));
        Assert.Equal("project_a", (string?)edge.Attribute("ownerProjectId"));
        Assert.Equal("complete", (string?)edge.Attribute("segmentRole"));
        Assert.Contains("endArrow=block", Style(edge));
    }

    [Fact]
    public void Render_splits_project_to_external_dependency_and_preserves_semantic_target()
    {
        var document = Render(new DiagramModel(
            new[] { Project("project_a", Node("source", "project_a", "Source")) },
            new[] { new ExternalDependencyNode("external", "External", "External", "external", "External", "[External]") },
            new[] { new DependencyEdge("edge_external", "source", "external", "external") }));

        var segments = LogicalSegments(document, "edge_external");

        Assert.Equal(2, segments.Length);
        Assert.Equal("project_a", (string?)segments[0].Attribute("parent"));
        Assert.Equal("1", (string?)segments[1].Attribute("parent"));
        Assert.All(segments, segment => Assert.Equal("external", (string?)segment.Attribute("semanticTargetId")));
        Assert.Contains("endArrow=none", Style(segments[0]));
        Assert.Contains("endArrow=block", Style(segments[1]));
    }

    [Fact]
    public void Render_preserves_original_semantic_target_for_duplicated_external_vertices()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                Project("project_a", Node("source_a", "project_a", "SourceA")),
                Project("project_b", Node("source_b", "project_b", "SourceB"))
            },
            new[] { new ExternalDependencyNode("external", "External", "External", "external", "External", "[External]") },
            new[]
            {
                new DependencyEdge("edge_a", "source_a", "external", "external"),
                new DependencyEdge("edge_b", "source_b", "external", "external")
            }));

        Assert.All(LogicalSegments(document, "edge_a"),
            segment => Assert.Equal("external", (string?)segment.Attribute("semanticTargetId")));
        Assert.All(LogicalSegments(document, "edge_b"),
            segment => Assert.Equal("external", (string?)segment.Attribute("semanticTargetId")));
    }

    [Fact]
    public void Zero_size_anchor_metadata_survives_save_reopen_normalization()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                Project("project_a", Node("source", "project_a", "Source")),
                Project("project_b", Node("target", "project_b", "Target"))
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_ab", "source", "target", "internal") }));
        var anchors = document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("anchorRole") == "ownership-boundary")
            .ToArray();
        Assert.NotEmpty(anchors);
        foreach (var anchor in anchors)
        {
            anchor.Element("mxGeometry")!.Attribute("width")?.Remove();
            anchor.Element("mxGeometry")!.Attribute("height")?.Remove();
        }

        var reopened = XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
        var reopenedAnchors = reopened.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("anchorRole") == "ownership-boundary")
            .ToArray();

        Assert.Equal(anchors.Length, reopenedAnchors.Length);
        Assert.All(reopenedAnchors, anchor =>
        {
            Assert.Equal("edge_ab", (string?)anchor.Attribute("logicalEdgeId"));
            Assert.False(string.IsNullOrWhiteSpace((string?)anchor.Attribute("ownerProjectId")));
            Assert.Contains("movable=0", Style(anchor));
            Assert.Contains("resizable=0", Style(anchor));
            Assert.Contains("deletable=0", Style(anchor));
            Assert.Equal(0, Dimension(anchor, "width"));
            Assert.Equal(0, Dimension(anchor, "height"));
        });
    }

    private static XDocument Render(DiagramModel model) =>
        XDocument.Parse(new DrawioDiagramRenderer().Render(model, DiagramSettings.CreateDefault()));

    private static ProjectContainer Project(string id, params TypeNode[] nodes) => new(id, id, nodes);

    private static TypeNode Node(string id, string projectId, string name) =>
        new(id, projectId, name, $"{projectId}.{name}", "Class");

    private static XElement[] LogicalSegments(XDocument document, string logicalEdgeId) =>
        document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("edge") == "1" &&
                (string?)cell.Attribute("logicalEdgeId") == logicalEdgeId)
            .OrderBy(SegmentIndex)
            .ToArray();

    private static int SegmentIndex(XElement cell) => (int)cell.Attribute("segmentIndex")!;

    private static string Style(XElement cell) => (string?)cell.Attribute("style") ?? string.Empty;

    private static int Dimension(XElement cell, string name) =>
        int.TryParse((string?)cell.Element("mxGeometry")!.Attribute(name), out var value) ? value : 0;
}
