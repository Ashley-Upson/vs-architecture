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
        var styleUsage = new Dictionary<string, (int Count, string Style)>(StringComparer.Ordinal);

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
            var styleKey = style.MatchedSelector ?? "<fallback>";
            styleUsage.TryGetValue(styleKey, out var usage);
            styleUsage[styleKey] = (usage.Count + 1, StyleString(style.Rule));
            var parent = diagram.Request.ProjectPlacement.ShowProjectContainers && node.ProjectId is not null
                ? ProjectCellId(node.ProjectId)
                : "1";
            var rectangle = parent == "1"
                ? nodeGeometry.AbsoluteBounds
                : new AbsoluteRectangle(nodeGeometry.RelativeBounds.X, nodeGeometry.RelativeBounds.Y, nodeGeometry.RelativeBounds.Width, nodeGeometry.RelativeBounds.Height);
            root.Add(NodeCell(node, nodeGeometry, parent, rectangle, style.Rule, style.MatchedSelector ?? "<fallback>", diagram.Request.RoutePlanning.ExternalDependencyTag, diagram.NodeMetadata.FirstOrDefault(item => item.PhysicalNodeId == node.PhysicalNodeId)));
            emitted.Add(node.PhysicalNodeId);
        }

        foreach (var node in diagram.PhysicalNodes)
            if (!emitted.Contains(node.PhysicalNodeId) && !geometryById.ContainsKey(node.PhysicalNodeId))
                diagnostics.Add(new DiagramDiagnostic("V6SkippedNode", "A planned physical node was not emitted.", node.SemanticNodeId));

        var emittedEdges = 0;
        foreach (var route in geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>())
        {
            var link = diagram.PhysicalLinks.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId);
            if (link is null) continue;
            root.Add(EdgeCell(link, route, diagram.Request));
            emittedEdges++;
        }

        var expected = diagram.PhysicalNodes.Count;
        if (emitted.Count != expected)
            diagnostics.Add(new DiagramDiagnostic("V6VertexCountMismatch", $"Expected {expected} physical node vertices but emitted {emitted.Count}.", null));
        diagnostics.Add(new DiagramDiagnostic("V6VertexCount", emitted.Count.ToString(CultureInfo.InvariantCulture), null));
        diagnostics.Add(new DiagramDiagnostic("V6SemanticLinkCount", diagram.Projection?.SemanticLinkToPhysicalLinkIds.Count.ToString(CultureInfo.InvariantCulture) ?? "0", null));
        diagnostics.Add(new DiagramDiagnostic("V6ArchitectureEdgeCount", emittedEdges.ToString(CultureInfo.InvariantCulture), null));
        diagnostics.Add(new DiagramDiagnostic("V6UnresolvedLinkCount", Math.Max(0, diagram.PhysicalLinks.Count - emittedEdges).ToString(CultureInfo.InvariantCulture), null));
        foreach (var routeGroup in (geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).GroupBy(route => route.TopologyFamily).OrderBy(group => group.Key))
            diagnostics.Add(new DiagramDiagnostic("V6RouteClassification", $"route={routeGroup.Key};count={routeGroup.Count()};bends={routeGroup.Sum(route => route.BendCount)}", null));
        diagnostics.Add(new DiagramDiagnostic("V6RouteLength", $"total={((geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).Sum(route => route.RouteLength))};average={((geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).Select(route => route.RouteLength).DefaultIfEmpty(0).Average()):0.##};max={((geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).Select(route => route.RouteLength).DefaultIfEmpty(0).Max())}", null));
        diagnostics.Add(new DiagramDiagnostic("V6RouteIntersections", $"nodes={(geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).Count(route => route.HasNodeIntersection)};shared={(geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).Count(route => route.HasSharedSegment)};crossings={(geometry?.Routes ?? Array.Empty<PlannedPhysicalRoute>()).Count(route => route.HasCrossing)}", null));
        diagnostics.Add(new DiagramDiagnostic("V6LinkStyleUsage", $"count={emittedEdges};style={(diagram.Request.ConnectorStyle is null ? "fallback" : "configured")}", null));
        diagnostics.Add(new DiagramDiagnostic("V6SkippedNodeCount", skippedCount.ToString(CultureInfo.InvariantCulture), null));
        diagnostics.Add(new DiagramDiagnostic("V6StyleFallbackCount", fallbackCount.ToString(CultureInfo.InvariantCulture), null));
        foreach (var usage in styleUsage.OrderBy(item => item.Key, StringComparer.Ordinal))
            diagnostics.Add(new DiagramDiagnostic("V6StyleRuleUsage", $"rule={usage.Key};count={usage.Value.Count};style={usage.Value.Style}", null));
        foreach (var rule in diagram.Request.StylePolicies ?? Array.Empty<ArchitectureV6StyleRule>())
            if (!styleUsage.ContainsKey(rule.Match))
                diagnostics.Add(new DiagramDiagnostic("V6UnmatchedStyleSelector", $"rule={rule.Match};style={StyleString(rule)}", null));
        foreach (var styleOverride in diagram.Request.StyleOverridesWithValues ?? Array.Empty<ArchitectureV6StyleOverride>())
            if (!diagram.PhysicalNodes.Any(node => string.Equals(node.SemanticFullName, styleOverride.FullName, StringComparison.Ordinal)))
                diagnostics.Add(new DiagramDiagnostic("V6UnusedStyleOverride", $"selector={styleOverride.FullName};style={StyleString(styleOverride.Style)}", null));
        if (geometry is not null)
            diagnostics.Add(new DiagramDiagnostic("V6OutputBounds", $"{geometry.AbsoluteDiagramBounds.Width}x{geometry.AbsoluteDiagramBounds.Height}", null));
        if (geometry is not null)
            AddGapDiagnostics(diagram, geometry, diagnostics);
        AddPlacementDiagnostics(diagram, diagnostics);

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

    private static XElement EdgeCell(PlannedPhysicalLink link, PlannedPhysicalRoute route, ArchitecturePlanningRequest request)
    {
        var connector = request.ConnectorStyle ?? new ArchitectureV6ConnectorStyle("#ffffff", 2, false);
        var style = $"edgeStyle=orthogonalEdgeStyle;orthogonal=1;orthogonalLoop=0;jettySize=0;rounded={(connector.Rounded ? "1" : "0")};html=1;strokeColor={connector.StrokeColor};strokeWidth={connector.StrokeWidth};dashed={(connector.Dashed ? "1" : "0")};" +
            (string.IsNullOrWhiteSpace(connector.DashPattern) ? string.Empty : $"dashPattern={connector.DashPattern};") +
            $"startArrow={connector.StartArrow};endArrow={connector.EndArrow};startFill=1;endFill=1;arrowSize={connector.ArrowSize};opacity={connector.Opacity};fontColor={connector.FontColor};exitX=0.5;exitY=1;entryX=0.5;entryY=0;exitPerimeter=1;entryPerimeter=1;";
        var waypoints = route.Segments
            .Take(Math.Max(0, route.Segments.Count - 1))
            .Select(segment => segment.End)
            .Select(point => new XElement("mxPoint",
                new XAttribute("x", point.X.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("y", point.Y.ToString(CultureInfo.InvariantCulture))));
        var geometry = new XElement("mxGeometry",
            new XAttribute("relative", "1"),
            new XAttribute("as", "geometry"),
            new XElement("Array", new XAttribute("as", "points"), waypoints));
        return new XElement("mxCell",
            new XAttribute("id", EdgeCellId(link.PhysicalLinkId)),
            new XAttribute("value", connector.ShowLabels ? link.DisplayLabel ?? string.Empty : string.Empty),
            new XAttribute("style", style),
            new XAttribute("edge", "1"), new XAttribute("parent", "1"),
            new XAttribute("source", CellId(link.SourcePhysicalNodeId)),
            new XAttribute("target", CellId(link.DestinationPhysicalNodeId)),
            new XAttribute("physicalLinkId", link.PhysicalLinkId),
            new XAttribute("semanticLinkId", link.SemanticLinkId),
            new XAttribute("relationshipKind", link.Kind ?? string.Empty),
            new XAttribute("sourceProjection", route.SourceProjection),
            new XAttribute("destinationProjection", route.DestinationProjection),
            new XAttribute("routeTopology", route.TopologyFamily.ToString()), geometry);
    }

    private static XElement NodeCell(
        PlannedPhysicalNode node,
        PlannedPhysicalNodeGeometry geometry,
        string parent,
        AbsoluteRectangle rectangle,
        ArchitectureV6StyleRule style,
        string matchedSelector,
        string externalTag,
        PhysicalNodePlacementMetadata? nodeMetadata)
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
            new XAttribute("matchedStyleRule", matchedSelector),
            new XElement("mxGeometry", new XAttribute("x", rectangle.X), new XAttribute("y", rectangle.Y),
                new XAttribute("width", rectangle.Width), new XAttribute("height", rectangle.Height), new XAttribute("as", "geometry")));
        var metadata = nodeMetadata;
        if (metadata is not null)
        {
            cell.Add(new XAttribute("semanticDepth", metadata.SemanticDepth));
            cell.Add(new XAttribute("logicalLayer", metadata.LogicalLayer));
            cell.Add(new XAttribute("roleSelector", metadata.RoleSelector));
            cell.Add(new XAttribute("roleBand", metadata.RoleBand));
            cell.Add(new XAttribute("ownershipGroup", metadata.OwnershipGroup));
            cell.Add(new XAttribute("siblingGroup", metadata.SiblingGroup));
            cell.Add(new XAttribute("horizontalSpacingPolicy", metadata.HorizontalSpacingPolicy));
            cell.Add(new XAttribute("verticalSpacingPolicy", metadata.VerticalSpacingPolicy));
            cell.Add(new XAttribute("physicalRow", metadata.PhysicalRow));
            cell.Add(new XAttribute("physicalColumn", metadata.PhysicalColumn));
            cell.Add(new XAttribute("treeRootId", metadata.TreeRootId));
            cell.Add(new XAttribute("parentSemanticId", metadata.ParentSemanticId ?? string.Empty));
            cell.Add(new XAttribute("subdepth", metadata.Subdepth));
            cell.Add(new XAttribute("configuredRoleOrder", metadata.ConfiguredRoleOrder));
            cell.Add(new XAttribute("siblingOrder", metadata.SiblingOrder));
            cell.Add(new XAttribute("branchOrder", metadata.BranchOrder));
            cell.Add(new XAttribute("placementGroup", metadata.PlacementGroup));
        }
        if (node.DuplicationProvenance is not null)
        {
            cell.Add(new XAttribute("duplicationSemanticNodeId", node.DuplicationProvenance.SemanticNodeId));
            cell.Add(new XAttribute("duplicationReason", node.DuplicationProvenance.Reason));
            cell.Add(new XAttribute("duplicationParentPhysicalNodeId", node.DuplicationProvenance.ParentPhysicalNodeId ?? string.Empty));
        }
        return cell;
    }

    private static void AddPlacementDiagnostics(PlannedArchitectureDiagram diagram, ICollection<DiagramDiagnostic> diagnostics)
    {
        foreach (var group in diagram.NodeMetadata.GroupBy(item => item.RoleSelector, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            diagnostics.Add(new DiagramDiagnostic("V6RoleBandUsage", $"role={group.Key};count={group.Count()};bands={string.Join(",", group.Select(item => item.RoleBand).Distinct().OrderBy(value => value).Select(value => value.ToString(CultureInfo.InvariantCulture)))}", null));
        foreach (var group in diagram.NodeMetadata.GroupBy(item => item.HorizontalSpacingPolicy, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            diagnostics.Add(new DiagramDiagnostic("V6SpacingPolicyUsage", $"axis=horizontal;policy={group.Key};count={group.Count()}", null));
        foreach (var group in diagram.NodeMetadata.GroupBy(item => item.VerticalSpacingPolicy, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            diagnostics.Add(new DiagramDiagnostic("V6SpacingPolicyUsage", $"axis=vertical;policy={group.Key};count={group.Count()}", null));
    }

    private static ResolvedStyle ResolveStyle(ArchitecturePlanningRequest request, PlannedPhysicalNode node)
    {
        if (node.IsExternal) return new ResolvedStyle(request.ExternalDependencyStyle ?? ExternalStyle, false, "<external>");
        var overrideStyle = request.StyleOverridesWithValues?.FirstOrDefault(item => string.Equals(item.FullName, node.SemanticFullName, StringComparison.Ordinal));
        if (overrideStyle is not null) return new ResolvedStyle(overrideStyle.Style, false, overrideStyle.FullName);
        var match = request.StylePolicies?.FirstOrDefault(rule => GlobMatcher.IsMatch(node.SemanticName, rule.Match) || GlobMatcher.IsMatch(node.SemanticFullName, rule.Match));
        return match is null ? new ResolvedStyle(FallbackStyle, true, null) : new ResolvedStyle(match, false, match.Match);
    }

    private static string StyleString(ArchitectureV6StyleRule style) =>
        $"shape={style.Shape};whiteSpace=wrap;html=1;rounded={(style.Shape == "rounded" ? "1" : "0")};shadow={(style.Shadow ? "1" : "0")};fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};{style.ExtraStyle}";

    private static void AddGapDiagnostics(PlannedArchitectureDiagram diagram, PlannedArchitectureGeometry geometry, ICollection<DiagramDiagnostic> diagnostics)
    {
        var placements = diagram.NodePlacements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        foreach (var group in geometry.Nodes.GroupBy(node => node.ProjectId, StringComparer.Ordinal))
        {
            var horizontal = group.OrderBy(node => node.AbsoluteBounds.X).ThenBy(node => node.AbsoluteBounds.Y).ToArray();
            for (var index = 1; index < horizontal.Length; index++)
            {
                var previous = horizontal[index - 1];
                var current = horizontal[index];
                if (previous.AbsoluteBounds.Y >= current.AbsoluteBounds.Y + current.AbsoluteBounds.Height || current.AbsoluteBounds.Y >= previous.AbsoluteBounds.Y + previous.AbsoluteBounds.Height)
                    continue;
                var renderedGap = current.AbsoluteBounds.X - (previous.AbsoluteBounds.X + previous.AbsoluteBounds.Width);
                var plannedGap = PlannedHorizontalGap(placements, previous.PhysicalNodeId, current.PhysicalNodeId, diagram.Request.NodePlacement.HorizontalSpacing);
                diagnostics.Add(new DiagramDiagnostic("V6NodeGap", $"from={previous.PhysicalNodeId};to={current.PhysicalNodeId};planned={plannedGap};rendered={renderedGap};policy=normal-horizontal", null));
            }
            var vertical = group.OrderBy(node => node.AbsoluteBounds.Y).ThenBy(node => node.AbsoluteBounds.X).ToArray();
            for (var index = 1; index < vertical.Length; index++)
            {
                var previous = vertical[index - 1];
                var current = vertical[index];
                if (previous.AbsoluteBounds.X >= current.AbsoluteBounds.X + current.AbsoluteBounds.Width || current.AbsoluteBounds.X >= previous.AbsoluteBounds.X + previous.AbsoluteBounds.Width)
                    continue;
                var renderedGap = current.AbsoluteBounds.Y - (previous.AbsoluteBounds.Y + previous.AbsoluteBounds.Height);
                diagnostics.Add(new DiagramDiagnostic("V6NodeGap", $"from={previous.PhysicalNodeId};to={current.PhysicalNodeId};planned=logical-row;rendered={renderedGap};policy=normal-vertical", null));
            }
        }
        var projects = geometry.Projects.OrderBy(project => project.AbsoluteBounds.X).ToArray();
        for (var index = 1; index < projects.Length; index++)
        {
            var previous = projects[index - 1];
            var current = projects[index];
            diagnostics.Add(new DiagramDiagnostic("V6ProjectGap", $"from={previous.ProjectId};to={current.ProjectId};rendered={current.AbsoluteBounds.X - (previous.AbsoluteBounds.X + previous.AbsoluteBounds.Width)};policy=project-section", null));
        }
    }

    private static int PlannedHorizontalGap(IReadOnlyDictionary<string, PlannedNodePlacement> placements, string leftId, string rightId, int cellWidth)
    {
        if (!placements.TryGetValue(leftId, out var left) || !placements.TryGetValue(rightId, out var right)) return 0;
        var leftMax = left.Footprint.Select(cell => ParseColumn(cell.ColumnId)).DefaultIfEmpty(0).Max();
        var rightMin = right.Footprint.Select(cell => ParseColumn(cell.ColumnId)).DefaultIfEmpty(0).Min();
        return Math.Max(0, rightMin - leftMax - 1) * Math.Max(1, cellWidth);
    }

    private static int ParseColumn(PlanningGridColumnId id) => int.Parse(id.Value.Substring(id.Value.LastIndexOf(':') + 1), CultureInfo.InvariantCulture);

    private static string CellId(string physicalNodeId) => StableId.From("architecture_v6_node", physicalNodeId);
    private static string EdgeCellId(string physicalLinkId) => StableId.From("architecture_v6_edge", physicalLinkId);
    private static string ProjectCellId(string projectId) => StableId.From("architecture_v6_project", projectId);
    private sealed record ResolvedStyle(ArchitectureV6StyleRule Rule, bool IsFallback, string? MatchedSelector);
}
