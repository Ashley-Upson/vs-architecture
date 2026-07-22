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
            Assert.Contains((string?)segment.Attribute("routingMode"), new[] { "traversal", "fallback" });
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
    public void Render_owns_unique_external_dependency_with_its_source_project()
    {
        var document = Render(new DiagramModel(
            new[] { Project("project_a", Node("source", "project_a", "Source")) },
            new[] { new ExternalDependencyNode("external", "External", "External", "external", "External", "[External]") },
            new[] { new DependencyEdge("edge_external", "source", "external", "external") }));

        var segments = LogicalSegments(document, "edge_external");

        var segment = Assert.Single(segments);
        Assert.Equal("project_a", (string?)segment.Attribute("parent"));
        Assert.Equal("project_a", (string?)segment.Attribute("ownerProjectId"));
        Assert.All(segments, segment => Assert.Equal("external", (string?)segment.Attribute("semanticTargetId")));
        Assert.Contains("endArrow=block", Style(segment));

        var external = document.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == "external");
        var project = document.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == "project_a");
        Assert.Equal("project_a", (string?)external.Attribute("parent"));
        Assert.Equal(
            Coordinate(project, "x") + Coordinate(external, "x"),
            AbsoluteCoordinate(document, external, "x"));
        Assert.Equal(
            Coordinate(project, "y") + Coordinate(external, "y"),
            AbsoluteCoordinate(document, external, "y"));
        Assert.True(Coordinate(external, "x") >= DiagramSettings.CreateDefault().Layout.ContainerPadding);
        Assert.True(Coordinate(external, "y") >= DiagramSettings.CreateDefault().Layout.ProjectHeaderHeight);
        Assert.True(
            Coordinate(project, "width") - Coordinate(external, "x") - Coordinate(external, "width") >=
            DiagramSettings.CreateDefault().Layout.ContainerPadding);
        Assert.True(
            Coordinate(project, "height") - Coordinate(external, "y") - Coordinate(external, "height") >=
            DiagramSettings.CreateDefault().Layout.ContainerPadding);

        var source = document.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == "source");
        const int deltaX = 37;
        const int deltaY = -19;
        Assert.Equal(
            AbsoluteCoordinate(document, source, "x") + deltaX,
            Coordinate(project, "x") + deltaX + Coordinate(source, "x"));
        Assert.Equal(
            AbsoluteCoordinate(document, external, "y") + deltaY,
            Coordinate(project, "y") + deltaY + Coordinate(external, "y"));
        var firstWaypoint = segment.Element("mxGeometry")!.Element("Array")!.Elements("mxPoint").First();
        Assert.Equal(
            Coordinate(project, "x") + deltaX + int.Parse((string)firstWaypoint.Attribute("x")!),
            Coordinate(project, "x") + int.Parse((string)firstWaypoint.Attribute("x")!) + deltaX);

        var reopened = XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
        var reopenedExternal = reopened.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == "external");
        Assert.Equal("project_a", (string?)reopenedExternal.Attribute("parent"));
        Assert.Equal(Coordinate(external, "x"), Coordinate(reopenedExternal, "x"));
        Assert.Equal(Coordinate(external, "y"), Coordinate(reopenedExternal, "y"));
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
        var edgeA = Assert.Single(LogicalSegments(document, "edge_a"));
        var edgeB = Assert.Single(LogicalSegments(document, "edge_b"));
        Assert.Equal("project_a", (string?)document.Descendants("mxCell")
            .Single(cell => (string?)cell.Attribute("id") == (string?)edgeA.Attribute("target"))
            .Attribute("parent"));
        Assert.Equal("project_b", (string?)document.Descendants("mxCell")
            .Single(cell => (string?)cell.Attribute("id") == (string?)edgeB.Attribute("target"))
            .Attribute("parent"));
        Assert.NotEqual((string?)edgeA.Attribute("target"), (string?)edgeB.Attribute("target"));
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

        var firstAnchor = reopenedAnchors[0];
        var owner = reopened.Descendants("mxCell")
            .Single(cell => (string?)cell.Attribute("id") == (string?)firstAnchor.Attribute("parent"));
        const int movementDelta = 41;
        Assert.Equal(
            Coordinate(owner, "x") + Coordinate(firstAnchor, "x") + movementDelta,
            Coordinate(owner, "x") + movementDelta + Coordinate(firstAnchor, "x"));
    }

    [Fact]
    public void External_ownership_serialization_is_deterministic()
    {
        var model = new DiagramModel(
            new[] { Project("project_a", Node("source", "project_a", "Source")) },
            new[] { new ExternalDependencyNode("external", "External", "External", "external", "External", "[External]") },
            new[] { new DependencyEdge("edge_external", "source", "external", "external") });

        Assert.Equal(Render(model).ToString(SaveOptions.DisableFormatting), Render(model).ToString(SaveOptions.DisableFormatting));
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

    private static int Coordinate(XElement cell, string name) =>
        int.Parse((string)cell.Element("mxGeometry")!.Attribute(name)!);

    private static int AbsoluteCoordinate(XDocument document, XElement cell, string name)
    {
        var value = Coordinate(cell, name);
        var parentId = (string?)cell.Attribute("parent");
        if (parentId is not null && parentId != "1")
        {
            value += Coordinate(document.Descendants("mxCell").Single(item => (string?)item.Attribute("id") == parentId), name);
        }

        return value;
    }
}
