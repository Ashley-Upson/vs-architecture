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

        var planning = Measure(timings, "replacement placement graph", () => BuildPlacementGraph(graph, settings));
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
                $"Replacement Architecture candidate rejected. {BuildPlacementSummary(planning, candidate, findings)} " +
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

    private static ArchitecturePlacementGraph BuildPlacementGraph(
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
        var planningNodes = nodes.Select(node => new ArchitecturePlacementNode(
            node,
            node.Id,
            node.SemanticNodeId,
            node.ProjectId,
            node.IsExternal,
            node.Occurrence,
            node.DuplicationReason,
            node.Order,
            node.Order,
            depths[node.Id],
            widthByNode[node.Id],
            settings.Layout.NodeHeight,
            node.PlacementParentRenderId ?? node.SemanticNodeId,
            IsBaselineNode(node, settings.Layout.BaselineAlignmentPattern),
            graph.TraversalRootSemanticIds.Contains(node.SemanticNodeId, StringComparer.Ordinal),
            node.PlacementParentRenderId,
            node.PlacementParentRenderId)).ToArray();
        var planningLinks = links.Select(link => new ArchitecturePlacementLink(
            link,
            link.Order,
            Topology(link, depths),
            link.SourceRenderInstanceId,
            link.TargetRenderInstanceId)).ToArray();
        return new ArchitecturePlacementGraph(graph, planningNodes, planningLinks);
    }

    private static ArchitectureCandidatePlan BuildCandidate(
        ArchitecturePlacementGraph graph,
        DiagramSettings settings)
    {
        var nodeRects = PlaceCanonicalUnits(graph, settings);
        var expansionEvents = Array.Empty<ArchitectureExpansionEvent>();

        var projectRects = BuildProjectRects(graph, nodeRects, settings);
        var terminals = AllocateTerminals(graph, nodeRects, settings);
        var routes = BuildRoutes(graph, nodeRects, terminals, projectRects, settings);
        var findings = ValidateScene(graph, nodeRects, projectRects, terminals, routes, settings);
        var expansionDiagnostics = BuildExpansionDiagnostics(graph, nodeRects, routes, expansionEvents, settings);
        return new ArchitectureCandidatePlan(
            "replacement-001",
            new ReadOnlyDictionary<string, Rect>(nodeRects),
            new ReadOnlyDictionary<string, Rect>(projectRects),
            terminals,
            routes,
            routes.SelectMany(route => projectRects.Keys.Where(project =>
                graph.RenderNodes.Any(node => node.Id == route.Link.Link.SourceRenderInstanceId && node.ProjectId == project) &&
                graph.RenderNodes.Any(node => node.Id == route.Link.Link.TargetRenderInstanceId && node.ProjectId != project)))
                .Distinct(StringComparer.Ordinal).ToArray(),
            new[]
            {
                "Candidate ordering: canonical placement graph discovery order",
                "Candidate placement: contiguous positional subtrees and isolated component grid",
                "Candidate placement: baseline members share one final Y coordinate",
                "Candidate routing: topology-specific orthogonal routes",
                "Candidate validation: absolute physical scene before Draw.io projection"
            },
            findings,
            routes.Sum(route => route.Points.Zip(route.Points.Skip(1), (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)).Sum()),
            routes.Sum(route => Math.Max(0, route.Points.Count - 2)),
            expansionDiagnostics);
    }

    private static ArchitecturePhysicalScene BuildScene(
        ArchitecturePlacementGraph graph,
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
        ArchitecturePlacementGraph graph,
        ArchitecturePhysicalScene scene,
        DiagramSettings settings)
    {
        var cells = new List<DrawioPageCell>
        {
            new("0", "", null, null, string.Empty, string.Empty, null, Array.Empty<Point>(), false),
            new("1", "0", null, null, string.Empty, string.Empty, null, Array.Empty<Point>(), false)
        };
        foreach (var project in graph.Projects.OrderBy(project => project.Order))
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
            var source = graph.RenderNodes.Single(node => node.Id == route.Link.Link.SourceRenderInstanceId);
            var target = graph.RenderNodes.Single(node => node.Id == route.Link.Link.TargetRenderInstanceId);
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
        ArchitecturePlacementGraph graph,
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
        ArchitecturePlacementGraph graph,
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
        ArchitecturePlacementGraph graph,
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
        ArchitecturePlacementGraph graph,
        IReadOnlyDictionary<string, Rect> nodes,
        IReadOnlyList<ArchitectureTerminal> terminals,
        IReadOnlyDictionary<string, Rect> projects,
        DiagramSettings settings)
    {
        var routes = new List<ArchitecturePhysicalRoute>();
        var usedChannelY = new HashSet<int>();
        var outerX = nodes.Values.Select(node => node.Right).DefaultIfEmpty(0).Max() + settings.Layout.ParallelLaneSpacing;
        foreach (var link in graph.Links.OrderBy(link => link.Order))
        {
            var source = terminals.Single(terminal => terminal.LinkId == link.Link.Id && terminal.IsSource).Point;
            var target = terminals.Single(terminal => terminal.LinkId == link.Link.Id && !terminal.IsSource).Point;
            var sourceNode = nodes[link.SourcePlanningNodeId];
            var targetNode = nodes[link.TargetPlanningNodeId];
            var downward = targetNode.Y > sourceNode.Y;
            var laneX = outerX + routes.Count * settings.Layout.ParallelLaneSpacing;
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
                $"slot:{link.Link.Id}", $"column:{link.Link.Id}", laneX, sourceChannelY, targetChannelY));
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

    private static Dictionary<string, Rect> PlaceCanonicalUnits(
        ArchitecturePlacementGraph graph,
        DiagramSettings settings)
    {
        var placementNodes = graph.Nodes.ToDictionary(node => node.RenderInstanceId, StringComparer.Ordinal);
        var ownedChildren = placementNodes.Values.ToDictionary(node => node.RenderInstanceId,
            _ => new List<ArchitecturePlacementNode>(), StringComparer.Ordinal);
        foreach (var node in placementNodes.Values)
        {
            if (node.PositionalOwnerId is not null && ownedChildren.TryGetValue(node.PositionalOwnerId, out var children))
                children.Add(node);
        }

        foreach (var children in ownedChildren.Values)
            children.Sort(ComparePlacementOrder);

        var connected = new HashSet<string>(graph.Links.SelectMany(link =>
            new[] { link.SourcePlanningNodeId, link.TargetPlanningNodeId }), StringComparer.Ordinal);
        var roots = placementNodes.Values
            .Where(node => connected.Contains(node.RenderInstanceId) &&
                (node.PositionalOwnerId is null || !placementNodes.ContainsKey(node.PositionalOwnerId)))
            .OrderBy(node => node.Order).ThenBy(node => node.RenderInstanceId, StringComparer.Ordinal)
            .ToArray();
        var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var x = settings.Layout.ContainerPadding * 2;
        var y = settings.Layout.ContainerPadding * 2;
        foreach (var root in roots)
        {
            var width = MeasureUnit(root, ownedChildren, settings);
            LayoutUnit(root, x, y, ownedChildren, rects, settings);
            x += width + settings.Layout.StandaloneGroupSpacing;
        }

        var standalone = placementNodes.Values.Where(node => !rects.ContainsKey(node.RenderInstanceId))
            .OrderBy(node => node.Order).ThenBy(node => node.RenderInstanceId, StringComparer.Ordinal).ToArray();
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standalone.Length)));
        var standaloneX = rects.Values.Select(rect => rect.Right).DefaultIfEmpty(x).Max() + settings.Layout.StandaloneGroupSpacing;
        for (var index = 0; index < standalone.Length; index++)
        {
            var node = standalone[index];
            rects[node.RenderInstanceId] = new Rect(
                standaloneX + (index % columns) * (node.Width + settings.Layout.HorizontalSpacing),
                y + (index / columns) * (node.Height + settings.Layout.VerticalSpacing),
                node.Width,
                node.Height);
        }

        ResolveUnitCollisions(graph, rects, settings);
        AlignBaselineUnits(graph, rects, settings);
        // Baseline alignment is a group movement. Repack complete positional units afterward;
        // the repack is horizontal only, so it preserves the shared baseline Y coordinate.
        ResolveUnitCollisions(graph, rects, settings);
        return rects;
    }

    private static int MeasureUnit(
        ArchitecturePlacementNode node,
        IReadOnlyDictionary<string, List<ArchitecturePlacementNode>> children,
        DiagramSettings settings)
    {
        var childWidth = children[node.RenderInstanceId]
            .Sum(child => MeasureUnit(child, children, settings));
        if (children[node.RenderInstanceId].Count > 1)
            childWidth += (children[node.RenderInstanceId].Count - 1) * settings.Layout.HorizontalSpacing;
        return Math.Max(node.Width, childWidth);
    }

    private static void LayoutUnit(
        ArchitecturePlacementNode node,
        int left,
        int top,
        IReadOnlyDictionary<string, List<ArchitecturePlacementNode>> children,
        IDictionary<string, Rect> rects,
        DiagramSettings settings)
    {
        var childNodes = children[node.RenderInstanceId];
        var measuredWidth = MeasureUnit(node, children, settings);
        var rect = new Rect(left + (measuredWidth - node.Width) / 2, top, node.Width, node.Height);
        rects[node.RenderInstanceId] = rect;
        if (childNodes.Count == 0) return;

        var childWidth = childNodes.Sum(child => MeasureUnit(child, children, settings)) +
            (childNodes.Count - 1) * settings.Layout.HorizontalSpacing;
        var childLeft = left + (measuredWidth - childWidth) / 2;
        foreach (var child in childNodes)
        {
            var width = MeasureUnit(child, children, settings);
            LayoutUnit(child, childLeft, rect.Bottom + settings.Layout.VerticalSpacing, children, rects, settings);
            childLeft += width + settings.Layout.HorizontalSpacing;
        }
    }

    private static void ResolveUnitCollisions(
        ArchitecturePlacementGraph graph,
        Dictionary<string, Rect> rects,
        DiagramSettings settings)
    {
        var ordered = graph.Nodes.OrderBy(node => node.Order)
            .ThenBy(node => node.RenderInstanceId, StringComparer.Ordinal).ToArray();
        for (var pass = 0; pass < ordered.Length; pass++)
        {
            var moved = false;
            for (var firstIndex = 0; firstIndex < ordered.Length; firstIndex++)
            for (var secondIndex = firstIndex + 1; secondIndex < ordered.Length; secondIndex++)
            {
                var first = ordered[firstIndex];
                var second = ordered[secondIndex];
                if (!Overlaps(rects[first.RenderInstanceId], rects[second.RenderInstanceId])) continue;
                var unit = second.RenderInstanceId;
                var delta = rects[first.RenderInstanceId].Right + settings.Layout.HorizontalSpacing - rects[unit].X;
                if (delta <= 0) delta = settings.Layout.HorizontalSpacing;
                MoveUnit(graph, unit, rects, delta, 0);
                moved = true;
            }
            if (!moved) break;
        }
    }

    private static void AlignBaselineUnits(
        ArchitecturePlacementGraph graph,
        Dictionary<string, Rect> rects,
        DiagramSettings settings)
    {
        var baseline = graph.Nodes.Where(node => node.IsBaseline).ToArray();
        if (baseline.Length == 0) return;
        var targetY = baseline.Max(node => rects[node.RenderInstanceId].Y);
        foreach (var node in baseline.OrderBy(node => node.Order))
        {
            var delta = targetY - rects[node.RenderInstanceId].Y;
            if (delta != 0) MoveUnit(graph, node.RenderInstanceId, rects, 0, delta);
        }
    }

    private static void MoveUnit(
        ArchitecturePlacementGraph graph,
        string unitId,
        IDictionary<string, Rect> rects,
        int deltaX,
        int deltaY)
    {
        var members = new HashSet<string>(StringComparer.Ordinal) { unitId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in graph.Nodes)
                if (node.PositionalOwnerId is not null && members.Contains(node.PositionalOwnerId) && members.Add(node.RenderInstanceId))
                    changed = true;
        }
        foreach (var id in members)
            rects[id] = rects[id] with { X = rects[id].X + deltaX, Y = rects[id].Y + deltaY };
    }

    private static int ComparePlacementOrder(ArchitecturePlacementNode left, ArchitecturePlacementNode right)
    {
        var order = left.Order.CompareTo(right.Order);
        return order != 0 ? order : string.CompareOrdinal(left.RenderInstanceId, right.RenderInstanceId);
    }

    private static Dictionary<string, Rect> BuildProjectRects(ArchitecturePlacementGraph graph, IReadOnlyDictionary<string, Rect> nodes, DiagramSettings settings)
    {
        return graph.Projects.Where(project => nodes.Any(node => graph.Nodes.Any(planning => planning.RenderInstanceId == node.Key && planning.ProjectId == project.Id)))
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

    private static ArchitectureExpansionDiagnostics BuildExpansionDiagnostics(
        ArchitecturePlacementGraph graph,
        IReadOnlyDictionary<string, Rect> nodes,
        IReadOnlyList<ArchitecturePhysicalRoute> routes,
        IReadOnlyList<ArchitectureExpansionEvent> events,
        DiagramSettings settings)
    {
        var orderedX = nodes.Values.Select(rect => rect.X).OrderBy(x => x).ToArray();
        var gaps = nodes.Values.OrderBy(rect => rect.X).Zip(nodes.Values.OrderBy(rect => rect.X).Skip(1),
                (left, right) => new { FromX = left.Right, ToX = right.X, Gap = right.X - left.Right })
            .Where(gap => gap.Gap > settings.Layout.HorizontalSpacing)
            .OrderByDescending(gap => gap.Gap)
            .Take(10)
            .Cast<object>()
            .ToArray();
        var widthContributions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["node-layout"] = nodes.Values.Select(rect => rect.Right).DefaultIfEmpty(0).Max(),
            ["local-node-shifts"] = events.Sum(item => Math.Max(0, item.ToX - item.FromX)),
            ["route-columns"] = routes.Select(route => route.LaneX).DefaultIfEmpty(0).Max()
        };
        var maximumColumn = routes.Select(route => route.LaneX).DefaultIfEmpty(0).Max();
        return new ArchitectureExpansionDiagnostics(
            events.ToArray(), gaps, nodes.Values.Select(rect => rect.Right).DefaultIfEmpty(0).Max(),
            orderedX.Length == 0 ? 0 : orderedX[orderedX.Length / 2],
            routes.Select(route => route.LaneX).Distinct().Count(), maximumColumn, widthContributions);
    }

    private static string Topology(ArchitectureRenderLink link, IReadOnlyDictionary<string, int> depths) =>
        depths[link.TargetRenderInstanceId] > depths[link.SourceRenderInstanceId] ? "downward" : "return";

    private static bool IsBaselineNode(ArchitectureRenderNode node, string? pattern) =>
        !string.IsNullOrWhiteSpace(pattern) &&
        (Regex.IsMatch(node.DisplayText ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
         Regex.IsMatch(node.SemanticTypeIdentity ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

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

    private static string BuildProvenanceJson(ArchitecturePlacementGraph graph, ArchitectureCandidatePlan candidate,
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
            placement = new
            {
                nodeBounds = candidate.NodeRects,
                projectDimensions = candidate.ProjectRects.ToDictionary(item => item.Key,
                    item => new { item.Value.X, item.Value.Y, item.Value.Width, item.Value.Height }, StringComparer.Ordinal),
                subtreeGroups = graph.Nodes.GroupBy(node => node.PlacementGroup, StringComparer.Ordinal)
                    .OrderBy(group => group.Min(node => node.Order))
                    .Select(group => new
                    {
                        group.Key,
                        nodes = group.OrderBy(node => node.Order)
                            .Select(node => node.RenderInstanceId).ToArray(),
                        bounds = group.Select(node => candidate.NodeRects[node.RenderInstanceId])
                            .Aggregate((left, right) => Union(left, right))
                    }),
                largestGaps = candidate.ExpansionDiagnostics.WidestGaps
            },
            expansion = candidate.ExpansionDiagnostics,
            longestRoutes = scene.Routes
                .OrderByDescending(route => route.Points.Zip(route.Points.Skip(1), (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)).Sum())
                .Take(20)
                .Select(route => new
                {
                    route.Link.Link.Id,
                    source = route.Link.Link.SourceSemanticId,
                    target = route.Link.Link.TargetSemanticId,
                    route.Topology,
                    directManhattan = DirectManhattan(route.Points[0], route.Points[route.Points.Count - 1]),
                    actualLength = route.Points.Zip(route.Points.Skip(1), (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)).Sum(),
                    bends = Math.Max(0, route.Points.Count - 2),
                    route.LaneX,
                    route.SourceChannelY,
                    route.TargetChannelY,
                    maxX = route.Points.Max(point => point.X)
                }),
            findings = findings.Select(finding => new { finding.Category, finding.LogicalRouteId, finding.Description })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static int DirectManhattan(Point source, Point target) =>
        Math.Abs(source.X - target.X) + Math.Abs(source.Y - target.Y);

    private static string BuildSceneJson(ArchitecturePhysicalScene scene) =>
        System.Text.Json.JsonSerializer.Serialize(scene, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static string BuildPageJson(DrawioPageModel page) =>
        System.Text.Json.JsonSerializer.Serialize(page, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static string BuildPlacementSummary(
        ArchitecturePlacementGraph graph,
        ArchitectureCandidatePlan candidate,
        IReadOnlyList<ValidationFinding> findings)
    {
        var bounds = candidate.NodeRects.Values.Aggregate(
            new Rect(0, 0, 0, 0), Union);
        var largestGap = candidate.NodeRects.Values.OrderBy(rect => rect.X)
            .Zip(candidate.NodeRects.Values.OrderBy(rect => rect.X).Skip(1),
                (left, right) => right.X - left.Right)
            .DefaultIfEmpty(0).Max();
        var projectDimensions = string.Join(",", candidate.ProjectRects.Select(item =>
            $"{item.Key}:{item.Value.Width}x{item.Value.Height}"));
        var overlaps = findings.Where(finding => finding.Category == "NodeOverlap").ToArray();
        var overlapSummary = overlaps.Length == 0
            ? "none"
            : string.Join("|", overlaps.Select(overlap =>
            {
                var first = graph.Nodes.SingleOrDefault(node => node.RenderInstanceId == overlap.LogicalRouteId);
                var second = graph.Nodes.SingleOrDefault(node => node.RenderInstanceId == overlap.OtherNodeId);
                return $"{overlap.LogicalRouteId}[{first?.SemanticNodeId},parent={first?.PositionalOwnerId},baseline={first?.IsBaseline},external={first?.IsExternal}]" +
                    $"/{overlap.OtherNodeId}[{second?.SemanticNodeId},parent={second?.PositionalOwnerId},baseline={second?.IsBaseline},external={second?.IsExternal}]";
            }));
        return $"Placement nodes={candidate.NodeRects.Count}, bounds={bounds.Width}x{bounds.Height}, projects={projectDimensions}, largestGap={largestGap}, nodeOverlaps={overlaps.Length} ({overlapSummary}).";
    }

    private static T Measure<T>(ICollection<PipelineStageMetric> timings, string name, Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        var result = action();
        timer.Stop();
        timings.Add(new PipelineStageMetric(name, timer.ElapsedMilliseconds, 1));
        return result;
    }
}
