using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed class DiagramFileBuilder
    {
        private readonly DiagramSettings _settings;
        private readonly StyleResolver _styleResolver;

        public DiagramFileBuilder(DiagramSettings settings)
        {
            _settings = settings;
            _styleResolver = new StyleResolver(settings);
        }

        public string Build(RenderLayout layout, CoordinateOwnershipCompilation ownership)
        {
            XDocument document;
            using (PerformanceAudit.Measure(
                "DiagramFileBuilder XML construction",
                layout.Nodes.Count,
                layout.Links.Count,
                ownership.Segments.Count,
                layout.Nodes.Count + ownership.Anchors.Count + ownership.Segments.Count,
                layout.LayoutRevision.Value))
            {
                var page = new DrawioPage(
                    "Architecture", "architecture", BuildArchitecturePage(layout, ownership),
                    Array.Empty<DiagramDiagnostic>());
                var composed = new DrawioDocumentComposer().Compose(
                    new[] { page }, new DrawioDocumentSettings());
                document = XDocument.Parse(composed.Content);
                PerformanceAudit.Increment("final nodes", layout.Nodes.Count);
                PerformanceAudit.Increment("logical routes", layout.Links.Count);
                PerformanceAudit.Increment("physical edge segments", ownership.Segments.Count);
                PerformanceAudit.Increment("XML cells", document.Descendants("mxCell").Count());
                PerformanceAudit.Increment("waypoints", document.Descendants("mxPoint").Count());
            }

            using (PerformanceAudit.Measure("XML ToString/materialization"))
            {
                var content = document.ToString(SaveOptions.DisableFormatting);
                PerformanceAudit.Increment("document characters", content.Length);
                PerformanceAudit.Increment("document bytes", Encoding.UTF8.GetByteCount(content));
                return content;
            }
        }

        public XElement BuildArchitecturePage(RenderLayout layout, CoordinateOwnershipCompilation ownership)
        {
            if (layout is null) throw new ArgumentNullException(nameof(layout));
            if (ownership is null) throw new ArgumentNullException(nameof(ownership));
            return GraphModel(new ArchitectureGenerator(this).Generate(layout, ownership));
        }

        private XElement ArchitectureRoot(RenderLayout layout, CoordinateOwnershipCompilation ownership)
        {
            var root = new XElement("root",
                new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

            foreach (var project in layout.Graph.Projects)
            {
                if (_settings.ShowProjectContainers && layout.Projects.TryGetValue(project.Id, out var projectLayout))
                {
                    root.Add(Vertex(project.Id, project.Name, BuildNodeStyle(_settings.ProjectContainerStyle), "1", projectLayout.Rect));
                }

                foreach (var nodeLayout in layout.Nodes.Values
                    .Where(node => string.Equals(node.Node.ProjectId, project.Id, StringComparison.Ordinal))
                    .OrderBy(node => node.Node.Order))
                {
                    var rect = nodeLayout.Rect;
                    ProjectLayout? parentProjectLayout = null;
                    var hasProjectParent = _settings.ShowProjectContainers &&
                        layout.Projects.TryGetValue(project.Id, out parentProjectLayout);
                    var parent = hasProjectParent ? project.Id : "1";
                    if (hasProjectParent)
                    {
                        rect = rect with
                        {
                            X = rect.X - parentProjectLayout!.Rect.X,
                            Y = rect.Y - parentProjectLayout.Rect.Y
                        };
                    }

                    root.Add(Vertex(
                        nodeLayout.Node.Id,
                        nodeLayout.Node.IsExternal
                            ? $"{nodeLayout.Node.Tag}\n{nodeLayout.Node.Name}\n{nodeLayout.Node.FullName}"
                            : NodeLabel(nodeLayout.Node),
                        nodeLayout.Node.IsExternal
                            ? BuildNodeStyle(_settings.ExternalDependencyStyle)
                            : BuildNodeStyle(_styleResolver.Resolve(ToTypeNode(nodeLayout.Node))),
                        parent,
                        rect));
                }
            }

            foreach (var nodeLayout in layout.Nodes.Values
                .Where(node => node.Node.IsExternal &&
                    (!_settings.ShowProjectContainers || node.Node.ProjectId is null || !layout.Projects.ContainsKey(node.Node.ProjectId)))
                .OrderBy(node => node.Node.Order))
            {
                root.Add(Vertex(
                    nodeLayout.Node.Id,
                    $"{nodeLayout.Node.Tag}\n{nodeLayout.Node.Name}\n{nodeLayout.Node.FullName}",
                    BuildNodeStyle(_settings.ExternalDependencyStyle),
                    "1",
                    nodeLayout.Rect));
            }

            foreach (var anchor in ownership.Anchors
                .OrderBy(anchor => anchor.LogicalEdgeId, StringComparer.Ordinal)
                .ThenBy(anchor => anchor.TransitionIndex))
            {
                root.Add(Anchor(anchor));
            }

            foreach (var segment in ownership.Segments
                .OrderBy(segment => segment.LogicalLink.Link.Order)
                .ThenBy(segment => segment.SegmentIndex))
            {
                PerformanceAudit.Increment("XML edge metadata lookups");
                PerformanceAudit.Increment("metadata projections");
                layout.Traversals.Traversals.TryGetValue(segment.LogicalEdgeId, out var traversal);
                var pathDecision = layout.PathSelection?.Decisions.FirstOrDefault(decision =>
                    string.Equals(decision.EdgeId, segment.LogicalEdgeId, StringComparison.Ordinal));
                CorridorPathCandidate? pathCandidate = null;
                if (layout.PathSelection is not null)
                {
                    layout.PathSelection.Selected.TryGetValue(segment.LogicalEdgeId, out pathCandidate);
                }
                if (layout.RegionalPathSelection is not null)
                {
                    layout.RegionalPathSelection.Selected.TryGetValue(segment.LogicalEdgeId, out pathCandidate);
                    if (layout.RegionalPathSelection.Initial.TryGetValue(segment.LogicalEdgeId, out var initialPathCandidate))
                    {
                        var regionalDecision = layout.RegionalPathSelection.Decisions.FirstOrDefault(decision =>
                            decision.MutableEdgeIds.Contains(segment.LogicalEdgeId, StringComparer.Ordinal));
                        pathDecision = new CorridorPathDecision(
                            segment.LogicalEdgeId,
                            initialPathCandidate.Signature.Value,
                            pathCandidate?.Signature.Value ?? initialPathCandidate.Signature.Value,
                            regionalDecision?.Reason ?? "No local traceability interaction was discovered.");
                    }
                }
                var rejectedPathEvaluations = layout.PathSelection?.Evaluations.Where(evaluation =>
                    evaluation.EdgeId == segment.LogicalEdgeId && !evaluation.IsSelected).ToArray();
                var regionDecision = layout.RegionalPathSelection?.Decisions.FirstOrDefault(decision =>
                    decision.MutableEdgeIds.Contains(segment.LogicalEdgeId, StringComparer.Ordinal) ||
                    decision.FixedContextEdgeIds.Contains(segment.LogicalEdgeId, StringComparer.Ordinal));
                root.Add(Edge(
                    segment,
                    traversal,
                    pathDecision,
                    pathCandidate,
                    rejectedPathEvaluations,
                    layout.RegionalPathSelection is not null,
                    regionDecision));
            }

            return root;
        }

        private sealed class ArchitectureGenerator
        {
            private readonly DiagramFileBuilder _builder;

            public ArchitectureGenerator(DiagramFileBuilder builder)
            {
                _builder = builder;
            }

            public XElement Generate(RenderLayout layout, CoordinateOwnershipCompilation ownership)
            {
                return _builder.ArchitectureRoot(layout, ownership);
            }
        }

private XElement GraphModel(XElement root)
        {
            return new XElement("mxGraphModel",
                new XAttribute("dx", "1200"),
                new XAttribute("dy", "900"),
                new XAttribute("grid", "0"),
                new XAttribute("gridSize", "10"),
                new XAttribute("guides", "1"),
                new XAttribute("tooltips", "1"),
                new XAttribute("connect", "1"),
                new XAttribute("arrows", "1"),
                new XAttribute("fold", "1"),
                new XAttribute("page", "0"),
                new XAttribute("pageScale", "1"),
                new XAttribute("pageWidth", "1600"),
                new XAttribute("pageHeight", "1200"),
                new XAttribute("background", _settings.Canvas.BackgroundColor),
                root);
        }

        private static string NodeLabel(RenderNode node)
        {
            return node.Interfaces.Count == 0
                ? node.Name
                : $"{node.Name} ({string.Join(", ", node.Interfaces)})";
        }

        private static XElement Vertex(string id, string value, string style, string parent, Rect rect)
        {
            return new XElement("mxCell",
                new XAttribute("id", id),
                new XAttribute("value", value),
                new XAttribute("style", style),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", parent),
                new XElement("mxGeometry",
                    new XAttribute("x", rect.X),
                    new XAttribute("y", rect.Y),
                    new XAttribute("width", rect.Width),
                    new XAttribute("height", rect.Height),
                    new XAttribute("as", "geometry")));
        }

        private static XElement Anchor(BoundaryAnchor anchor)
        {
            return new XElement("mxCell",
                new XAttribute("id", anchor.Id),
                new XAttribute("logicalEdgeId", anchor.LogicalEdgeId),
                new XAttribute("anchorRole", "ownership-boundary"),
                new XAttribute("ownerProjectId", anchor.OwnerProjectId),
                new XAttribute("transitionIndex", anchor.TransitionIndex),
                new XAttribute("value", string.Empty),
                new XAttribute("style", "opacity=0;fillOpacity=0;strokeOpacity=0;movable=0;resizable=0;deletable=0;"),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", anchor.OwnerProjectId),
                new XElement("mxGeometry",
                    new XAttribute("x", anchor.RelativePoint.X),
                    new XAttribute("y", anchor.RelativePoint.Y),
                    new XAttribute("width", 0),
                    new XAttribute("height", 0),
                    new XAttribute("as", "geometry")));
        }

        private XElement Edge(
            PhysicalEdgeSegment segment,
            EdgeTraversal? traversal,
            CorridorPathDecision? pathDecision,
            CorridorPathCandidate? pathCandidate,
            IReadOnlyList<CorridorPathEvaluation>? rejectedPathEvaluations,
            bool usesRegionalOptimisation,
            RegionOptimisationDecision? regionDecision)
        {
            // mxCell stores edge terminals in source/target, style as key=value pairs, and mxGeometry points as waypoints.
            // See https://jgraph.github.io/mxgraph/docs/js-api/files/model/mxCell-js.html and
            // https://jgraph.github.io/mxgraph/docs/js-api/files/model/mxGeometry-js.html.
            return new XElement("mxCell",
                new XAttribute("id", segment.Id),
                new XAttribute("logicalEdgeId", segment.LogicalEdgeId),
                new XAttribute("semanticSourceId", segment.SemanticSourceId),
                new XAttribute("semanticTargetId", segment.SemanticTargetId),
                new XAttribute("segmentIndex", segment.SegmentIndex),
                new XAttribute("segmentRole", segment.Role.ToString().ToLowerInvariant()),
                new XAttribute("routingMode", traversal?.UsesFallback == true ? "fallback" : "traversal"),
                pathDecision is null ? null : new XAttribute("pathInitialSignature", pathDecision.InitialSignature),
                pathDecision is null ? null : new XAttribute("pathFinalSignature", pathDecision.FinalSignature),
                pathDecision is null ? null : new XAttribute("pathDecision", pathDecision.Reason),
                pathCandidate is null ? null : new XAttribute("pathLocalCost",
                    $"length={pathCandidate.LocalCost.PathLength};bends={pathCandidate.LocalCost.BendCount};envelopeExpansion={pathCandidate.LocalCost.RouteEnvelopeExpansion}"),
                pathCandidate?.FanoutMemberships is null || pathCandidate.FanoutMemberships.Count == 0 ? null :
                    new XAttribute("fanoutGroups", string.Join(" | ", pathCandidate.FanoutMemberships.Select(fanout =>
                        $"{fanout.GroupId}:terminal={fanout.ConnectionOrder},lane={fanout.SlotOrder},remote={fanout.RemoteNodeOrder},side={fanout.Side}"))),
                pathCandidate?.FanoutMemberships is null ? null :
                    new XAttribute("fanoutMonotonic", pathCandidate.FanoutMemberships.Count == 0 ? "not-applicable" : "preserved"),
                rejectedPathEvaluations is null || rejectedPathEvaluations.Count == 0 ? null :
                    new XAttribute("pathRejectedAlternatives", string.Join(" | ", rejectedPathEvaluations.Select(evaluation =>
                        $"{evaluation.Signature}: {evaluation.Reason}"))),
                usesRegionalOptimisation ? new XAttribute("optimisationMode", "regional") : null,
                !usesRegionalOptimisation ? null : new XAttribute("optimisationRegionId", regionDecision?.RegionId ?? string.Empty),
                !usesRegionalOptimisation ? null : new XAttribute("regionMutableEdgeCount", regionDecision?.MutableEdgeIds.Count ?? 0),
                !usesRegionalOptimisation ? null : new XAttribute("regionContextEdgeCount", regionDecision?.FixedContextEdgeIds.Count ?? 0),
                regionDecision is null ? null : new XAttribute("regionInitialScore", regionDecision.InitialScore),
                regionDecision is null ? null : new XAttribute("regionFinalScore", regionDecision.FinalScore),
                !usesRegionalOptimisation ? null : new XAttribute("regionDecision",
                    regionDecision?.Reason ?? "No local traceability interaction was discovered."),
                !usesRegionalOptimisation ? null : new XAttribute("regionFallbackReason",
                    regionDecision?.FallbackReason.ToString() ?? RegionFallbackReason.NoTraceabilityIssue.ToString()),
                traversal is null || traversal.Diagnostics.Count == 0
                    ? null
                    : new XAttribute("routingDiagnostics", string.Join(",", traversal.Diagnostics
                        .Select(diagnostic => diagnostic.Code)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(code => code, StringComparer.Ordinal))),
                segment.OwnerProjectId is null ? null : new XAttribute("ownerProjectId", segment.OwnerProjectId),
                new XAttribute("labelOwner", segment.OwnsLabel ? "1" : "0"),
                new XAttribute("style", BuildConnectorStyle(_settings.Connector, segment)),
                new XAttribute("edge", "1"),
                new XAttribute("parent", segment.ParentId),
                new XAttribute("source", segment.SourceCellId),
                new XAttribute("target", segment.TargetCellId),
                new XElement("mxGeometry",
                    new XAttribute("relative", "1"),
                    new XAttribute("as", "geometry"),
                    new XElement("Array",
                        new XAttribute("as", "points"),
                        segment.RelativeWaypoints.Select(point => new XElement("mxPoint",
                            new XAttribute("x", point.X),
                            new XAttribute("y", point.Y))))));
        }

        private static TypeNode ToTypeNode(RenderNode node)
        {
            return new TypeNode(node.Id, node.ProjectId ?? string.Empty, node.Name, node.FullName, node.Kind, Interfaces: node.Interfaces, Properties: node.Properties, MethodCount: node.MethodCount);
        }

        private static string BuildNodeStyle(NodeStyle style)
        {
            var shape = style.Shape == "rounded" ? "rounded=1;whiteSpace=wrap;html=1;" : $"shape={style.Shape};whiteSpace=wrap;html=1;";
            var shadow = style.Shadow ? "shadow=1;" : string.Empty;
            return $"{shape}fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};{shadow}{style.ExtraStyle}";
        }

        private static string BuildConnectorStyle(ConnectorStyle style, PhysicalEdgeSegment segment)
        {
            var rounded = style.Rounded ? "rounded=1;" : "rounded=0;";
            var link = segment.LogicalLink;
            var exitX = segment.SegmentIndex == 0 ? link.ExitX : 0.5;
            var exitY = segment.SegmentIndex == 0 ? link.ExitY : 0.5;
            var entryX = segment.HasTargetArrow ? link.EntryX : 0.5;
            var entryY = segment.HasTargetArrow ? link.EntryY : 0.5;
            var endArrow = segment.HasTargetArrow ? "endArrow=block;endFill=1;" : "endArrow=none;endFill=0;";
            return $"edgeStyle=none;noEdgeStyle=1;orthogonal=0;curved=0;html=1;{rounded}startArrow=none;{endArrow}strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};exitX={FormatRatio(exitX)};exitY={FormatRatio(exitY)};exitPerimeter=0;entryX={FormatRatio(entryX)};entryY={FormatRatio(entryY)};entryPerimeter=0;";
        }

        private static string BuildConnectorStyle(ConnectorStyle style, LinkLayout link)
        {
            var rounded = style.Rounded ? "rounded=1;" : "rounded=0;";
            return $"edgeStyle=none;noEdgeStyle=1;orthogonal=0;curved=0;html=1;{rounded}endArrow=block;endFill=1;strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};exitX={FormatRatio(link.ExitX)};exitY={FormatRatio(link.ExitY)};exitPerimeter=0;entryX={FormatRatio(link.EntryX)};entryY={FormatRatio(link.EntryY)};entryPerimeter=0;";
        }

        private static string FormatRatio(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
}
