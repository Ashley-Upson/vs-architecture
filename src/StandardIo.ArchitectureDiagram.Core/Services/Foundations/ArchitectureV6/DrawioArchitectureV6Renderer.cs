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
            return Page(root, new AbsoluteRectangle(0, 0, 1200, 900), diagnostics, diagram.Request.GenerationSettings.BackgroundColor);
        }

        var physicalNodes = diagram.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var nodeCells = new Dictionary<string, string>(StringComparer.Ordinal);
        var projectCells = new Dictionary<string, string>(StringComparer.Ordinal);
        var emittedNodeBounds = new Dictionary<string, AbsoluteRectangle>(StringComparer.Ordinal);
        var emittedCellIds = new HashSet<string>(StringComparer.Ordinal) { "0", "1" };
        var minimumX = new[] { geometry.AbsoluteDiagramBounds.X }
            .Concat(geometry.Projects.Select(project => project.AbsoluteBounds.X))
            .Concat(geometry.Nodes.Select(node => node.AbsoluteBounds.X))
            .Concat(geometry.Routes.SelectMany(route => (route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Select(point => point.Point.X)))
            .DefaultIfEmpty(0).Min();
        var minimumY = new[] { geometry.AbsoluteDiagramBounds.Y }
            .Concat(geometry.Projects.Select(project => project.AbsoluteBounds.Y))
            .Concat(geometry.Nodes.Select(node => node.AbsoluteBounds.Y))
            .Concat(geometry.Routes.SelectMany(route => (route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Select(point => point.Point.Y)))
            .DefaultIfEmpty(0).Min();
        var offsetX = minimumX < 0 ? -minimumX : 0;
        var offsetY = minimumY < 0 ? -minimumY : 0;

        foreach (var project in geometry.Projects.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            if (!diagram.Request.ProjectPlacement.ShowProjectContainers) continue;
            var id = CellId("project", project.ProjectId);
            if (!emittedCellIds.Add(id))
            {
                diagnostics.Add(new DiagramDiagnostic("V6RendererIdCollision", "A generated project cell ID collided with an existing Draw.io cell ID.", id));
                continue;
            }
            projectCells[project.ProjectId] = id;
            root.Add(Vertex(id, project.ProjectId, Style(diagram.Request.ProjectContainerStyle ?? DefaultProjectStyle()), "1",
                project.AbsoluteBounds.X + offsetX, project.AbsoluteBounds.Y + offsetY, project.AbsoluteBounds.Width, project.AbsoluteBounds.Height,
                new Dictionary<string, string> { ["projectId"] = project.ProjectId, ["architectureRole"] = "project-container" }));
        }

        foreach (var node in geometry.Nodes.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
        {
            var cellId = CellId("node", node.PhysicalNodeId);
            if (!emittedCellIds.Add(cellId))
            {
                diagnostics.Add(new DiagramDiagnostic("V6RendererIdCollision", "A generated node cell ID collided with an existing Draw.io cell ID.", cellId));
                continue;
            }
            nodeCells[node.PhysicalNodeId] = cellId;
            physicalNodes.TryGetValue(node.PhysicalNodeId, out var physicalNode);
            var parent = node.ProjectId is not null && projectCells.TryGetValue(node.ProjectId, out var projectCell)
                ? projectCell : "1";
            var bounds = node.ProjectId is not null && geometry.Projects.FirstOrDefault(item => item.ProjectId == node.ProjectId) is { } project
                ? new AbsoluteRectangle(node.AbsoluteBounds.X - project.AbsoluteBounds.X, node.AbsoluteBounds.Y - project.AbsoluteBounds.Y,
                    node.AbsoluteBounds.Width, node.AbsoluteBounds.Height)
                : new AbsoluteRectangle(node.AbsoluteBounds.X + offsetX, node.AbsoluteBounds.Y + offsetY, node.AbsoluteBounds.Width, node.AbsoluteBounds.Height);
            var reconstructedBounds = node.ProjectId is not null && geometry.Projects.FirstOrDefault(item => item.ProjectId == node.ProjectId) is { } owningProject
                ? new AbsoluteRectangle(bounds.X + owningProject.AbsoluteBounds.X + offsetX, bounds.Y + owningProject.AbsoluteBounds.Y + offsetY,
                    bounds.Width, bounds.Height)
                : bounds;
            emittedNodeBounds[node.PhysicalNodeId] = reconstructedBounds;
            var styleRule = physicalNode?.ResolvedStyle;
            if (styleRule is null)
                diagnostics.Add(new DiagramDiagnostic("V6UnresolvedNodeStyle", "A physical node reached the renderer without a planner-resolved style.", node.PhysicalNodeId));
            var metadata = new Dictionary<string, string>
            {
                ["physicalNodeId"] = node.PhysicalNodeId,
                ["semanticNodeId"] = node.SemanticNodeId,
                ["projectionMode"] = node.ProjectionMode.ToString(),
                ["isExternal"] = node.IsExternal ? "1" : "0",
                ["isStandalone"] = node.IsStandalone ? "1" : "0",
                ["displayLabel"] = physicalNode?.DisplayLabel ?? physicalNode?.SemanticName ?? node.SemanticNodeId,
                ["resolvedStyleRule"] = styleRule?.Match ?? "<unresolved>",
                ["styleFallback"] = "0"
            };
            if (!string.IsNullOrWhiteSpace(node.PositionalOwnerId)) metadata["positionalOwnerId"] = node.PositionalOwnerId!;
            if (physicalNode?.DuplicationProvenance is { } provenance)
            {
                metadata["duplicationSemanticNodeId"] = provenance.SemanticNodeId;
                metadata["duplicationReason"] = provenance.Reason;
                if (provenance.ParentPhysicalNodeId is not null) metadata["duplicationParentPhysicalNodeId"] = provenance.ParentPhysicalNodeId;
            }
            root.Add(Vertex(cellId, physicalNode?.DisplayLabel ?? physicalNode?.SemanticName ?? node.SemanticNodeId,
                styleRule is null ? string.Empty : Style(styleRule), parent, bounds.X, bounds.Y, bounds.Width, bounds.Height, metadata));
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
            var edgeId = CellId("edge", link.PhysicalLinkId);
            if (!emittedCellIds.Add(edgeId))
            {
                diagnostics.Add(new DiagramDiagnostic("V6RendererIdCollision", "A generated edge cell ID collided with an existing Draw.io cell ID.", edgeId));
                continue;
            }
            var connector = link.ResolvedStyle;
            if (connector is null)
                diagnostics.Add(new DiagramDiagnostic("V6ConnectorStyleFidelityFailure", "A physical relationship reached the renderer without a planner-resolved connector style; no Draw.io connector fallback was applied.", link.PhysicalLinkId));
            physicalNodes.TryGetValue(link.DestinationPhysicalNodeId, out var targetNode);
            var sourceTerminal = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId &&
                item.PhysicalNodeId == link.SourcePhysicalNodeId && item.Side == GridSide.Bottom);
            var targetTerminal = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId &&
                item.PhysicalNodeId == link.DestinationPhysicalNodeId && item.Side == GridSide.Top);
            var sourceGeometry = geometry.Nodes.FirstOrDefault(item => item.PhysicalNodeId == link.SourcePhysicalNodeId);
            var targetGeometry = geometry.Nodes.FirstOrDefault(item => item.PhysicalNodeId == link.DestinationPhysicalNodeId);
            var edge = Edge(link, route, source, target, connector,
                sourceGeometry, targetGeometry, sourceTerminal, targetTerminal,
                link.ResolvedStyleSource, offsetX, offsetY);
            ValidateConnectorStyle(link, edge, diagnostics);
            ValidateEmittedEdgeGeometry(link, route, edge, emittedNodeBounds, sourceTerminal, targetTerminal, offsetX, offsetY, diagnostics);
            root.Add(edge);
            emittedEdges++;
        }

        diagnostics.Add(new DiagramDiagnostic("V6PhysicalNodesEmitted", $"Emitted {geometry.Nodes.Count} physical node vertices.", null));
        diagnostics.Add(new DiagramDiagnostic("V6RelationshipsEmitted", $"Emitted {emittedEdges} physical relationship edges.", null));
        if (nodeCells.Count != geometry.Nodes.Count || emittedEdges != diagram.PhysicalLinks.Count)
            diagnostics.Add(new DiagramDiagnostic("V6RendererAccountingFailure",
                $"Renderer accounting mismatch: plannedNodes={geometry.Nodes.Count}, emittedNodes={nodeCells.Count}, plannedLinks={diagram.PhysicalLinks.Count}, emittedLinks={emittedEdges}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6InvalidRoutesEmitted", $"Emitted {geometry.Routes.Count(route => route.IsInvalid)} invalid routes with retained geometry.", null));
        diagnostics.Add(new DiagramDiagnostic("V6NodeStyleSummary", $"Resolved node styles: {string.Join(", ", diagram.PhysicalNodes.GroupBy(node => node.ResolvedStyle?.Match ?? "<fallback>").OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key + "=" + group.Count()))}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6LinkStyleSummary", $"Resolved relationship style is carried on all {diagram.PhysicalLinks.Count} physical links. Sources: {string.Join(", ", diagram.PhysicalLinks.GroupBy(link => link.ResolvedStyleSource).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key + "=" + group.Count()))}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6LogicalPlacementComplete", "The renderer consumed the planner's completed logical and physical geometry.", null));
        diagnostics.Add(new DiagramDiagnostic("V6RendererMechanicalProjection", "Draw.io geometry was projected from the physical scene without placement or routing decisions.", null));
        return Page(root, geometry.AbsoluteDiagramBounds, diagnostics, diagram.Request.GenerationSettings.BackgroundColor);
    }

    private static DrawioPage Page(XElement root, AbsoluteRectangle bounds, IReadOnlyList<DiagramDiagnostic> diagnostics, string backgroundColor)
    {
        // Draw.io accepts scene coordinates outside the page origin. The page
        // extent is the scene extent, not the right/bottom coordinate relative
        // to an assumed zero origin.
        var width = Math.Max(1200, bounds.Width);
        var height = Math.Max(900, bounds.Height);
        var graph = new XElement("mxGraphModel",
            new XAttribute("dx", Math.Min(width, 1200)), new XAttribute("dy", Math.Min(height, 900)),
            new XAttribute("grid", "0"), new XAttribute("gridSize", "10"), new XAttribute("guides", "1"),
            new XAttribute("tooltips", "1"), new XAttribute("connect", "1"), new XAttribute("arrows", "1"),
            new XAttribute("fold", "1"), new XAttribute("page", "0"), new XAttribute("pageScale", "1"),
            // Keep configured connector and node colours literal. Draw.io's
            // automatic/simple adaptive-colour modes can remap black/white
            // while leaving the stored cell style and tooltip unchanged.
            new XAttribute("adaptiveColors", "none"),
            new XAttribute("background", string.IsNullOrWhiteSpace(backgroundColor) ? "#111111" : backgroundColor),
            new XAttribute("pageWidth", width), new XAttribute("pageHeight", height), root);
        return new DrawioPage("Architecture", "architecture", graph, diagnostics);
    }

    private static XElement Edge(PlannedPhysicalLink link, PlannedPhysicalRoute? route, string source, string target,
        ArchitectureV6ConnectorStyle? connector,
        PlannedPhysicalNodeGeometry? sourceGeometry, PlannedPhysicalNodeGeometry? targetGeometry,
        PlannedPhysicalTerminal? sourceTerminal, PlannedPhysicalTerminal? targetTerminal, string resolvedStyleSource,
        int offsetX, int offsetY)
    {
        var rawPoints = (route?.ReducedPoints ?? route?.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
            .Where(point => point.Point != sourceTerminal?.Point && point.Point != targetTerminal?.Point)
            .Aggregate(new List<PlannedPhysicalRoutePoint>(), (items, point) =>
            {
                if (items.Count == 0 || items[items.Count - 1].Point != point.Point) items.Add(point);
                return items;
            });
        var points = rawPoints
            .Select(point => new XElement("mxPoint", new XAttribute("x", (point.Point.X + offsetX).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("y", (point.Point.Y + offsetY).ToString(CultureInfo.InvariantCulture))))
            .ToArray();
        var style = connector;
        var styleText = style is null ? string.Empty : ConnectorStyleText(style);
        if (style is not null && sourceGeometry is not null && sourceTerminal is not null)
            styleText += $"exitX={Ratio(sourceTerminal.Point.X + offsetX, sourceGeometry.AbsoluteBounds.X + offsetX, sourceGeometry.AbsoluteBounds.Width)};exitY=1;";
        if (style is not null && targetGeometry is not null && targetTerminal is not null)
            styleText += $"entryX={Ratio(targetTerminal.Point.X + offsetX, targetGeometry.AbsoluteBounds.X + offsetX, targetGeometry.AbsoluteBounds.Width)};entryY=0;";
        if (style is not null && style.Dashed) styleText += $"dashed=1;dashPattern={style.DashPattern ?? "3 3"};";
        if (style is not null && !style.ShowLabels) styleText += "labelPosition=none;";
        var attributes = new Dictionary<string, string>
        {
            ["id"] = CellId("edge", link.PhysicalLinkId), ["physicalLinkId"] = link.PhysicalLinkId,
            ["semanticLinkId"] = link.SemanticLinkId, ["sourcePhysicalNodeId"] = link.SourcePhysicalNodeId,
            ["targetPhysicalNodeId"] = link.DestinationPhysicalNodeId, ["edge"] = "1", ["parent"] = "1",
            ["source"] = source, ["target"] = target, ["style"] = styleText,
            ["resolvedStyleSource"] = resolvedStyleSource,
            ["resolvedStrokeColor"] = style?.StrokeColor ?? string.Empty,
            ["resolvedStrokeWidth"] = style?.StrokeWidth.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["resolvedOpacity"] = style?.Opacity.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["resolvedRounded"] = style?.Rounded == true ? "1" : "0",
            ["resolvedDashed"] = style?.Dashed == true ? "1" : "0",
            ["resolvedDashPattern"] = style?.DashPattern ?? string.Empty,
            ["resolvedStartArrow"] = style?.StartArrow ?? string.Empty,
            ["resolvedEndArrow"] = style?.EndArrow ?? string.Empty,
            ["resolvedArrowSize"] = style?.ArrowSize.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["resolvedStartFill"] = style?.StartFill == true ? "1" : "0",
            ["resolvedEndFill"] = style?.EndFill == true ? "1" : "0",
            ["resolvedFontColor"] = style?.FontColor ?? string.Empty,
            ["resolvedShowLabels"] = style?.ShowLabels == true ? "1" : "0",
            ["resolvedExtraStyle"] = style?.ExtraStyle ?? string.Empty
        };
        if (sourceTerminal is not null) attributes["sourceTerminalId"] = sourceTerminal.TerminalId;
        if (targetTerminal is not null) attributes["targetTerminalId"] = targetTerminal.TerminalId;
        if (style?.ShowLabels == true && !string.IsNullOrWhiteSpace(link.DisplayLabel)) attributes["value"] = link.DisplayLabel!;
        if (route?.IsInvalid == true) attributes["invalidRoute"] = "1";
        if (route is not null) attributes["topologyFamily"] = route.TopologyFamily.ToString();
        return new XElement("mxCell", attributes.Select(item => new XAttribute(item.Key, item.Value)),
            new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"),
                new XElement("Array", new XAttribute("as", "points"), points)));
    }

    private static string Ratio(int x, int left, int width) =>
        Math.Max(0, Math.Min(1, (x - left) / (double)Math.Max(1, width))).ToString("0.###############", CultureInfo.InvariantCulture);

    private static void ValidateEmittedEdgeGeometry(PlannedPhysicalLink link, PlannedPhysicalRoute? route,
        XElement edge, IReadOnlyDictionary<string, AbsoluteRectangle> emittedNodeBounds,
        PlannedPhysicalTerminal? sourceTerminal, PlannedPhysicalTerminal? targetTerminal, int offsetX, int offsetY,
        ICollection<DiagramDiagnostic> diagnostics)
    {
        if (route is null || !emittedNodeBounds.TryGetValue(link.SourcePhysicalNodeId, out var sourceBounds) ||
            !emittedNodeBounds.TryGetValue(link.DestinationPhysicalNodeId, out var destinationBounds) ||
            sourceTerminal is null || targetTerminal is null)
            return;

        var style = ParseStyle((string?)edge.Attribute("style"));
        var source = new EmittedPoint(
            sourceBounds.X + sourceBounds.Width * ReadRatio(style, "exitX"),
            sourceBounds.Y + sourceBounds.Height);
        var destination = new EmittedPoint(
            destinationBounds.X + destinationBounds.Width * ReadRatio(style, "entryX"),
            destinationBounds.Y);
        var waypoints = edge.Element("mxGeometry")?.Element("Array")?.Elements("mxPoint")
            .Select(point => new EmittedPoint(
                ReadDouble(point, "x") + offsetX,
                ReadDouble(point, "y") + offsetY))
            .ToArray() ?? Array.Empty<EmittedPoint>();
        var emitted = new[] { source }.Concat(waypoints).Concat(new[] { destination }).ToArray();
        for (var index = 1; index < emitted.Length; index++)
        {
            if (!Orthogonal(emitted[index - 1], emitted[index]))
                diagnostics.Add(new DiagramDiagnostic("FinalRenderedDiagonal",
                    $"Emitted Draw.io geometry contains a diagonal segment at index {index - 1}: {emitted[index - 1]} -> {emitted[index]}.", link.PhysicalLinkId));
        }

        var expectedWaypoints = (route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
            .Where(point => point.Point != sourceTerminal.Point && point.Point != targetTerminal.Point)
            .Aggregate(new List<EmittedPoint>(), (points, point) =>
            {
                var value = new EmittedPoint(point.Point.X + offsetX, point.Point.Y + offsetY);
                if (points.Count == 0 || points[points.Count - 1] != value) points.Add(value);
                return points;
            });
        var expected = new[] { new EmittedPoint(sourceTerminal.Point.X + offsetX, sourceTerminal.Point.Y + offsetY) }
            .Concat(expectedWaypoints)
            .Concat(new[] { new EmittedPoint(targetTerminal.Point.X + offsetX, targetTerminal.Point.Y + offsetY) })
            .ToArray();
        var geometryMismatch = expected.Length != emitted.Length;
        var comparedCount = Math.Min(expected.Length, emitted.Length);
        for (var index = 0; index < comparedCount; index++)
            geometryMismatch |= Math.Abs(expected[index].X - emitted[index].X) > 0.01 || Math.Abs(expected[index].Y - emitted[index].Y) > 0.01;
        if (geometryMismatch)
            diagnostics.Add(new DiagramDiagnostic("RendererRouteGeometryMismatch",
                $"Reconstructed emitted geometry does not match the planned route polyline. plannedPoints={expected.Length}; emittedPoints={emitted.Length}.", link.PhysicalLinkId));
    }

    private static Dictionary<string, string> ParseStyle(string? text) => (text ?? string.Empty)
        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(token => token.Split(new[] { '=' }, 2))
        .Where(parts => parts.Length == 2)
        .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.OrdinalIgnoreCase);

    private static double ReadRatio(IReadOnlyDictionary<string, string> style, string key) =>
        style.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            ? Math.Max(0, Math.Min(1, ratio)) : 0.5;

    private static double ReadDouble(XElement element, string name) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static bool Orthogonal(EmittedPoint left, EmittedPoint right) =>
        Math.Abs(left.X - right.X) <= 0.01 || Math.Abs(left.Y - right.Y) <= 0.01;

    private readonly record struct EmittedPoint(double X, double Y);

    private static void ValidateConnectorStyle(PlannedPhysicalLink link, XElement edge, ICollection<DiagramDiagnostic> diagnostics)
    {
        if (link.ResolvedStyle is null) return;
        var tokens = ((string?)edge.Attribute("style") ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Split(new[] { '=' }, 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(parts => parts[1]).ToArray(), StringComparer.OrdinalIgnoreCase);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["strokeColor"] = link.ResolvedStyle.StrokeColor,
            ["strokeWidth"] = link.ResolvedStyle.StrokeWidth.ToString(CultureInfo.InvariantCulture),
            ["opacity"] = link.ResolvedStyle.Opacity.ToString(CultureInfo.InvariantCulture),
            ["endArrow"] = link.ResolvedStyle.EndArrow
        };
        foreach (var property in expected)
        {
            if (!tokens.TryGetValue(property.Key, out var values) || values.Length != 1 || !string.Equals(values[0], property.Value, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new DiagramDiagnostic("V6ConnectorStyleFidelityFailure",
                    $"Emitted connector style does not preserve planner property {property.Key}={property.Value}.", link.PhysicalLinkId));
                return;
            }
        }
    }

    private static string ConnectorStyleText(ArchitectureV6ConnectorStyle style)
    {
        // Extra connector tokens are deliberately emitted before structured
        // planner-owned properties. This preserves harmless custom tokens but
        // makes the structured values the single effective source of truth.
        var authoritative = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rounded", "startArrow", "endArrow", "startFill", "endFill", "startSize", "endSize",
            "strokeColor", "strokeWidth", "opacity", "fontColor", "dashed", "dashPattern", "labelPosition"
        };
        var extra = (style.ExtraStyle ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .Where(token => !authoritative.Contains(token.Split(new[] { '=' }, 2)[0].Trim()))
            .ToArray();
        var tokens = new List<string>(extra)
        {
            "edgeStyle=none", "orthogonal=0", "curved=0", $"rounded={(style.Rounded ? 1 : 0)}",
            $"startArrow={style.StartArrow}", $"endArrow={style.EndArrow}", $"startFill={(style.StartFill ? 1 : 0)}",
            $"endFill={(style.EndFill ? 1 : 0)}", $"startSize={style.ArrowSize}", $"endSize={style.ArrowSize}",
            $"strokeColor={style.StrokeColor}", $"strokeWidth={style.StrokeWidth}", $"opacity={style.Opacity}",
            $"fontColor={style.FontColor}", "html=1"
        };
        if (style.Dashed)
        {
            tokens.Add("dashed=1");
            tokens.Add($"dashPattern={style.DashPattern ?? "3 3"}");
        }
        return string.Join(";", tokens) + ";";
    }

    private static ArchitectureV6StyleRule DefaultProjectStyle() => new("<project>", "#323a40", "#263238", "#ffffff", "swimlane", true, "swimlaneLine=0;startSize=34;horizontal=1;opacity=88;");

    private static string Style(ArchitectureV6StyleRule rule)
    {
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
