using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class DrawioArchitectureV6Renderer : IArchitectureDiagramRenderer<DrawioPage>
{
    public DrawioPage Render(PlannedArchitectureDiagram diagram, ArchitectureRenderRequest request)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var scene = diagram.PhysicalScene;
        var geometry = scene?.Geometry;
        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var diagnostics = diagram.Diagnostics.Findings
            .Select(finding => new DiagramDiagnostic(finding.Code, finding.Message, finding.SubjectId))
            .ToList();

        if (scene is null || geometry is null)
        {
            diagnostics.Add(new DiagramDiagnostic("V6PhysicalSceneUnavailable", "No physical scene was available for Draw.io projection.", null));
            return Page(root, new AbsoluteRectangle(0, 0, 1200, 900), diagnostics);
        }

        var physicalNodes = diagram.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var nodeCells = new Dictionary<string, string>(StringComparer.Ordinal);
        var projectCells = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var project in geometry.Projects.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            if (!diagram.Request.ProjectPlacement.ShowProjectContainers) continue;
            var id = CellId("project", project.ProjectId);
            projectCells[project.ProjectId] = id;
            root.Add(Vertex(id, project.ProjectId, Style(diagram.Request.ProjectContainerStyle ?? DefaultProjectStyle(), DefaultProjectStyle()), "1",
                project.AbsoluteBounds.X, project.AbsoluteBounds.Y, project.AbsoluteBounds.Width, project.AbsoluteBounds.Height,
                new Dictionary<string, string> { ["projectId"] = project.ProjectId, ["architectureRole"] = "project-container" }));
        }

        foreach (var node in geometry.Nodes.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
        {
            var cellId = CellId("node", node.PhysicalNodeId);
            nodeCells[node.PhysicalNodeId] = cellId;
            physicalNodes.TryGetValue(node.PhysicalNodeId, out var physicalNode);
            var parent = node.ProjectId is not null && projectCells.TryGetValue(node.ProjectId, out var projectCell)
                ? projectCell : "1";
            var bounds = node.ProjectId is not null && geometry.Projects.FirstOrDefault(item => item.ProjectId == node.ProjectId) is { } project
                ? new AbsoluteRectangle(node.AbsoluteBounds.X - project.AbsoluteBounds.X, node.AbsoluteBounds.Y - project.AbsoluteBounds.Y,
                    node.AbsoluteBounds.Width, node.AbsoluteBounds.Height)
                : node.AbsoluteBounds;
            var styleRule = physicalNode?.ResolvedStyle ?? ResolveStyle(diagram, physicalNode, node);
            var metadata = new Dictionary<string, string>
            {
                ["physicalNodeId"] = node.PhysicalNodeId,
                ["semanticNodeId"] = node.SemanticNodeId,
                ["projectionMode"] = node.ProjectionMode.ToString(),
                ["isExternal"] = node.IsExternal ? "1" : "0",
                ["isStandalone"] = node.IsStandalone ? "1" : "0",
                ["resolvedStyleRule"] = styleRule.Match,
                ["styleFallback"] = string.Equals(styleRule.Match, "<fallback>", StringComparison.OrdinalIgnoreCase) ? "1" : "0"
            };
            if (!string.IsNullOrWhiteSpace(node.PositionalOwnerId)) metadata["positionalOwnerId"] = node.PositionalOwnerId!;
            if (physicalNode?.DuplicationProvenance is { } provenance)
            {
                metadata["duplicationSemanticNodeId"] = provenance.SemanticNodeId;
                metadata["duplicationReason"] = provenance.Reason;
                if (provenance.ParentPhysicalNodeId is not null) metadata["duplicationParentPhysicalNodeId"] = provenance.ParentPhysicalNodeId;
            }
            root.Add(Vertex(cellId, physicalNode?.SemanticFullName ?? physicalNode?.SemanticName ?? node.SemanticNodeId,
                Style(styleRule, DefaultNodeStyle(node.IsExternal)), parent, bounds.X, bounds.Y, bounds.Width, bounds.Height, metadata));
        }

        var emittedEdges = 0;
        foreach (var link in diagram.PhysicalLinks.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            if (!nodeCells.TryGetValue(link.SourcePhysicalNodeId, out var source) ||
                !nodeCells.TryGetValue(link.DestinationPhysicalNodeId, out var target))
            {
                diagnostics.Add(new DiagramDiagnostic("V6RelationshipOmitted", "A physical relationship referenced a node that was not emitted.", link.PhysicalLinkId));
                continue;
            }
            var route = geometry.Routes.FirstOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId);
            var connector = link.ResolvedStyle ?? diagram.Request.ConnectorStyle;
            physicalNodes.TryGetValue(link.DestinationPhysicalNodeId, out var targetNode);
            root.Add(Edge(link, route, source, target, ConnectorForTarget(connector, targetNode),
                targetNode?.ResolvedStyle?.FillColor is { Length: > 0 } ? "target-node-background" : link.ResolvedStyleSource));
            emittedEdges++;
        }

        diagnostics.Add(new DiagramDiagnostic("V6PhysicalNodesEmitted", $"Emitted {geometry.Nodes.Count} physical node vertices.", null));
        diagnostics.Add(new DiagramDiagnostic("V6RelationshipsEmitted", $"Emitted {emittedEdges} physical relationship edges.", null));
        diagnostics.Add(new DiagramDiagnostic("V6InvalidRoutesEmitted", $"Emitted {geometry.Routes.Count(route => route.IsInvalid)} invalid routes with retained geometry.", null));
        diagnostics.Add(new DiagramDiagnostic("V6NodeStyleSummary", $"Resolved node styles: {string.Join(", ", diagram.PhysicalNodes.GroupBy(node => node.ResolvedStyle?.Match ?? "<fallback>").OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key + "=" + group.Count()))}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6LinkStyleSummary", $"Resolved relationship style is carried on all {diagram.PhysicalLinks.Count} physical links. Sources: {string.Join(", ", diagram.PhysicalLinks.GroupBy(link => link.ResolvedStyleSource).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key + "=" + group.Count()))}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6LogicalPlacementComplete", "The renderer consumed the planner's completed logical and physical geometry.", null));
        diagnostics.Add(new DiagramDiagnostic("V6RendererMechanicalProjection", "Draw.io geometry was projected from the physical scene without placement or routing decisions.", null));
        return Page(root, geometry.AbsoluteDiagramBounds, diagnostics);
    }

    private static DrawioPage Page(XElement root, AbsoluteRectangle bounds, IReadOnlyList<DiagramDiagnostic> diagnostics)
    {
        var width = Math.Max(1200, bounds.Width + Math.Max(0, bounds.X));
        var height = Math.Max(900, bounds.Height + Math.Max(0, bounds.Y));
        var graph = new XElement("mxGraphModel",
            new XAttribute("dx", Math.Min(width, 1200)), new XAttribute("dy", Math.Min(height, 900)),
            new XAttribute("grid", "0"), new XAttribute("gridSize", "10"), new XAttribute("guides", "1"),
            new XAttribute("tooltips", "1"), new XAttribute("connect", "1"), new XAttribute("arrows", "1"),
            new XAttribute("fold", "1"), new XAttribute("page", "0"), new XAttribute("pageScale", "1"),
            new XAttribute("pageWidth", width), new XAttribute("pageHeight", height), root);
        return new DrawioPage("Architecture", "architecture", graph, diagnostics);
    }

    private static XElement Edge(PlannedPhysicalLink link, PlannedPhysicalRoute? route, string source, string target,
        ArchitectureV6ConnectorStyle? connector, string resolvedStyleSource)
    {
        var points = (route?.ReducedPoints ?? route?.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
            .Select(point => new XElement("mxPoint", new XAttribute("x", point.Point.X.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("y", point.Point.Y.ToString(CultureInfo.InvariantCulture))))
            .ToArray();
        var style = connector ?? new ArchitectureV6ConnectorStyle("#6c8ebf", 1, false);
        var styleText = $"edgeStyle=none;orthogonal=0;curved=0;rounded={(style.Rounded ? 1 : 0)};startArrow={style.StartArrow};endArrow={style.EndArrow};startFill={(style.StartFill ? 1 : 0)};endFill={(style.EndFill ? 1 : 0)};startSize={style.ArrowSize};endSize={style.ArrowSize};strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};opacity={style.Opacity};fontColor={style.FontColor};html=1;";
        if (style.Dashed) styleText += $"dashed=1;dashPattern={style.DashPattern ?? "3 3"};";
        if (!style.ShowLabels) styleText += "labelPosition=none;";
        if (!string.IsNullOrWhiteSpace(style.ExtraStyle)) styleText += style.ExtraStyle!.TrimEnd(';') + ";";
        var attributes = new Dictionary<string, string>
        {
            ["id"] = CellId("edge", link.PhysicalLinkId), ["physicalLinkId"] = link.PhysicalLinkId,
            ["semanticLinkId"] = link.SemanticLinkId, ["sourcePhysicalNodeId"] = link.SourcePhysicalNodeId,
            ["targetPhysicalNodeId"] = link.DestinationPhysicalNodeId, ["edge"] = "1", ["parent"] = "1",
            ["source"] = source, ["target"] = target, ["style"] = styleText,
            ["resolvedStyleSource"] = resolvedStyleSource,
            ["resolvedStrokeColor"] = style.StrokeColor,
            ["resolvedStrokeWidth"] = style.StrokeWidth.ToString(CultureInfo.InvariantCulture)
        };
        if (style.ShowLabels && !string.IsNullOrWhiteSpace(link.DisplayLabel)) attributes["value"] = link.DisplayLabel!;
        if (route?.IsInvalid == true) attributes["invalidRoute"] = "1";
        if (route is not null) attributes["topologyFamily"] = route.TopologyFamily.ToString();
        return new XElement("mxCell", attributes.Select(item => new XAttribute(item.Key, item.Value)),
            new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"),
                new XElement("Array", new XAttribute("as", "points"), points)));
    }

    private static ArchitectureV6ConnectorStyle ConnectorForTarget(
        ArchitectureV6ConnectorStyle? connector,
        PlannedPhysicalNode? targetNode)
    {
        var style = connector ?? new ArchitectureV6ConnectorStyle("#6c8ebf", 1, false);
        var targetFill = targetNode?.ResolvedStyle?.FillColor;
        return string.IsNullOrWhiteSpace(targetFill)
            ? style
            : style with { StrokeColor = targetFill };
    }

    private static ArchitectureV6StyleRule ResolveStyle(PlannedArchitectureDiagram diagram, PlannedPhysicalNode? node, PlannedPhysicalNodeGeometry geometry)
    {
        if (geometry.IsExternal && diagram.Request.ExternalDependencyStyle is not null) return diagram.Request.ExternalDependencyStyle;
        var fullName = node?.SemanticFullName ?? string.Empty;
        var exact = diagram.Request.StyleOverridesWithValues?.FirstOrDefault(item => item.FullName == fullName);
        if (exact is not null) return exact.Style;
        return diagram.Request.StylePolicies?.FirstOrDefault(rule => GlobMatcher.IsMatch(node?.SemanticName ?? fullName, rule.Match) || GlobMatcher.IsMatch(fullName, rule.Match))
            ?? DefaultNodeStyle(geometry.IsExternal);
    }

    private static ArchitectureV6StyleRule DefaultNodeStyle(bool external) => external
        ? new ArchitectureV6StyleRule("<external>", "#f36c21", "#a43b08", "#111111", "rhombus", true, null)
        : new ArchitectureV6StyleRule("<fallback>", "#dae8fc", "#6c8ebf", "#111111", "rounded", true, null);

    private static ArchitectureV6StyleRule DefaultProjectStyle() => new("<project>", "#323a40", "#263238", "#ffffff", "swimlane", true, "swimlaneLine=0;startSize=34;horizontal=1;opacity=88;");

    private static string Style(ArchitectureV6StyleRule rule, ArchitectureV6StyleRule fallback)
    {
        rule ??= fallback;
        var shape = string.Equals(rule.Shape, "rounded", StringComparison.OrdinalIgnoreCase) ? "rounded=1" : "shape=" + rule.Shape;
        return $"{shape};whiteSpace=wrap;html=1;fillColor={rule.FillColor};strokeColor={rule.StrokeColor};fontColor={rule.FontColor};shadow={(rule.Shadow ? 1 : 0)};" + (rule.ExtraStyle ?? string.Empty);
    }

    private static XElement Vertex(string id, string value, string style, string parent, int x, int y, int width, int height, IReadOnlyDictionary<string, string> metadata)
    {
        var attributes = new List<XAttribute> { new("id", id), new("value", value ?? string.Empty), new("style", style), new("vertex", "1"), new("parent", parent) };
        attributes.AddRange(metadata.Select(item => new XAttribute(item.Key, item.Value)));
        return new XElement("mxCell", attributes, new XElement("mxGeometry", new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width), new XAttribute("height", height), new XAttribute("as", "geometry")));
    }

    private static string CellId(string kind, string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return "architecture_" + kind + "_" + string.Concat(hash.Take(12).Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
