using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
        var serializationFindings = ValidateReconstruction(pageModel, reconstructed);
        var findings = scene.Findings.Concat(serializationFindings).ToArray();

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
        while (queue.Count > 0)
        {
            var source = queue.Dequeue();
            foreach (var link in outgoing[source].OrderBy(link => link.Order))
            {
                depths[link.TargetRenderInstanceId] = Math.Max(
                    depths[link.TargetRenderInstanceId], depths[source] + 1);
                if (--remaining[link.TargetRenderInstanceId] == 0) queue.Enqueue(link.TargetRenderInstanceId);
            }
        }

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
        ResolveHorizontalOverlaps(graph, nodeRects, settings);

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
        var cells = graphModel.Element("root")?.Elements("mxCell").Select(cell =>
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
                cell.Attribute("style")?.Value ?? string.Empty, bounds, points, isEdge);
        }).ToArray() ?? Array.Empty<DrawioPageCell>();
        return new DrawioPageModel(cells, new Rect(0, 0, 0, 0), Array.Empty<string>());
    }

    private static IReadOnlyList<ValidationFinding> ValidateReconstruction(DrawioPageModel expected, DrawioPageModel actual)
    {
        var expectedEdges = expected.Cells.Where(cell => cell.IsEdge).ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        var actualEdges = actual.Cells.Where(cell => cell.IsEdge).ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        return expectedEdges.Where(item => !actualEdges.TryGetValue(item.Key, out var actualEdge) ||
                !item.Value.Waypoints.SequenceEqual(actualEdge.Waypoints))
            .Select(item => Finding("SerializationGeometry", item.Key, "Draw.io reconstruction changed or lost route waypoints.", true))
            .ToArray();
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
                if (points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Any(segment => segment.Intersects(node.Value)))
                    findings.Add(Finding("LinkNodeIntersection", route.Link.Link.Id, $"Route intersects node {node.Key}.", true, node.Key));
        }
        foreach (var pair in routes.SelectMany((left, index) => routes.Skip(index + 1)
                     .Select(right => (left, right))))
        {
            var shared = pair.left.Points.Zip(pair.left.Points.Skip(1), (start, end) => new Segment(start, end))
                .SelectMany(first => pair.right.Points.Zip(pair.right.Points.Skip(1), (start, end) => new Segment(start, end))
                    .Where(second => first.IsHorizontal == second.IsHorizontal && first.IsOrthogonal &&
                        first.OverlapLength(second) > 0))
                .FirstOrDefault();
            if (shared.Length > 0)
                findings.Add(FindingWithOtherRoute("SharedSegment", pair.left.Link.Link.Id,
                    pair.right.Link.Link.Id, "Routes share a non-zero collinear segment.", true));
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
        var depthByNodeId = graph.Nodes.ToDictionary(node => node.Node.Id, node => node.Depth, StringComparer.Ordinal);
        var laneByLinkId = graph.Links
            .GroupBy(link => (SourceDepth: depthByNodeId[link.SourcePlanningNodeId], TargetDepth: depthByNodeId[link.TargetPlanningNodeId]))
            .SelectMany(group => group.OrderBy(link => link.Order).Select((link, index) => new { link.Link.Id, Index = index }))
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        foreach (var link in graph.Links.OrderBy(link => link.Order))
        {
            var source = terminals.Single(terminal => terminal.LinkId == link.Link.Id && terminal.IsSource).Point;
            var target = terminals.Single(terminal => terminal.LinkId == link.Link.Id && !terminal.IsSource).Point;
            var sourceNode = nodes[link.SourcePlanningNodeId];
            var targetNode = nodes[link.TargetPlanningNodeId];
            IReadOnlyList<Point> points;
            if (targetNode.Y > sourceNode.Y)
            {
                var y = source.Y + settings.Layout.LinkPadding +
                    laneByLinkId[link.Link.Id] * settings.Layout.ParallelLaneSpacing;
                points = Normalize(new[] { source, new Point(source.X, y), new Point(target.X, y), target });
            }
            else
            {
                var laneX = outerX + routes.Count * settings.Layout.ParallelLaneSpacing;
                var y1 = source.Y + settings.Layout.LinkPadding;
                var y2 = target.Y - settings.Layout.LinkPadding;
                points = Normalize(new[] { source, new Point(source.X, y1), new Point(laneX, y1), new Point(laneX, y2), new Point(target.X, y2), target });
            }
            routes.Add(new ArchitecturePhysicalRoute(link, points, link.Topology,
                $"slot:{link.Link.Id}", $"column:{link.Link.Id}"));
        }
        return routes;
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

    private static void ResolveHorizontalOverlaps(ArchitecturePlanningGraph graph, Dictionary<string, Rect> nodes, DiagramSettings settings)
    {
        foreach (var layer in graph.Nodes.GroupBy(node => node.Depth).OrderBy(layer => layer.Key))
        {
            var cursor = settings.Layout.ContainerPadding * 2;
            foreach (var node in layer.OrderBy(node => nodes[node.Node.Id].X).ThenBy(node => node.Order))
            {
                var rect = nodes[node.Node.Id];
                if (rect.X < cursor) rect = rect with { X = cursor };
                nodes[node.Node.Id] = rect;
                cursor = rect.Right + settings.Layout.HorizontalSpacing;
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
