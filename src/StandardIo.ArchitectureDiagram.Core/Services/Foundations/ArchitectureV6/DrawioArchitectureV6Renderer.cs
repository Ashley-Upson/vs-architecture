using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class DrawioArchitectureV6Renderer : IArchitectureDiagramRenderer<DrawioPage>
{
    private static readonly ArchitectureV6StyleRule FallbackStyle = new("<fallback>", "#dae8fc", "#6c8ebf", "#111111", "rounded", true, null);
    private static readonly ArchitectureV6StyleRule ExternalStyle = new("<external>", "#f36c21", "#a43b08", "#111111", "rhombus", true, null);

    public DrawioPage Render(PlannedArchitectureDiagram diagram, ArchitectureRenderRequest request)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var diagnostics = new List<DiagramDiagnostic>();
        foreach (var finding in diagram.Diagnostics.Findings)
            diagnostics.Add(new DiagramDiagnostic(finding.Code, finding.Message, finding.SubjectId));

        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var geometry = diagram.Geometry;
        var geometryById = geometry?.Nodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal)
            ?? new Dictionary<string, PlannedPhysicalNodeGeometry>(StringComparer.Ordinal);
        var projects = geometry?.Projects ?? Array.Empty<PlannedProjectGeometry>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var fallbackCount = 0;
        var skippedCount = 0;

        if (diagram.Request.ProjectPlacement.ShowProjectContainers)
            foreach (var project in projects)
                root.Add(ProjectCell(project, diagram.Request));

        foreach (var node in diagram.PhysicalNodes)
        {
            if (!geometryById.TryGetValue(node.PhysicalNodeId, out var nodeGeometry))
            {
                skippedCount++;
                diagnostics.Add(new DiagramDiagnostic("V6SkippedNode", "No planned geometry was available for a physical node.", node.SemanticNodeId));
                continue;
            }

            var style = ResolveStyle(diagram.Request, node);
            if (style.IsFallback) fallbackCount++;
            var parent = diagram.Request.ProjectPlacement.ShowProjectContainers && node.ProjectId is not null
                ? ProjectCellId(node.ProjectId)
                : "1";
            var rectangle = parent == "1"
                ? nodeGeometry.AbsoluteBounds
                : new AbsoluteRectangle(nodeGeometry.RelativeBounds.X, nodeGeometry.RelativeBounds.Y, nodeGeometry.RelativeBounds.Width, nodeGeometry.RelativeBounds.Height);
            root.Add(NodeCell(node, nodeGeometry, parent, rectangle, style.Rule, diagram.Request.RoutePlanning.ExternalDependencyTag));
            emitted.Add(node.PhysicalNodeId);
        }

        foreach (var node in diagram.PhysicalNodes)
            if (!emitted.Contains(node.PhysicalNodeId) && !geometryById.ContainsKey(node.PhysicalNodeId))
                diagnostics.Add(new DiagramDiagnostic("V6SkippedNode", "A planned physical node was not emitted.", node.SemanticNodeId));

        var expected = diagram.PhysicalNodes.Count;
        if (emitted.Count != expected)
            diagnostics.Add(new DiagramDiagnostic("V6VertexCountMismatch", $"Expected {expected} physical node vertices but emitted {emitted.Count}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6VertexCount", emitted.Count.ToString(CultureInfo.InvariantCulture), null));
        diagnostics.Add(new DiagramDiagnostic("V6SkippedNodeCount", skippedCount.ToString(CultureInfo.InvariantCulture), null));
        diagnostics.Add(new DiagramDiagnostic("V6StyleFallbackCount", fallbackCount.ToString(CultureInfo.InvariantCulture), null));
        if (geometry is not null)
            diagnostics.Add(new DiagramDiagnostic("V6OutputBounds", $"{geometry.AbsoluteDiagramBounds.Width}x{geometry.AbsoluteDiagramBounds.Height}", null));

        var bounds = geometry?.AbsoluteDiagramBounds ?? new AbsoluteRectangle(0, 0, 1, 1);
        var graph = new XElement("mxGraphModel",
            new XAttribute("dx", Math.Max(1200, bounds.Width).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("dy", Math.Max(900, bounds.Height).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("grid", "0"), new XAttribute("gridSize", "10"),
            new XAttribute("guides", "1"), new XAttribute("tooltips", "1"),
            new XAttribute("connect", "1"), new XAttribute("arrows", "1"),
            new XAttribute("fold", "1"), new XAttribute("page", "0"),
            new XAttribute("pageScale", "1"),
            new XAttribute("pageWidth", Math.Max(1, bounds.Width).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("pageHeight", Math.Max(1, bounds.Height).ToString(CultureInfo.InvariantCulture)),
            root);
        return new DrawioPage("Architecture", "architecture", graph, diagnostics);
    }

    private static XElement ProjectCell(PlannedProjectGeometry project, ArchitecturePlanningRequest request) =>
        new XElement("mxCell",
            new XAttribute("id", ProjectCellId(project.ProjectId)),
            new XAttribute("value", project.ProjectId),
            new XAttribute("style", StyleString(request.ProjectContainerStyle ?? new ArchitectureV6StyleRule("<project>", "#323a40", "#263238", "#ffffff", "swimlane", true, "swimlaneLine=0;startSize=34;horizontal=1;"))),
            new XAttribute("vertex", "1"), new XAttribute("parent", "1"),
            new XAttribute("projectId", project.ProjectId),
            new XElement("mxGeometry", new XAttribute("x", project.AbsoluteBounds.X), new XAttribute("y", project.AbsoluteBounds.Y),
                new XAttribute("width", project.AbsoluteBounds.Width), new XAttribute("height", project.AbsoluteBounds.Height), new XAttribute("as", "geometry")));

    private static XElement NodeCell(
        PlannedPhysicalNode node,
        PlannedPhysicalNodeGeometry geometry,
        string parent,
        AbsoluteRectangle rectangle,
        ArchitectureV6StyleRule style,
        string externalTag)
    {
        var value = node.IsExternal ? (externalTag ?? "[External]") + " " + node.SemanticName : node.SemanticName;
        var cell = new XElement("mxCell",
            new XAttribute("id", CellId(node.PhysicalNodeId)),
            new XAttribute("value", value ?? string.Empty),
            new XAttribute("style", StyleString(style)),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", parent),
            new XAttribute("physicalNodeId", node.PhysicalNodeId),
            new XAttribute("semanticNodeId", node.SemanticNodeId),
            new XAttribute("semanticFullName", node.SemanticFullName ?? string.Empty),
            new XAttribute("projectionMode", node.ProjectionMode.ToString()),
            new XAttribute("projectId", node.ProjectId ?? string.Empty),
            new XAttribute("external", node.IsExternal ? "1" : "0"),
            new XAttribute("standalone", node.IsStandalone ? "1" : "0"),
            new XAttribute("positionalOwnerId", geometry.PositionalOwnerId ?? string.Empty),
            new XElement("mxGeometry", new XAttribute("x", rectangle.X), new XAttribute("y", rectangle.Y),
                new XAttribute("width", rectangle.Width), new XAttribute("height", rectangle.Height), new XAttribute("as", "geometry")));
        if (node.DuplicationProvenance is not null)
        {
            cell.Add(new XAttribute("duplicationSemanticNodeId", node.DuplicationProvenance.SemanticNodeId));
            cell.Add(new XAttribute("duplicationReason", node.DuplicationProvenance.Reason));
            cell.Add(new XAttribute("duplicationParentPhysicalNodeId", node.DuplicationProvenance.ParentPhysicalNodeId ?? string.Empty));
        }
        return cell;
    }

    private static ResolvedStyle ResolveStyle(ArchitecturePlanningRequest request, PlannedPhysicalNode node)
    {
        if (node.IsExternal) return new ResolvedStyle(request.ExternalDependencyStyle ?? ExternalStyle, false);
        var overrideStyle = request.StyleOverridesWithValues?.FirstOrDefault(item => string.Equals(item.FullName, node.SemanticFullName, StringComparison.Ordinal));
        if (overrideStyle is not null) return new ResolvedStyle(overrideStyle.Style, false);
        var match = request.StylePolicies?.FirstOrDefault(rule => GlobMatcher.IsMatch(node.SemanticName, rule.Match) || GlobMatcher.IsMatch(node.SemanticFullName, rule.Match));
        return match is null ? new ResolvedStyle(FallbackStyle, true) : new ResolvedStyle(match, false);
    }

    private static string StyleString(ArchitectureV6StyleRule style) =>
        $"shape={style.Shape};whiteSpace=wrap;html=1;rounded={(style.Shape == "rounded" ? "1" : "0")};shadow={(style.Shadow ? "1" : "0")};fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};{style.ExtraStyle}";

    private static string CellId(string physicalNodeId) => StableId.From("architecture_v6_node", physicalNodeId);
    private static string ProjectCellId(string projectId) => StableId.From("architecture_v6_project", projectId);
    private sealed record ResolvedStyle(ArchitectureV6StyleRule Rule, bool IsFallback);
}
