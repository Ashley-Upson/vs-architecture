using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

/// <summary>
/// First replacement Architecture renderer. It deliberately keeps planning, the accepted absolute
/// scene, and Draw.io projection as separate immutable boundaries.
/// </summary>
public sealed class ReplacementArchitectureRenderer
{
    public ArchitectureRenderResult Render(ArchitectureRenderGraph graph, DiagramSettings settings)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        settings ??= DiagramSettings.CreateDefault();
        var timings = new List<PipelineStageMetric>();

        var planning = Measure(timings, "replacement planning graph", () => BuildPlanningGraph(graph, settings));
        var candidate = Measure(timings, "replacement candidate construction", () => BuildCandidate(planning, settings));
        var scene = Measure(timings, "replacement physical scene", () => BuildScene(planning, candidate, settings));
        var pageModel = Measure(timings, "replacement Draw.io page model", () => BuildPageModel(planning, scene, settings));
        var graphModel = Measure(timings, "replacement Draw.io serialization", () => Serialize(pageModel, settings));
        var reconstructed = Measure(timings, "replacement serialized geometry reconstruction", () => Reconstruct(graphModel));
        var serializationFindings = ValidateReconstruction(planning, scene, pageModel, reconstructed);
        var findings = scene.Findings.Concat(serializationFindings).ToArray();
        var hardFindings = findings.Where(finding => finding.IsStrictlyEnforced).ToArray();
        if (hardFindings.Length > 0)
            throw new InvalidOperationException(
                "Replacement Architecture candidate rejected: " +
                string.Join("; ", hardFindings.Select(finding => $"{finding.Category} ({finding.LogicalRouteId}, node={finding.OtherNodeId}, otherRoute={finding.OtherRouteId}, {finding.Description})")));

        var page = new DrawioPage("Architecture", "architecture", graphModel,
            findings.Select(finding => new DiagramDiagnostic(
                finding.Category, finding.Description, finding.LogicalRouteId)).ToArray());
        var routes = scene.Routes.Select(route => new GeneratedRoute(
            route.Link.Link.Id,
            route.Points.Select(point => new ValidationPoint(point.X, point.Y)).ToArray())).ToArray();

        return new ArchitectureRenderResult(
            page,
            Array.Empty<ValidationFinding>(),
            findings,
            Array.Empty<ValidationFinding>(),
            Array.Empty<RouteRepairAttempt>(),
            routes,
            timings,
            new ArchitectureEligibilityResult(
                findings.All(finding => !finding.IsStrictlyEnforced),
                findings.Where(finding => finding.IsStrictlyEnforced)
                    .Select(finding => finding.Category).Distinct(StringComparer.Ordinal).ToArray()),
            () => new DrawioDiagnosticExportResult(
                new DrawioDocumentComposer().Compose(new[] { page }, new DrawioDocumentSettings()).Content,
                BuildProvenanceJson(planning, candidate, scene, pageModel, findings),
                new Dictionary<string, string>
                {
                    ["planning.json"] = BuildProvenanceJson(planning, candidate, scene, pageModel, findings)
                },
                findings.Count(finding => finding.IsStrictlyEnforced),
                findings.Select(finding => finding.LogicalRouteId).Distinct(StringComparer.Ordinal).Count()),
            new ArchitectureDevelopmentArtifacts(
                BuildProvenanceJson(planning, candidate, scene, pageModel, findings),
                new Dictionary<string, string>
                {
                    ["planning.json"] = BuildProvenanceJson(planning, candidate, scene, pageModel, findings),
                    ["scene.json"] = BuildSceneJson(scene),
                    ["page-model.json"] = BuildPageJson(pageModel)
                }));
    }

    private static ArchitecturePlanningGraph BuildPlanningGraph(
        ArchitectureRenderGraph graph,
        DiagramSettings settings)
    {
        var nodes = graph.Nodes.OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal).ToArray();
        var links = graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal).ToArray();
        var incoming = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.ToDictionary(node => node.Id, _ => new List<ArchitectureRenderLink>(), StringComparer.Ordinal);
        foreach (var link in links)
        {
            if (incoming.ContainsKey(link.TargetRenderInstanceId)) incoming[link.TargetRenderInstanceId]++;
            if (outgoing.TryGetValue(link.SourceRenderInstanceId, out var list)) list.Add(link);
        }

        var depths = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var queue = new Queue<string>(nodes.Where(node => incoming[node.Id] == 0).Select(node => node.Id));
        var remaining = incoming.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var processed = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var source = queue.Dequeue();
            processed.Add(source);
            foreach (var link in outgoing[source].OrderBy(link => link.Order))
            {
                depths[link.TargetRenderInstanceId] = Math.Max(
                    depths[link.TargetRenderInstanceId], depths[source] + 1);
                if (--remaining[link.TargetRenderInstanceId] == 0) queue.Enqueue(link.TargetRenderInstanceId);
            }
        }

        // A residual strongly connected component has no topological entry point. Give its
        // members deterministic fallback depths so one cycle edge can remain downward while
        // the return edge is routed through the explicit return-lane policy.
        var residual = nodes.Where(node => !processed.Contains(node.Id))
            .OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < residual.Length; index++) depths[residual[index].Id] = index;

        var widthByNode = nodes.ToDictionary(node => node.Id, node => RequiredWidth(node, links, settings), StringComparer.Ordinal);
        var planningNodes = nodes.Select(node => new ArchitecturePlanningNode(
            node,
            node.Order,
            depths[node.Id],
            widthByNode[node.Id],
            settings.Layout.NodeHeight,
            node.PlacementParentRenderId ?? node.SemanticNodeId)).ToArray();
        var planningLinks = links.Select(link => new ArchitecturePlanningLink(
            link,
            link.Order,
            Topology(link, depths),
            link.SourceRenderInstanceId,
            link.TargetRenderInstanceId)).ToArray();
        return new ArchitecturePlanningGraph(graph, planningNodes, planningLinks);
    }

    private static ArchitectureCandidatePlan BuildCandidate(
        ArchitecturePlanningGraph graph,
        DiagramSettings settings)
    {
        var nodeRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var maxDepth = graph.Nodes.Select(node => node.Depth).DefaultIfEmpty(0).Max();
        var layers = graph.Nodes.Where(node => graph.Links.Any(link =>
                link.SourcePlanningNodeId == node.Node.Id || link.TargetPlanningNodeId == node.Node.Id))
            .GroupBy(node => node.Depth).OrderBy(group => group.Key).ToArray();
        var y = settings.Layout.ContainerPadding * 2;
        var depthByNodeId = graph.Nodes.ToDictionary(node => node.Node.Id, node => node.Depth, StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            var x = settings.Layout.ContainerPadding * 2;
            foreach (var node in layer.OrderBy(node => node.Order).ThenBy(node => node.Node.Id, StringComparer.Ordinal))
            {
                nodeRects[node.Node.Id] = new Rect(x, y, node.Width, node.Height);
                x += node.Width + settings.Layout.HorizontalSpacing;
            }
            var bandPressure = graph.Links.Count(link =>
            {
                var sourceDepth = depthByNodeId[link.SourcePlanningNodeId];
                var targetDepth = depthByNodeId[link.TargetPlanningNodeId];
                return Math.Min(sourceDepth, targetDepth) <= layer.Key && layer.Key < Math.Max(sourceDepth, targetDepth);
            });
            y += settings.Layout.NodeHeight + settings.Layout.VerticalSpacing +
                Math.Max(0, bandPressure) * settings.Layout.ParallelLaneSpacing + settings.Layout.LinkPadding * 2;
        }

        var connectedRight = nodeRects.Values.Select(rect => rect.Right).DefaultIfEmpty(0).Max();
        var standalone = graph.Nodes.Where(node => !nodeRects.ContainsKey(node.Node.Id))
            .OrderBy(node => node.Order).ThenBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standalone.Length)));
        var standaloneX = connectedRight + settings.Layout.StandaloneGroupSpacing;
        for (var index = 0; index < standalone.Length; index++)
        {
            var node = standalone[index];
            nodeRects[node.Node.Id] = new Rect(
                standaloneX + (index % columns) * (node.Width + settings.Layout.HorizontalSpacing),
                settings.Layout.ContainerPadding * 2 + (index / columns) * (node.Height + settings.Layout.VerticalSpacing),
                node.Width,
                node.Height);
        }

        CenterParents(graph, nodeRects, settings);
        ApplyBaselineAlignment(graph, nodeRects, settings);
        PlaceExternalTerminals(graph, nodeRects, settings);
        ResolveNodeOverlaps(graph, nodeRects, settings);
        ClearPortColumnObstacles(graph, nodeRects, settings);
        ResolveNodeOverlaps(graph, nodeRects, settings);

        var projectRects = BuildProjectRects(graph, nodeRects, settings);
        var terminals = AllocateTerminals(graph, nodeRects, settings);
        var routes = BuildRoutes(graph, nodeRects, terminals, projectRects, settings);
        var findings = ValidateScene(graph, nodeRects, projectRects, terminals, routes, settings);
        return new ArchitectureCandidatePlan(
            "replacement-001",
            new ReadOnlyDictionary<string, Rect>(nodeRects),
            new ReadOnlyDictionary<string, Rect>(projectRects),
            terminals,
            routes,
            routes.SelectMany(route => projectRects.Keys.Where(project =>
                graph.Source.Nodes.Any(node => node.Id == route.Link.Link.SourceRenderInstanceId && node.ProjectId == project) &&
                graph.Source.Nodes.Any(node => node.Id == route.Link.Link.TargetRenderInstanceId && node.ProjectId != project)))
                .Distinct(StringComparer.Ordinal).ToArray(),
            new[]
            {
                "Candidate ordering: source ArchitectureRenderGraph order",
                "Candidate placement: contiguous depth layers with explicit standalone grid",
                "Candidate routing: topology-specific orthogonal routes",
                "Candidate validation: absolute physical scene before Draw.io projection"
            },
            findings,
            routes.Sum(route => route.Points.Zip(route.Points.Skip(1), (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)).Sum()),
            routes.Sum(route => Math.Max(0, route.Points.Count - 2)));
    }

    private static ArchitecturePhysicalScene BuildScene(
        ArchitecturePlanningGraph graph,
        ArchitectureCandidatePlan candidate,
        DiagramSettings settings)
    {
        var bounds = candidate.NodeRects.Values.Concat(candidate.ProjectRects.Values).Aggregate(
            new Rect(0, 0, settings.Layout.ContainerPadding, settings.Layout.ContainerPadding), Union);
        foreach (var route in candidate.Routes)
            foreach (var point in route.Points)
                bounds = Union(bounds, new Rect(point.X, point.Y, 1, 1));
        var labels = candidate.ProjectRects.ToDictionary(
            item => item.Key,
            item => new Rect(item.Value.X, item.Value.Y, item.Value.Width, settings.Layout.ProjectHeaderHeight),
            StringComparer.Ordinal);
        return new ArchitecturePhysicalScene(candidate, bounds, labels, candidate.Routes,
            candidate.OwnershipTransitions, candidate.Findings);
    }

    private static DrawioPageModel BuildPageModel(
        ArchitecturePlanningGraph graph,
        ArchitecturePhysicalScene scene,
        DiagramSettings settings)
    {
        var cells = new List<DrawioPageCell>
        {
            new("0", "", null, null, string.Empty, string.Empty, null, Array.Empty<Point>(), false),
            new("1", "0", null, null, string.Empty, string.Empty, null, Array.Empty<Point>(), false)
        };
        foreach (var project in graph.Source.Projects.OrderBy(project => project.Order))
        {
            if (settings.ShowProjectContainers && scene.Candidate.ProjectRects.TryGetValue(project.Id, out var projectRect))
                cells.Add(new DrawioPageCell(project.Id, "1", null, null, project.Name,
                    ProjectStyle(settings.ProjectContainerStyle), projectRect, Array.Empty<Point>(), false));
        }
        foreach (var node in graph.Nodes.OrderBy(node => node.Order))
        {
            var rect = scene.Candidate.NodeRects[node.Node.Id];
            var parent = settings.ShowProjectContainers && node.Node.ProjectId is not null &&
                scene.Candidate.ProjectRects.ContainsKey(node.Node.ProjectId) ? node.Node.ProjectId : "1";
            var absolute = rect;
            if (parent != "1")
            {
                var projectRect = scene.Candidate.ProjectRects[parent];
                rect = new Rect(absolute.X - projectRect.X, absolute.Y - projectRect.Y, absolute.Width, absolute.Height);
            }
            cells.Add(new DrawioPageCell(node.Node.Id, parent, null, null,
                node.Node.IsExternal ? $"{node.Node.ExternalTag}\n{node.Node.DisplayText}" : node.Node.DisplayText,
                NodeStyle(settings, node.Node), rect, Array.Empty<Point>(), false,
                SemanticNodeId: node.Node.SemanticNodeId));
        }
        foreach (var route in scene.Routes.OrderBy(route => route.Link.Link.Order))
        {
            var source = graph.Source.Nodes.Single(node => node.Id == route.Link.Link.SourceRenderInstanceId);
            var target = graph.Source.Nodes.Single(node => node.Id == route.Link.Link.TargetRenderInstanceId);
            var sourceRect = scene.Candidate.NodeRects[source.Id];
            var targetRect = scene.Candidate.NodeRects[target.Id];
            var sourceTerminal = scene.Candidate.Terminals.Single(terminal => terminal.LinkId == route.Link.Link.Id && terminal.IsSource).Point;
            var targetTerminal = scene.Candidate.Terminals.Single(terminal => terminal.LinkId == route.Link.Link.Id && !terminal.IsSource).Point;
            cells.Add(new DrawioPageCell(
                $"edge_{route.Link.Link.Id}",
                "1",
                source.Id,
                target.Id,
                string.Empty,
                ConnectorStyle(settings, route, target),
                null,
                route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).ToArray(),
                true,
                route.Link.Link.SourceSemanticId,
                route.Link.Link.TargetSemanticId,
                ExitX: (sourceTerminal.X - sourceRect.X) / (double)Math.Max(1, sourceRect.Width),
                EntryX: (targetTerminal.X - targetRect.X) / (double)Math.Max(1, targetRect.Width)));
        }
        return new DrawioPageModel(cells, scene.PageBounds, scene.Candidate.Provenance);
    }

    private static XElement Serialize(DrawioPageModel page, DiagramSettings settings)
    {
        var root = new XElement("root");
        foreach (var cell in page.Cells)
        {
            if (cell.Id == "0" || cell.Id == "1")
            {
                root.Add(cell.Id == "0"
                    ? new XElement("mxCell", new XAttribute("id", "0"))
                    : new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
                continue;
            }

            var element = new XElement("mxCell",
                new XAttribute("id", cell.Id),
                new XAttribute("parent", cell.ParentId),
                new XAttribute("value", cell.Value),
                new XAttribute("style", cell.Style));
            if (cell.IsEdge)
            {
                element.Add(new XAttribute("edge", "1"),
                    new XAttribute("logicalEdgeId", cell.Id.StartsWith("edge_", StringComparison.Ordinal) ? cell.Id.Substring(5) : cell.Id),
                    new XAttribute("segmentIndex", "0"),
                    new XAttribute("source", cell.SourceId!), new XAttribute("target", cell.TargetId!));
                if (cell.SemanticSourceId is not null)
                    element.Add(new XAttribute("semanticSourceId", cell.SemanticSourceId));
                if (cell.SemanticTargetId is not null)
                    element.Add(new XAttribute("semanticTargetId", cell.SemanticTargetId));
                if (cell.ExitX is double exitX)
                    element.Add(new XAttribute("exitX", FormatRatio(exitX)), new XAttribute("exitY", "1"));
                if (cell.EntryX is double entryX)
                    element.Add(new XAttribute("entryX", FormatRatio(entryX)), new XAttribute("entryY", "0"));
                element.Add(new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"),
                    new XElement("Array", new XAttribute("as", "points"), cell.Waypoints.Select(point =>
                        new XElement("mxPoint", new XAttribute("x", point.X), new XAttribute("y", point.Y))))));
            }
            else
            {
                element.Add(new XAttribute("vertex", "1"));
                if (cell.Bounds is Rect rect)
                {
                    if (cell.SemanticNodeId is not null)
                        element.Add(new XAttribute("semanticNodeId", cell.SemanticNodeId));
                    element.Add(new XElement("mxGeometry", new XAttribute("x", rect.X), new XAttribute("y", rect.Y),
                        new XAttribute("width", rect.Width), new XAttribute("height", rect.Height), new XAttribute("as", "geometry")));
                }
            }
            root.Add(element);
        }
        return new XElement("mxGraphModel", new XAttribute("grid", "0"), new XAttribute("page", "0"), root);
    }

    private static DrawioPageModel Reconstruct(XElement graphModel)
    {
        var rawCells = graphModel.Element("root")?.Elements("mxCell").Select(cell =>
        {
            var geometry = cell.Element("mxGeometry");
            var isEdge = cell.Attribute("edge")?.Value == "1";
            var bounds = !isEdge && geometry is not null && int.TryParse(geometry.Attribute("x")?.Value, out var x) &&
                int.TryParse(geometry.Attribute("y")?.Value, out var y) && int.TryParse(geometry.Attribute("width")?.Value, out var width) &&
                int.TryParse(geometry.Attribute("height")?.Value, out var height) ? new Rect(x, y, width, height) : (Rect?)null;
            var points = geometry?.Element("Array")?.Elements("mxPoint").Select(point => new Point(
                int.Parse(point.Attribute("x")!.Value), int.Parse(point.Attribute("y")!.Value))).ToArray() ?? Array.Empty<Point>();
            return new DrawioPageCell(cell.Attribute("id")?.Value ?? string.Empty, cell.Attribute("parent")?.Value ?? string.Empty,
                cell.Attribute("source")?.Value, cell.Attribute("target")?.Value, cell.Attribute("value")?.Value ?? string.Empty,
                cell.Attribute("style")?.Value ?? string.Empty, bounds, points, isEdge,
                cell.Attribute("semanticSourceId")?.Value, cell.Attribute("semanticTargetId")?.Value,
                cell.Attribute("semanticNodeId")?.Value,
                double.TryParse(cell.Attribute("exitX")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var exitX) ? exitX : null,
                double.TryParse(cell.Attribute("entryX")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var entryX) ? entryX : null);
        }).ToArray() ?? Array.Empty<DrawioPageCell>();
        var byId = rawCells.ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        var cells = rawCells.Select(cell => cell with
        {
            Bounds = cell.IsEdge || cell.Bounds is null ? cell.Bounds : ResolveAbsoluteBounds(cell, byId, new HashSet<string>(StringComparer.Ordinal))
        }).ToArray();
        return new DrawioPageModel(cells, new Rect(0, 0, 0, 0), Array.Empty<string>());
    }

    private static IReadOnlyList<ValidationFinding> ValidateReconstruction(
        ArchitecturePlanningGraph graph,
        ArchitecturePhysicalScene scene,
        DrawioPageModel expected,
        DrawioPageModel actual)
    {
        var findings = new List<ValidationFinding>();
        var expectedEdges = expected.Cells.Where(cell => cell.IsEdge).ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        var actualEdges = actual.Cells.Where(cell => cell.IsEdge).ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        foreach (var item in expectedEdges)
        {
            if (!actualEdges.TryGetValue(item.Key, out var actualEdge) || !item.Value.Waypoints.SequenceEqual(actualEdge.Waypoints))
                findings.Add(Finding("SerializationGeometry", item.Key, "Draw.io reconstruction changed or lost route waypoints.", true));
            else if (actualEdge.SourceId != item.Value.SourceId || actualEdge.TargetId != item.Value.TargetId ||
                     !SameRatio(actualEdge.ExitX, item.Value.ExitX) || !SameRatio(actualEdge.EntryX, item.Value.EntryX))
                findings.Add(Finding("OwnershipReconstruction", item.Key,
                    $"Draw.io reconstruction changed edge ownership or terminal ratios ({item.Value.SourceId}->{item.Value.TargetId}, {item.Value.ExitX}/{item.Value.EntryX} became {actualEdge.SourceId}->{actualEdge.TargetId}, {actualEdge.ExitX}/{actualEdge.EntryX}).", true));

            if (actualEdges.TryGetValue(item.Key, out actualEdge) && actualEdge.SourceId is { } sourceId &&
                actualEdge.TargetId is { } targetId && actualNodesById(actual, sourceId) is { } source &&
                actualNodesById(actual, targetId) is { } target && actualEdge.ExitX is double exitX &&
                actualEdge.EntryX is double entryX)
            {
                var reconstructedRoute = new[]
                {
                    new Point(source.X + (int)Math.Round(source.Width * exitX), source.Bottom),
                }.Concat(actualEdge.Waypoints)
                .Concat(new[] { new Point(target.X + (int)Math.Round(target.Width * entryX), target.Y) })
                .ToArray();
                var routeId = item.Key.StartsWith("edge_", StringComparison.Ordinal) ? item.Key.Substring(5) : item.Key;
                var accepted = scene.Routes.SingleOrDefault(route => route.Link.Link.Id == routeId)?.Points;
                if (accepted is null || !accepted.SequenceEqual(reconstructedRoute))
                    findings.Add(Finding("GeometryReconstruction", item.Key, "Reconstructed complete route differs from the accepted physical route.", true));
            }
            else
            {
                findings.Add(Finding("OwnershipReconstruction", item.Key, "Reconstructed route is missing a source, target, or terminal ratio.", true));
            }
        }

        var expectedNodes = expected.Cells.Where(cell => !cell.IsEdge && cell.SemanticNodeId is not null)
            .ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        var actualNodes = actual.Cells.Where(cell => !cell.IsEdge && cell.SemanticNodeId is not null)
            .ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        foreach (var planning in graph.Nodes)
        {
            if (!actualNodes.TryGetValue(planning.Node.Id, out var actualNode) || actualNode.Bounds is null ||
                !scene.Candidate.NodeRects.TryGetValue(planning.Node.Id, out var expectedBounds) ||
                actualNode.Bounds.Value != expectedBounds)
                findings.Add(Finding("GeometryReconstruction", planning.Node.Id, "Reconstructed node bounds differ from the accepted absolute scene.", true));
            if (expectedNodes.TryGetValue(planning.Node.Id, out var expectedNode) && actualNode.ParentId != expectedNode.ParentId)
                findings.Add(Finding("OwnershipReconstruction", planning.Node.Id, "Reconstructed node parent ownership changed.", true));
        }
        foreach (var project in scene.Candidate.ProjectRects)
        {
            var actualProject = actual.Cells.SingleOrDefault(cell => cell.Id == project.Key);
            if (actualProject?.Bounds is null || actualProject.Bounds.Value != project.Value)
                findings.Add(Finding("GeometryReconstruction", project.Key, "Reconstructed project bounds differ from the accepted scene.", true));
        }
        return findings;

        static Rect? actualNodesById(DrawioPageModel model, string id) =>
            model.Cells.SingleOrDefault(cell => cell.Id == id && !cell.IsEdge)?.Bounds;
    }

    private static IReadOnlyList<ValidationFinding> ValidateScene(
        ArchitecturePlanningGraph graph,
        IReadOnlyDictionary<string, Rect> nodes,
        IReadOnlyDictionary<string, Rect> projects,
        IReadOnlyList<ArchitectureTerminal> terminals,
        IReadOnlyList<ArchitecturePhysicalRoute> routes,
        DiagramSettings settings)
    {
        var findings = new List<ValidationFinding>();
        foreach (var pair in nodes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            foreach (var other in nodes.Where(other => string.CompareOrdinal(pair.Key, other.Key) < 0))
                if (Overlaps(pair.Value, other.Value))
                    findings.Add(Finding("NodeOverlap", pair.Key, $"Node {pair.Key} overlaps node {other.Key}.", true, other.Key));
        foreach (var route in routes)
        {
            var points = route.Points;
            if (points.Count < 2 || points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Any(segment => !segment.IsOrthogonal || segment.Length == 0))
                findings.Add(Finding("InvalidRoute", route.Link.Link.Id, "Route is not composed of non-zero orthogonal segments.", true));
            var source = nodes[route.Link.Link.SourceRenderInstanceId];
            var target = nodes[route.Link.Link.TargetRenderInstanceId];
            if (points[0].Y != source.Bottom || points[points.Count - 1].Y != target.Y)
                findings.Add(Finding("EndpointDirection", route.Link.Link.Id, "Route does not leave the source bottom and arrive at the target top.", true));
            foreach (var node in nodes.Where(node => node.Key != route.Link.Link.SourceRenderInstanceId && node.Key != route.Link.Link.TargetRenderInstanceId))
            {
                var offending = points.Zip(points.Skip(1), (a, b) => new Segment(a, b))
                    .FirstOrDefault(segment => SegmentIntersectsInterior(segment, node.Value));
                if (offending.Length > 0)
                    findings.Add(new ValidationFinding("LinkNodeIntersection", route.Link.Link.Id, null, node.Key, 1,
                        $"Route intersects node {node.Key} at {offending.Start.X},{offending.Start.Y}->{offending.End.X},{offending.End.Y}; node bounds {node.Value.X},{node.Value.Y},{node.Value.Width},{node.Value.Height}.",
                        new[] { new ValidationPoint(offending.Start.X, offending.Start.Y), new ValidationPoint(offending.End.X, offending.End.Y) },
                        Array.Empty<ValidationSegment>(), null, null, null, true));
            }
        }
        foreach (var pair in routes.SelectMany((left, index) => routes.Skip(index + 1)
                      .Select(right => (left, right))))
        {
            var sharedPair = pair.left.Points.Zip(pair.left.Points.Skip(1), (start, end) => new Segment(start, end))
                .SelectMany(first => pair.right.Points.Zip(pair.right.Points.Skip(1), (start, end) => new Segment(start, end))
                    .Where(second => first.IsHorizontal == second.IsHorizontal && first.IsOrthogonal &&
                        first.OverlapLength(second) > 0)
                    .Select(second => (First: first, Second: second)))
                .FirstOrDefault();
            var shared = sharedPair.First;
            if (shared.Length > 0)
                findings.Add(new ValidationFinding("SharedSegment", pair.left.Link.Link.Id, pair.right.Link.Link.Id, null, 1,
                    $"Routes share a non-zero collinear segment ({shared.Start.X},{shared.Start.Y}->{shared.End.X},{shared.End.Y}).",
                    new[] { new ValidationPoint(shared.Start.X, shared.Start.Y), new ValidationPoint(shared.End.X, shared.End.Y) },
                    Array.Empty<ValidationSegment>(), null, null, shared.Length, true));
        }
        return findings;
    }

    private static IReadOnlyList<ArchitectureTerminal> AllocateTerminals(
        ArchitecturePlanningGraph graph,
        IReadOnlyDictionary<string, Rect> nodes,
        DiagramSettings settings)
    {
        var terminals = new List<ArchitectureTerminal>();
        foreach (var group in graph.Links.GroupBy(link => link.SourcePlanningNodeId, StringComparer.Ordinal))
        {
            var source = nodes[group.Key];
            var links = group.OrderBy(link => nodes[link.TargetPlanningNodeId].CenterX).ThenBy(link => link.Order).ToArray();
            for (var index = 0; index < links.Length; index++)
                terminals.Add(new ArchitectureTerminal(links[index].Link.Id, group.Key,
                    new Point(source.X + (index + 1) * source.Width / (links.Length + 1), source.Bottom), true, "source-terminal-allocation"));
        }
        foreach (var group in graph.Links.GroupBy(link => link.TargetPlanningNodeId, StringComparer.Ordinal))
        {
            var target = nodes[group.Key];
            var links = group.OrderBy(link => nodes[link.SourcePlanningNodeId].CenterX).ThenBy(link => link.Order).ToArray();
            for (var index = 0; index < links.Length; index++)
                terminals.Add(new ArchitectureTerminal(links[index].Link.Id, group.Key,
                    new Point(target.X + (index + 1) * target.Width / (links.Length + 1), target.Y), false, "target-terminal-allocation"));
        }
        return terminals;
    }

    private static IReadOnlyList<ArchitecturePhysicalRoute> BuildRoutes(
        ArchitecturePlanningGraph graph,
        IReadOnlyDictionary<string, Rect> nodes,
        IReadOnlyList<ArchitectureTerminal> terminals,
        IReadOnlyDictionary<string, Rect> projects,
        DiagramSettings settings)
    {
        var routes = new List<ArchitecturePhysicalRoute>();
        var outerX = nodes.Values.Select(node => node.Right).DefaultIfEmpty(0).Max() + settings.Layout.ParallelLaneSpacing;
        var usedChannelY = new HashSet<int>();
        foreach (var link in graph.Links.OrderBy(link => link.Order))
        {
            var source = terminals.Single(terminal => terminal.LinkId == link.Link.Id && terminal.IsSource).Point;
            var target = terminals.Single(terminal => terminal.LinkId == link.Link.Id && !terminal.IsSource).Point;
            var sourceNode = nodes[link.SourcePlanningNodeId];
            var targetNode = nodes[link.TargetPlanningNodeId];
            var laneX = outerX + routes.Count * settings.Layout.ParallelLaneSpacing;
            var downward = targetNode.Y > sourceNode.Y;
            var sourceChannelY = FindChannelY(sourceNode.Bottom + settings.Layout.LinkPadding,
                downward ? targetNode.Y - settings.Layout.LinkPadding : sourceNode.Bottom + settings.Layout.LinkPadding + 100000,
                settings.Layout.ParallelLaneSpacing, source.X, laneX, sourceNode.Bottom, nodes,
                link.SourcePlanningNodeId, link.TargetPlanningNodeId, usedChannelY);
            var targetChannelY = FindChannelY(targetNode.Y - settings.Layout.LinkPadding,
                downward ? sourceNode.Bottom + settings.Layout.LinkPadding : targetNode.Y - settings.Layout.LinkPadding - 100000,
                -settings.Layout.ParallelLaneSpacing, target.X, laneX, targetNode.Y, nodes,
                link.SourcePlanningNodeId, link.TargetPlanningNodeId, usedChannelY);
            var points = Normalize(new[]
            {
                source,
                new Point(source.X, sourceChannelY),
                new Point(laneX, sourceChannelY),
                new Point(laneX, targetChannelY),
                new Point(target.X, targetChannelY),
                target
            });
            routes.Add(new ArchitecturePhysicalRoute(link, points, link.Topology,
                $"slot:{link.Link.Id}", $"column:{link.Link.Id}"));
        }
        return routes;
    }

    private static int FindChannelY(
        int start,
        int limit,
        int step,
        int nodeX,
        int laneX,
        int stubStartY,
        IReadOnlyDictionary<string, Rect> nodes,
        string sourceNodeId,
        string targetNodeId,
        ISet<int> usedChannelY)
    {
        var direction = Math.Sign(step);
        var increment = Math.Abs(step);
        if (increment == 0) increment = 1;
        for (var y = start; direction > 0 ? y <= limit : y >= limit; y += direction * increment)
        {
            if (usedChannelY.Contains(y)) continue;
            var intersects = nodes.Any(item => item.Key != sourceNodeId && item.Key != targetNodeId &&
                (SegmentIntersectsInterior(new Segment(new Point(nodeX, stubStartY), new Point(nodeX, y)), item.Value) ||
                 SegmentIntersectsInterior(new Segment(new Point(nodeX, y), new Point(laneX, y)), item.Value)));
            if (intersects) continue;
            usedChannelY.Add(y);
            return y;
        }
        for (var distance = 0; distance <= 100000; distance += increment)
        {
            var fallback = stubStartY + direction * distance;
            if (usedChannelY.Contains(fallback)) continue;
            var clear = !nodes.Any(item => item.Key != sourceNodeId && item.Key != targetNodeId &&
                (SegmentIntersectsInterior(new Segment(new Point(nodeX, stubStartY), new Point(nodeX, fallback)), item.Value) ||
                 SegmentIntersectsInterior(new Segment(new Point(nodeX, fallback), new Point(laneX, fallback)), item.Value)));
            if (!clear) continue;
            usedChannelY.Add(fallback);
            return fallback;
        }

        usedChannelY.Add(stubStartY);
        return stubStartY;
    }

    private static void CenterParents(ArchitecturePlanningGraph graph, Dictionary<string, Rect> nodes, DiagramSettings settings)
    {
        foreach (var source in graph.Nodes.OrderByDescending(node => node.Depth))
        {
            var children = graph.Links.Where(link => link.SourcePlanningNodeId == source.Node.Id)
                .Select(link => nodes[link.TargetPlanningNodeId]).ToArray();
            if (children.Length == 0) continue;
            var left = children.Min(child => child.X);
            var right = children.Max(child => child.Right);
            var current = nodes[source.Node.Id];
            nodes[source.Node.Id] = current with { X = Math.Max(settings.Layout.ContainerPadding, left + (right - left - current.Width) / 2) };
        }
    }

    private static void ApplyBaselineAlignment(
        ArchitecturePlanningGraph graph,
        Dictionary<string, Rect> nodes,
        DiagramSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Layout.BaselineAlignmentPattern)) return;
        var pattern = new Regex(settings.Layout.BaselineAlignmentPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var baseline = graph.Nodes.Where(node => pattern.IsMatch(node.Node.DisplayText ?? string.Empty) ||
                pattern.IsMatch(node.Node.SemanticTypeIdentity ?? string.Empty))
            .Select(node => nodes[node.Node.Id].Y).DefaultIfEmpty(-1).Min();
        if (baseline < 0) return;
        foreach (var node in graph.Nodes.Where(node => pattern.IsMatch(node.Node.DisplayText ?? string.Empty) ||
                pattern.IsMatch(node.Node.SemanticTypeIdentity ?? string.Empty)))
        {
            var rect = nodes[node.Node.Id];
            nodes[node.Node.Id] = rect with { Y = baseline };
        }
    }

    private static void PlaceExternalTerminals(
        ArchitecturePlanningGraph graph,
        Dictionary<string, Rect> nodes,
        DiagramSettings settings)
    {
        foreach (var external in graph.Nodes.Where(node => node.Node.IsExternal))
        {
            var parents = graph.Links.Where(link => link.TargetPlanningNodeId == external.Node.Id)
                .Select(link => nodes[link.SourcePlanningNodeId]).OrderBy(rect => rect.X).ToArray();
            if (parents.Length != 1) continue;
            var parent = parents[0];
            var rect = nodes[external.Node.Id];
            nodes[external.Node.Id] = rect with
            {
                X = parent.CenterX - rect.Width / 2,
                Y = parent.Bottom + settings.Layout.VerticalSpacing
            };
        }
    }

    private static void ClearPortColumnObstacles(
        ArchitecturePlanningGraph graph,
        Dictionary<string, Rect> nodes,
        DiagramSettings settings)
    {
        if (graph.Links.Count == 0 || nodes.Count == 0) return;
        for (var pass = 0; pass < 3; pass++)
        {
            var moved = false;
            var terminals = AllocateTerminals(graph, nodes, settings);
            var outerX = nodes.Values.Max(rect => rect.Right) + settings.Layout.ParallelLaneSpacing;
            var sourceIndex = graph.Links.GroupBy(link => link.SourcePlanningNodeId).SelectMany(group =>
                    group.OrderBy(link => link.Order).Select((link, index) => new { link.Link.Id, Index = index }))
                .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
            var targetIndex = graph.Links.GroupBy(link => link.TargetPlanningNodeId).SelectMany(group =>
                    group.OrderBy(link => link.Order).Select((link, index) => new { link.Link.Id, Index = index }))
                .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
            foreach (var link in graph.Links.OrderBy(link => link.Order))
            {
                var sourceTerminal = terminals.Single(terminal => terminal.LinkId == link.Link.Id && terminal.IsSource).Point;
                var targetTerminal = terminals.Single(terminal => terminal.LinkId == link.Link.Id && !terminal.IsSource).Point;
                var source = nodes[link.SourcePlanningNodeId];
                var target = nodes[link.TargetPlanningNodeId];
                var top = Math.Min(source.Bottom, target.Y);
                var bottom = Math.Max(source.Bottom, target.Y);
                foreach (var item in nodes.Where(item => item.Key != link.SourcePlanningNodeId && item.Key != link.TargetPlanningNodeId).ToArray())
                {
                    var sourceColumnBlocked = item.Value.X < sourceTerminal.X && sourceTerminal.X < item.Value.Right &&
                        item.Value.Y < bottom && top < item.Value.Bottom;
                    var targetColumnBlocked = item.Value.X < targetTerminal.X && targetTerminal.X < item.Value.Right &&
                        item.Value.Y < bottom && top < item.Value.Bottom;
                    if (!sourceColumnBlocked && !targetColumnBlocked) continue;
                    var right = nodes.Values.Max(rect => rect.Right) + settings.Layout.HorizontalSpacing;
                    nodes[item.Key] = item.Value with { X = right };
                    moved = true;
                }

                var sourceChannelY = source.Bottom + settings.Layout.LinkPadding +
                    sourceIndex[link.Link.Id] * settings.Layout.ParallelLaneSpacing;
                var targetChannelY = target.Y - settings.Layout.LinkPadding -
                    targetIndex[link.Link.Id] * settings.Layout.ParallelLaneSpacing;
                foreach (var item in nodes.Where(item => item.Key != link.SourcePlanningNodeId && item.Key != link.TargetPlanningNodeId).ToArray())
                {
                    var sourceChannelBlocked = SegmentIntersectsInterior(
                        new Segment(new Point(sourceTerminal.X, sourceChannelY), new Point(outerX, sourceChannelY)), item.Value);
                    var targetChannelBlocked = SegmentIntersectsInterior(
                        new Segment(new Point(targetTerminal.X, targetChannelY), new Point(outerX, targetChannelY)), item.Value);
                    if (!sourceChannelBlocked && !targetChannelBlocked) continue;
                    var right = nodes.Values.Max(rect => rect.Right) + settings.Layout.HorizontalSpacing;
                    nodes[item.Key] = item.Value with { X = right };
                    moved = true;
                }
            }
            if (!moved) break;
        }
    }

    private static void ResolveNodeOverlaps(ArchitecturePlanningGraph graph, Dictionary<string, Rect> nodes, DiagramSettings settings)
    {
        var ordered = graph.Nodes.OrderBy(node => nodes[node.Node.Id].Y)
            .ThenBy(node => nodes[node.Node.Id].X).ThenBy(node => node.Order).ThenBy(node => node.Node.Id, StringComparer.Ordinal)
            .ToArray();
        for (var left = 0; left < ordered.Length; left++)
        {
            for (var right = left + 1; right < ordered.Length; right++)
            {
                var first = nodes[ordered[left].Node.Id];
                var second = nodes[ordered[right].Node.Id];
                if (!Overlaps(first, second)) continue;
                nodes[ordered[right].Node.Id] = second with { X = first.Right + settings.Layout.HorizontalSpacing };
            }
        }
    }

    private static Dictionary<string, Rect> BuildProjectRects(ArchitecturePlanningGraph graph, IReadOnlyDictionary<string, Rect> nodes, DiagramSettings settings)
    {
        return graph.Source.Projects.Where(project => nodes.Any(node => graph.Nodes.Any(planning => planning.Node.Id == node.Key && planning.Node.ProjectId == project.Id)))
            .ToDictionary(project => project.Id, project =>
            {
                var projectNodes = graph.Nodes.Where(node => node.Node.ProjectId == project.Id).Select(node => nodes[node.Node.Id]).ToArray();
                var left = projectNodes.Min(node => node.X) - settings.Layout.ContainerPadding;
                var top = projectNodes.Min(node => node.Y) - settings.Layout.ProjectHeaderHeight - settings.Layout.ContainerPadding;
                var right = projectNodes.Max(node => node.Right) + settings.Layout.ContainerPadding;
                var bottom = projectNodes.Max(node => node.Bottom) + settings.Layout.ContainerPadding;
                return new Rect(left, top, right - left, bottom - top);
            }, StringComparer.Ordinal);
    }

    private static int RequiredWidth(ArchitectureRenderNode node, IReadOnlyList<ArchitectureRenderLink> links, DiagramSettings settings)
    {
        var incoming = links.Count(link => link.TargetRenderInstanceId == node.Id);
        var outgoing = links.Count(link => link.SourceRenderInstanceId == node.Id);
        var text = Math.Max(node.DisplayText?.Length ?? 0, node.SemanticTypeIdentity?.Length / 2 ?? 0) * 8 + settings.Layout.LinkNodeWidthPadding;
        var terminal = Math.Max(incoming, outgoing) * Math.Max(settings.Layout.EdgePortSpacing, settings.Layout.ParallelLaneSpacing) + settings.Layout.LinkNodeWidthPadding;
        return Math.Max(settings.Layout.NodeWidth, Math.Max(text, terminal));
    }

    private static string Topology(ArchitectureRenderLink link, IReadOnlyDictionary<string, int> depths) =>
        depths[link.TargetRenderInstanceId] > depths[link.SourceRenderInstanceId] ? "downward" : "return";

    private static IReadOnlyList<Point> Normalize(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
            if (result.Count == 0 || result[result.Count - 1] != point) result.Add(point);
        return result;
    }

    private static bool Overlaps(Rect left, Rect right) =>
        left.X < right.Right && right.X < left.Right && left.Y < right.Bottom && right.Y < left.Bottom;

    private static bool SegmentIntersectsInterior(Segment segment, Rect rect) =>
        segment.Start.X == segment.End.X
            ? segment.Start.X > rect.X && segment.Start.X < rect.Right &&
              Math.Max(Math.Min(segment.Start.Y, segment.End.Y), rect.Y) <
              Math.Min(Math.Max(segment.Start.Y, segment.End.Y), rect.Bottom)
            : segment.Start.Y == segment.End.Y && segment.Start.Y > rect.Y && segment.Start.Y < rect.Bottom &&
              Math.Max(Math.Min(segment.Start.X, segment.End.X), rect.X) <
              Math.Min(Math.Max(segment.Start.X, segment.End.X), rect.Right);

    private static bool SameRatio(double? left, double? right) =>
        left is null && right is null || left is double l && right is double r && Math.Abs(l - r) < 0.000001;

    private static Rect ResolveAbsoluteBounds(
        DrawioPageCell cell,
        IReadOnlyDictionary<string, DrawioPageCell> cells,
        ISet<string> visited)
    {
        if (cell.Bounds is not Rect bounds) throw new InvalidOperationException($"Cell {cell.Id} has no geometry.");
        if (cell.ParentId == "0" || cell.ParentId == "1" || !cells.TryGetValue(cell.ParentId, out var parent) ||
            parent.Bounds is null || !visited.Add(cell.ParentId)) return bounds;
        var parentBounds = ResolveAbsoluteBounds(parent, cells, visited);
        return bounds with { X = bounds.X + parentBounds.X, Y = bounds.Y + parentBounds.Y };
    }

    private static Rect Union(Rect left, Rect right)
    {
        var x = Math.Min(left.X, right.X);
        var y = Math.Min(left.Y, right.Y);
        var r = Math.Max(left.Right, right.Right);
        var b = Math.Max(left.Bottom, right.Bottom);
        return new Rect(x, y, r - x, b - y);
    }

    private static ValidationFinding Finding(string category, string routeId, string description, bool strict, string? otherNodeId = null) =>
        new(category, routeId, null, otherNodeId, 1, description, Array.Empty<ValidationPoint>(),
            Array.Empty<ValidationSegment>(), null, null, null, strict);

    private static ValidationFinding FindingWithOtherRoute(string category, string routeId, string otherRouteId,
        string description, bool strict) =>
        new(category, routeId, otherRouteId, null, 1, description, Array.Empty<ValidationPoint>(),
            Array.Empty<ValidationSegment>(), null, null, null, strict);

    private static string NodeStyle(DiagramSettings settings, ArchitectureRenderNode node)
    {
        var style = node.IsExternal ? settings.ExternalDependencyStyle : new StyleResolver(settings).Resolve(
            new TypeNode(node.Id, node.ProjectId ?? string.Empty, node.DisplayText, node.SemanticTypeIdentity, node.NodeKind));
        var shape = style.Shape == "rounded" ? "rounded=1;whiteSpace=wrap;html=1;" : $"shape={style.Shape};whiteSpace=wrap;html=1;";
        return $"{shape}fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};shadow={(style.Shadow ? 1 : 0)};{style.ExtraStyle}";
    }

    private static string ProjectStyle(NodeStyle style) =>
        $"shape=swimlane;whiteSpace=wrap;html=1;fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};{style.ExtraStyle}";

    private static string ConnectorStyle(DiagramSettings settings, ArchitecturePhysicalRoute route, ArchitectureRenderNode target)
    {
        var nodeStyle = target.IsExternal ? settings.ExternalDependencyStyle : new StyleResolver(settings).Resolve(
            new TypeNode(target.Id, target.ProjectId ?? string.Empty, target.DisplayText, target.SemanticTypeIdentity, target.NodeKind));
        var stroke = IsColour(nodeStyle.FillColor) ? nodeStyle.FillColor : settings.Connector.StrokeColor;
        return $"edgeStyle=none;noEdgeStyle=1;orthogonal=0;curved=0;html=1;endArrow=block;endFill=1;strokeColor={stroke};strokeWidth={settings.Connector.StrokeWidth};exitPerimeter=0;entryPerimeter=0;";
    }

    private static bool IsColour(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length is 7 or 9 && value[0] == '#' &&
        value.Skip(1).All(character => Uri.IsHexDigit(character));

    private static string FormatRatio(double value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static string BuildProvenanceJson(ArchitecturePlanningGraph graph, ArchitectureCandidatePlan candidate,
        ArchitecturePhysicalScene scene, DrawioPageModel page, IReadOnlyList<ValidationFinding> findings) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            pipeline = "replacement-architecture-renderer",
            candidate = candidate.CandidateId,
            nodes = graph.Nodes.Count,
            links = graph.Links.Count,
            routes = scene.Routes.Count,
            cells = page.Cells.Count,
            pageBounds = scene.PageBounds,
            routeLength = candidate.RouteLength,
            bendCount = candidate.BendCount,
            findings = findings.Select(finding => new { finding.Category, finding.LogicalRouteId, finding.Description })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static string BuildSceneJson(ArchitecturePhysicalScene scene) =>
        System.Text.Json.JsonSerializer.Serialize(scene, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static string BuildPageJson(DrawioPageModel page) =>
        System.Text.Json.JsonSerializer.Serialize(page, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static T Measure<T>(ICollection<PipelineStageMetric> timings, string name, Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        var result = action();
        timer.Stop();
        timings.Add(new PipelineStageMetric(name, timer.ElapsedMilliseconds, 1));
        return result;
    }
}
