using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

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
            var architectureRoot = new ArchitectureGenerator(this).Generate(layout, ownership);
            var dataModelRoot = new DataModelGenerator(this).Generate(layout.Graph.DataModels);

            var file = new XElement("mxfile",
                new XAttribute("host", "StandardIo.ArchitectureDiagram"),
                new XElement("diagram", new XAttribute("name", "Architecture"), GraphModel(architectureRoot)),
                new XElement("diagram", new XAttribute("name", "Data Model"), GraphModel(dataModelRoot)));

            return new XDocument(file).ToString(SaveOptions.DisableFormatting);
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
                layout.Traversals.Traversals.TryGetValue(segment.LogicalEdgeId, out var traversal);
                root.Add(Edge(segment, traversal));
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

        private sealed class DataModelGenerator
        {
            private readonly DiagramFileBuilder _builder;

            public DataModelGenerator(DiagramFileBuilder builder)
            {
                _builder = builder;
            }

            public XElement Generate(IReadOnlyList<RenderNode> models)
            {
                return _builder.DataModelRoot(models);
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

        private static string DataModelPropertyLabel(TypeProperty property)
        {
            return string.IsNullOrWhiteSpace(property.TypeName)
                ? property.Name
                : $"{property.TypeName}: {property.Name}";
        }

        private static string DataModelNodeId(string id) => $"data_model_{id}";

        private static string DataModelContainerStyle()
        {
            return "shape=rectangle;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#9fb7d5;strokeWidth=1;";
        }

        private static string DataModelHeaderStyle()
        {
            return "shape=rectangle;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=8;fontStyle=1;fontColor=#ffffff;fillColor=#2f5f97;strokeColor=#9fb7d5;";
        }

        private static string DataModelRowStyle(int index)
        {
            var fill = index % 2 == 0 ? "#f7fbff" : "#e6f0fb";
            return $"shape=rectangle;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=8;fontColor=#111111;fillColor={fill};strokeColor=#9fb7d5;";
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

        private XElement Edge(PhysicalEdgeSegment segment, EdgeTraversal? traversal)
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

        private XElement Edge(LinkLayout link)
        {
            return new XElement("mxCell",
                new XAttribute("id", link.Link.Id),
                new XAttribute("style", BuildConnectorStyle(_settings.Connector, link)),
                new XAttribute("edge", "1"),
                new XAttribute("parent", "1"),
                new XAttribute("source", link.Link.SourceId),
                new XAttribute("target", link.Link.TargetId),
                new XElement("mxGeometry",
                    new XAttribute("relative", "1"),
                    new XAttribute("as", "geometry"),
                    new XElement("Array",
                        new XAttribute("as", "points"),
                        link.Points.Select(point => new XElement("mxPoint",
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

private XElement DataModelRoot(IReadOnlyList<RenderNode> models)
        {
            var root = new XElement("root",
                new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
            var modelsById = models.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var modelIds = new HashSet<string>(models.Select(model => model.Id), StringComparer.Ordinal);
            var orderedModels = models.OrderBy(model => model.FullName, StringComparer.Ordinal).ToArray();
            var rects = PositionDataModelTables(orderedModels, _settings.Layout);

            foreach (var model in orderedModels)
            {
                AddDataModelTable(root, model, rects[model.Id]);
            }

            var edgeOrder = 0;
            var routedSegments = new List<Segment>();
            foreach (var model in models)
            {
                if (!rects.TryGetValue(model.Id, out var source))
                {
                    continue;
                }

                foreach (var property in model.Properties.Where(property => property.TypeId is not null && modelIds.Contains(property.TypeId!)))
                {
                    var target = rects[property.TypeId!];
                    var routedRelationship = RouteDataModelRelationship(
                        source,
                        target,
                        rects
                            .Where(item => item.Key != model.Id && item.Key != property.TypeId)
                            .Select(item => item.Value.Inflate(_settings.Layout.LinkPadding))
                            .ToArray(),
                        routedSegments,
                        edgeOrder,
                        _settings.Layout);
                    var link = new LinkLayout(
                        new RenderLink($"data_model_edge_{edgeOrder++}", DataModelNodeId(model.Id), DataModelNodeId(property.TypeId!), "property", edgeOrder),
                        routedRelationship.Terminals.Source,
                        routedRelationship.Terminals.Target,
                        routedRelationship.Route,
                        Ratio(routedRelationship.Terminals.Source.X, source),
                        Ratio(routedRelationship.Terminals.Target.X, target),
                        RatioY(routedRelationship.Terminals.Source.Y, source),
                        RatioY(routedRelationship.Terminals.Target.Y, target));
                    root.Add(Edge(link));
                    routedSegments.AddRange(Segments(new[] { routedRelationship.Terminals.Source }.Concat(routedRelationship.Route).Concat(new[] { routedRelationship.Terminals.Target }).ToArray()));
                }
            }

            return root;
        }

        private static Dictionary<string, Rect> PositionDataModelTables(
            IReadOnlyList<RenderNode> models,
            LayoutSettings layout)
        {
            var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var modelsById = models.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var relationships = DataModelRelationships(modelsById);
            var adjacency = DataModelAdjacency(models, relationships);
            var connectedComponents = DataModelConnectedComponents(models, adjacency)
                .Where(component => component.Count > 1)
                .OrderByDescending(component => component.Count)
                .ThenBy(component => component.Min(model => model.FullName), StringComparer.Ordinal)
                .ToArray();
            var connectedIds = new HashSet<string>(connectedComponents.SelectMany(component => component.Select(model => model.Id)), StringComparer.Ordinal);
            var isolatedModels = models
                .Where(model => !connectedIds.Contains(model.Id))
                .OrderBy(model => model.FullName, StringComparer.Ordinal)
                .ToArray();

            PackDataModelComponents(rects, connectedComponents, relationships, layout);
            var y = rects.Values.Select(rect => rect.Bottom).DefaultIfEmpty(layout.DataModelCanvasMargin).Max();

            if (isolatedModels.Length > 0)
            {
                y += rects.Count == 0 ? 0 : layout.DataModelComponentSpacing;
                var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(isolatedModels.Length)));
                var rowGap = DataModelRowGap(layout);
                var columnWidth = DataModelColumnStep(layout);
                foreach (var rowModels in isolatedModels.Select((model, index) => new { model, index }).GroupBy(item => item.index / columns))
                {
                    var row = rowModels.ToArray();
                    var rowHeight = row
                        .Select(item => DataModelTableHeight(item.model, layout))
                        .DefaultIfEmpty(0)
                        .Max();

                    foreach (var item in row)
                    {
                        var column = item.index % columns;
                        rects[item.model.Id] = new Rect(
                            layout.DataModelCanvasMargin + column * columnWidth,
                            y,
                            layout.DataModelTableWidth,
                            DataModelTableHeight(item.model, layout));
                    }

                    y += rowHeight + rowGap;
                }
            }

            ResolveDataModelTableOverlaps(rects, layout);
            return rects;
        }

        private static Dictionary<string, HashSet<string>> DataModelAdjacency(
            IReadOnlyList<RenderNode> models,
            IReadOnlyList<DataModelRelationship> relationships)
        {
            var adjacency = models.ToDictionary(
                model => model.Id,
                _ => new HashSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);

            foreach (var relationship in relationships)
            {
                adjacency[relationship.SourceId].Add(relationship.TargetId);
                adjacency[relationship.TargetId].Add(relationship.SourceId);
            }

            return adjacency;
        }

        private static void PackDataModelComponents(
            Dictionary<string, Rect> rects,
            IReadOnlyList<IReadOnlyList<RenderNode>> components,
            IReadOnlyList<DataModelRelationship> relationships,
            LayoutSettings layout)
        {
            var x = layout.DataModelCanvasMargin;
            var y = layout.DataModelCanvasMargin;
            var rowBottom = y;

            foreach (var component in components)
            {
                var componentRects = PositionDataModelComponent(component, relationships, layout);
                var bounds = DataModelBounds(componentRects.Values);
                var rowWidth = Math.Max(layout.DataModelComponentRowWidth, layout.DataModelCanvasMargin * 2 + bounds.Width);

                if (x > layout.DataModelCanvasMargin && x + bounds.Width > rowWidth)
                {
                    x = layout.DataModelCanvasMargin;
                    y = rowBottom + layout.DataModelComponentSpacing;
                    rowBottom = y;
                }

                foreach (var rect in componentRects)
                {
                    rects[rect.Key] = OffsetRect(rect.Value, x - bounds.X, y - bounds.Y);
                }

                x += bounds.Width + layout.DataModelComponentSpacing;
                rowBottom = Math.Max(rowBottom, y + bounds.Height);
            }
        }

        private static IReadOnlyList<DataModelRelationship> DataModelRelationships(IReadOnlyDictionary<string, RenderNode> modelsById)
        {
            return modelsById.Values
                .SelectMany(model => model.Properties
                    .Where(property => property.TypeId is not null && modelsById.ContainsKey(property.TypeId!))
                    .Select(property => new DataModelRelationship(model.Id, property.TypeId!, property.Name)))
                .OrderBy(relationship => modelsById[relationship.SourceId].FullName, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.PropertyName, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<IReadOnlyList<RenderNode>> DataModelConnectedComponents(
            IReadOnlyList<RenderNode> models,
            IReadOnlyDictionary<string, HashSet<string>> adjacency)
        {
            var modelsById = models.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var components = new List<IReadOnlyList<RenderNode>>();

            foreach (var model in models.OrderBy(model => model.FullName, StringComparer.Ordinal))
            {
                if (!visited.Add(model.Id))
                {
                    continue;
                }

                var queue = new Queue<string>();
                var component = new List<RenderNode>();
                queue.Enqueue(model.Id);

                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    component.Add(modelsById[id]);

                    foreach (var relatedId in adjacency[id].OrderBy(id => modelsById[id].FullName, StringComparer.Ordinal))
                    {
                        if (visited.Add(relatedId))
                        {
                            queue.Enqueue(relatedId);
                        }
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private static Dictionary<string, Rect> PositionDataModelComponent(
            IReadOnlyList<RenderNode> component,
            IReadOnlyList<DataModelRelationship> relationships,
            LayoutSettings layout)
        {
            var componentIds = new HashSet<string>(component.Select(model => model.Id), StringComparer.Ordinal);
            var componentRelationships = relationships
                .Where(relationship => componentIds.Contains(relationship.SourceId) && componentIds.Contains(relationship.TargetId))
                .ToArray();
            var modelsById = component.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var adjacency = DataModelAdjacency(component, componentRelationships);
            var outgoing = componentRelationships
                .GroupBy(relationship => relationship.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(relationship => relationship.TargetId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var incoming = componentRelationships
                .GroupBy(relationship => relationship.TargetId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(relationship => relationship.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var root = SelectDataModelHub(component, outgoing, incoming);
            var levels = DataModelLevels(root, adjacency, modelsById, out var parents);
            var ringStep = DataModelRingStep(component, layout);
            var maxRadius = levels.Values
                .GroupBy(level => level)
                .Max(group => DataModelRingRadius(
                    group.Key,
                    component.Where(model => levels[model.Id] == group.Key).ToArray(),
                    ringStep,
                    layout));
            var center = new Point(maxRadius + layout.DataModelCanvasMargin, maxRadius + layout.DataModelCanvasMargin);
            var rects = new Dictionary<string, Rect>(StringComparer.Ordinal)
            {
                [root.Id] = CenterDataModelTable(root, center, layout)
            };

            foreach (var group in DataModelLevelGroups(component, levels))
            {
                var groupModels = OrderDataModelRing(group, rects, parents, center, adjacency, modelsById).ToArray();
                var level = levels[groupModels[0].Id];
                var radius = DataModelRingRadius(level, groupModels, ringStep, layout);

                for (var index = 0; index < groupModels.Length; index++)
                {
                    var angle = DataModelAngle(groupModels[index], index, groupModels, level, rects, parents, center);
                    rects[groupModels[index].Id] = PositionDataModelTable(groupModels[index], center, radius, angle, layout);
                }
            }

            return NormalizeDataModelComponent(rects);
        }

        private static RenderNode SelectDataModelHub(
            IReadOnlyList<RenderNode> component,
            IReadOnlyDictionary<string, string[]> outgoing,
            IReadOnlyDictionary<string, string[]> incoming)
        {
            return component
                .OrderByDescending(model => DataModelDegree(model.Id, outgoing, incoming))
                .ThenByDescending(model => outgoing.TryGetValue(model.Id, out var outIds) ? outIds.Length : 0)
                .ThenBy(model => model.FullName, StringComparer.Ordinal)
                .First();
        }

        private static Dictionary<string, int> DataModelLevels(
            RenderNode root,
            IReadOnlyDictionary<string, HashSet<string>> adjacency,
            IReadOnlyDictionary<string, RenderNode> modelsById,
            out Dictionary<string, string> parents)
        {
            var levels = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [root.Id] = 0
            };
            parents = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(root.Id);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                var level = levels[id];

                foreach (var relatedId in adjacency[id].OrderByDescending(adjacencyId => adjacency[adjacencyId].Count).ThenBy(adjacencyId => modelsById[adjacencyId].FullName, StringComparer.Ordinal))
                {
                    if (!levels.ContainsKey(relatedId))
                    {
                        levels[relatedId] = level + 1;
                        parents[relatedId] = id;
                        queue.Enqueue(relatedId);
                    }
                }
            }

            return levels;
        }

        private static IEnumerable<IGrouping<int, RenderNode>> DataModelLevelGroups(
            IReadOnlyList<RenderNode> component,
            IReadOnlyDictionary<string, int> levels)
        {
            return component
                .Where(model => levels[model.Id] > 0)
                .GroupBy(model => levels[model.Id])
                .OrderBy(group => group.Key)
                .ToArray();
        }

        private static IEnumerable<RenderNode> OrderDataModelRing(
            IEnumerable<RenderNode> group,
            IReadOnlyDictionary<string, Rect> rects,
            IReadOnlyDictionary<string, string> parents,
            Point center,
            IReadOnlyDictionary<string, HashSet<string>> adjacency,
            IReadOnlyDictionary<string, RenderNode> modelsById)
        {
            return group
                .OrderBy(model => DataModelParentAngle(model.Id, rects, parents, center))
                .ThenByDescending(model => adjacency[model.Id].Count)
                .ThenBy(model => modelsById[model.Id].FullName, StringComparer.Ordinal);
        }

        private static double DataModelParentAngle(
            string modelId,
            IReadOnlyDictionary<string, Rect> rects,
            IReadOnlyDictionary<string, string> parents,
            Point center)
        {
            if (!parents.TryGetValue(modelId, out var parentId) || !rects.TryGetValue(parentId, out var parent))
            {
                return 0;
            }

            return Math.Atan2(parent.CenterY - center.Y, parent.CenterX - center.X);
        }

        private static int DataModelDegree(
            string id,
            IReadOnlyDictionary<string, string[]> outgoing,
            IReadOnlyDictionary<string, string[]> incoming)
        {
            return (outgoing.TryGetValue(id, out var outIds) ? outIds.Length : 0) +
                (incoming.TryGetValue(id, out var inIds) ? inIds.Length : 0);
        }

        private static int DataModelRingStep(IReadOnlyList<RenderNode> component, LayoutSettings layout)
        {
            var maxHeight = component.Max(model => DataModelTableHeight(model, layout));
            var minimum = Math.Max(layout.DataModelTableWidth, maxHeight) +
                layout.DataModelRowSpacing +
                layout.DataModelRelationshipStubLength * 2;
            return Math.Max(layout.DataModelRadialRingSpacing, minimum);
        }

        private static int DataModelRingRadius(
            int level,
            IReadOnlyList<RenderNode> ringModels,
            int ringStep,
            LayoutSettings layout)
        {
            var count = ringModels.Count;
            var maxHeight = ringModels.Select(model => DataModelTableHeight(model, layout)).DefaultIfEmpty(0).Max();
            var minimum = Math.Max(layout.DataModelRadialMinimumRadius, level * ringStep);
            var diagonalRadius = (int)Math.Ceiling(Math.Sqrt(
                layout.DataModelTableWidth * layout.DataModelTableWidth +
                maxHeight * maxHeight)) + layout.DataModelRowSpacing;
            var circumferenceRadius = (int)Math.Ceiling(count *
                (layout.DataModelTableWidth + layout.DataModelRowSpacing) /
                (Math.PI * 2));
            return Math.Max(Math.Max(minimum, diagonalRadius), circumferenceRadius);
        }

        private static double DataModelAngle(
            RenderNode model,
            int index,
            IReadOnlyList<RenderNode> ringModels,
            int level,
            IReadOnlyDictionary<string, Rect> rects,
            IReadOnlyDictionary<string, string> parents,
            Point center)
        {
            if (level <= 1)
            {
                return DataModelRootChildAngle(index, ringModels.Count);
            }

            return DataModelNestedChildAngle(model, ringModels, rects, parents, center);
        }

        private static double DataModelRootChildAngle(int index, int count)
        {
            if (count == 1)
            {
                return 0;
            }

            return count switch
            {
                2 => index == 0 ? 0 : Math.PI,
                3 => new[] { -Math.PI / 2, 0, Math.PI }[index],
                4 => new[] { -Math.PI / 2, 0, Math.PI / 2, Math.PI }[index],
                _ => -Math.PI / 2 + index * (Math.PI * 2 / count)
            };
        }

        private static double DataModelNestedChildAngle(
            RenderNode model,
            IReadOnlyList<RenderNode> ringModels,
            IReadOnlyDictionary<string, Rect> rects,
            IReadOnlyDictionary<string, string> parents,
            Point center)
        {
            var parentId = parents.TryGetValue(model.Id, out var id) ? id : string.Empty;
            var siblings = ringModels
                .Where(item => parents.TryGetValue(item.Id, out var siblingParentId) &&
                    string.Equals(siblingParentId, parentId, StringComparison.Ordinal))
                .OrderBy(item => item.FullName, StringComparer.Ordinal)
                .ToArray();
            var siblingIndex = Array.FindIndex(siblings, item => string.Equals(item.Id, model.Id, StringComparison.Ordinal));
            var offset = DataModelSiblingAngleOffset(Math.Max(0, siblingIndex), siblings.Length);

            return DataModelParentAngle(model.Id, rects, parents, center) + offset;
        }

        private static double DataModelSiblingAngleOffset(int index, int count)
        {
            if (count <= 1)
            {
                return 0;
            }

            var step = Math.Min(0.35, 1.2 / Math.Max(1, count - 1));
            return (index - (count - 1) / 2.0) * step;
        }

        private static Rect PositionDataModelTable(
            RenderNode model,
            Point center,
            int radius,
            double angle,
            LayoutSettings layout)
        {
            var tableCenter = new Point(
                center.X + (int)Math.Round(Math.Cos(angle) * radius),
                center.Y + (int)Math.Round(Math.Sin(angle) * radius));
            return CenterDataModelTable(model, tableCenter, layout);
        }

        private static Rect CenterDataModelTable(RenderNode model, Point center, LayoutSettings layout)
        {
            var height = DataModelTableHeight(model, layout);
            return new Rect(
                center.X - layout.DataModelTableWidth / 2,
                center.Y - height / 2,
                layout.DataModelTableWidth,
                height);
        }

        private static Dictionary<string, Rect> NormalizeDataModelComponent(Dictionary<string, Rect> rects)
        {
            var bounds = DataModelBounds(rects.Values);
            return rects.ToDictionary(
                rect => rect.Key,
                rect => OffsetRect(rect.Value, -bounds.X, -bounds.Y),
                StringComparer.Ordinal);
        }

        private static Rect DataModelBounds(IEnumerable<Rect> rects)
        {
            var array = rects.ToArray();
            var left = array.Min(rect => rect.X);
            var top = array.Min(rect => rect.Y);
            var right = array.Max(rect => rect.Right);
            var bottom = array.Max(rect => rect.Bottom);
            return new Rect(left, top, right - left, bottom - top);
        }

        private static Rect OffsetRect(Rect rect, int dx, int dy)
        {
            return rect with { X = rect.X + dx, Y = rect.Y + dy };
        }

        private static void ResolveDataModelTableOverlaps(
            Dictionary<string, Rect> rects,
            LayoutSettings layout)
        {
            var gap = Math.Max(layout.DataModelMinimumTableGap, layout.LinkPadding * 2);
            for (var attempt = 0; attempt < rects.Count * rects.Count; attempt++)
            {
                if (!PushFirstDataModelOverlap(rects, gap))
                {
                    return;
                }
            }
        }

        private static bool PushFirstDataModelOverlap(Dictionary<string, Rect> rects, int gap)
        {
            var ids = rects.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            for (var leftIndex = 0; leftIndex < ids.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < ids.Length; rightIndex++)
                {
                    if (PushDataModelOverlap(rects, ids[leftIndex], ids[rightIndex], gap))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PushDataModelOverlap(
            Dictionary<string, Rect> rects,
            string leftId,
            string rightId,
            int gap)
        {
            var left = rects[leftId];
            var right = rects[rightId];
            var push = DataModelPush(left, right, gap);
            if (push.X == 0 && push.Y == 0)
            {
                return false;
            }

            rects[rightId] = OffsetRect(right, push.X, push.Y);
            return true;
        }

        private static Point DataModelPush(Rect left, Rect right, int gap)
        {
            var horizontalGap = Math.Max(left.X, right.X) - Math.Min(left.Right, right.Right);
            var verticalGap = Math.Max(left.Y, right.Y) - Math.Min(left.Bottom, right.Bottom);
            var horizontalOverlap = horizontalGap < 0;
            var verticalOverlap = verticalGap < 0;

            if (horizontalOverlap && verticalOverlap)
            {
                return Math.Abs(horizontalGap) <= Math.Abs(verticalGap)
                    ? new Point(Math.Abs(horizontalGap) + gap, 0)
                    : new Point(0, Math.Abs(verticalGap) + gap);
            }

            if (horizontalOverlap && verticalGap < gap)
            {
                return new Point(0, gap - verticalGap);
            }

            if (verticalOverlap && horizontalGap < gap)
            {
                return new Point(gap - horizontalGap, 0);
            }

            return new Point(0, 0);
        }

        private static int DataModelTableHeight(RenderNode model, LayoutSettings layout)
        {
            return layout.DataModelHeaderHeight +
                Math.Max(1, model.Properties.Count) * layout.DataModelPropertyRowHeight;
        }

        private static int DataModelRowGap(LayoutSettings layout)
        {
            return Math.Max(layout.DataModelMinimumTableGap, Math.Max(layout.DataModelRowSpacing, layout.DataModelPropertyRowHeight * 2));
        }

        private static int DataModelColumnStep(LayoutSettings layout)
        {
            return Math.Max(layout.DataModelColumnWidth, layout.DataModelTableWidth + layout.DataModelMinimumTableGap);
        }

        private void AddDataModelTable(XElement root, RenderNode model, Rect rect)
        {
            var tableId = DataModelNodeId(model.Id);
            root.Add(Vertex(tableId, string.Empty, DataModelContainerStyle(), "1", rect));
            root.Add(Vertex(
                $"{tableId}_header",
                model.Name,
                DataModelHeaderStyle(),
                tableId,
                new Rect(0, 0, rect.Width, _settings.Layout.DataModelHeaderHeight)));

            var properties = model.Properties.Count == 0
                ? new[] { new TypeProperty("(no public properties)", string.Empty) }
                : model.Properties;
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties[index];
                root.Add(Vertex(
                    $"{tableId}_row_{index}",
                    DataModelPropertyLabel(property),
                    DataModelRowStyle(index),
                    tableId,
                    new Rect(
                        0,
                        _settings.Layout.DataModelHeaderHeight + index * _settings.Layout.DataModelPropertyRowHeight,
                        rect.Width,
                        _settings.Layout.DataModelPropertyRowHeight)));
            }
        }

        private static DataModelRelationshipRoute RouteDataModelRelationship(
            Rect source,
            Rect target,
            IReadOnlyList<Rect> obstacles,
            IReadOnlyList<Segment> routedSegments,
            int order,
            LayoutSettings layout)
        {
            return DataModelTerminalCandidates(source, target, order, layout)
                .Select(terminals => new DataModelRelationshipRoute(
                    terminals,
                    DataModelRoute(terminals, obstacles, routedSegments, order, layout)))
                .OrderBy(route => DataModelRouteScore(
                    route.Terminals.Source,
                    route.Route,
                    route.Terminals.Target,
                    obstacles,
                    routedSegments,
                    route.Terminals))
                .ThenBy(route => RouteLength(route.Terminals.Source, route.Route, route.Terminals.Target))
                .First();
        }

        private static IReadOnlyList<Point> DataModelRoute(
            DataModelTerminals terminals,
            IReadOnlyList<Rect> obstacles,
            IReadOnlyList<Segment> routedSegments,
            int order,
            LayoutSettings layout)
        {
            var sourcePoint = terminals.Source;
            var targetPoint = terminals.Target;
            var sourceStub = StubPoint(sourcePoint, terminals.SourceSide, layout.DataModelRelationshipStubLength);
            var targetStub = StubPoint(targetPoint, terminals.TargetSide, layout.DataModelRelationshipStubLength);
            return DataModelRouteCandidates(sourceStub, targetStub, obstacles, order, layout)
                .Select(route => SimplifyRoute(route))
                .OrderBy(route => DataModelRouteScore(sourcePoint, route, targetPoint, obstacles, routedSegments, terminals))
                .ThenBy(route => RouteLength(sourcePoint, route, targetPoint))
                .First();
        }

        private static IEnumerable<DataModelTerminals> DataModelTerminalCandidates(
            Rect source,
            Rect target,
            int order,
            LayoutSettings layout)
        {
            foreach (var pair in OrderedDataModelSidePairs(source, target))
            {
                var sourcePoint = DataModelPort(source, pair.Source, target, order, layout);
                var targetPoint = DataModelPort(target, pair.Target, source, order, layout);
                yield return new DataModelTerminals(sourcePoint, targetPoint, pair.Source, pair.Target);
            }
        }

        private static IEnumerable<DataModelSidePair> OrderedDataModelSidePairs(Rect source, Rect target)
        {
            var dx = target.CenterX - source.CenterX;
            var dy = target.CenterY - source.CenterY;
            var horizontal = Math.Abs(dx) >= Math.Abs(dy);
            var primary = horizontal
                ? new DataModelSidePair(dx >= 0 ? DataModelSide.Right : DataModelSide.Left, dx >= 0 ? DataModelSide.Left : DataModelSide.Right)
                : new DataModelSidePair(dy >= 0 ? DataModelSide.Bottom : DataModelSide.Top, dy >= 0 ? DataModelSide.Top : DataModelSide.Bottom);
            yield return primary;

            var secondary = horizontal
                ? new DataModelSidePair(dy >= 0 ? DataModelSide.Bottom : DataModelSide.Top, dy >= 0 ? DataModelSide.Top : DataModelSide.Bottom)
                : new DataModelSidePair(dx >= 0 ? DataModelSide.Right : DataModelSide.Left, dx >= 0 ? DataModelSide.Left : DataModelSide.Right);
            yield return secondary;
            yield return new DataModelSidePair(primary.Target, primary.Source);
            yield return new DataModelSidePair(secondary.Target, secondary.Source);

            foreach (var pair in AllDataModelSidePairs()
                .OrderBy(pair => DataModelSidePairPenalty(pair, primary, secondary)))
            {
                if (pair != primary && pair != secondary)
                {
                    yield return pair;
                }
            }
        }

        private static IEnumerable<DataModelSidePair> AllDataModelSidePairs()
        {
            var sides = new[] { DataModelSide.Left, DataModelSide.Right, DataModelSide.Top, DataModelSide.Bottom };
            foreach (var source in sides)
            {
                foreach (var target in sides)
                {
                    yield return new DataModelSidePair(source, target);
                }
            }
        }

        private static int DataModelSidePairPenalty(
            DataModelSidePair pair,
            DataModelSidePair primary,
            DataModelSidePair secondary)
        {
            if (pair == primary)
            {
                return 0;
            }

            if (pair == secondary)
            {
                return 1;
            }

            return OppositeSide(pair.Source) == pair.Target ? 2 : 3;
        }

        private static Point DataModelPort(
            Rect rect,
            DataModelSide side,
            Rect other,
            int order,
            LayoutSettings layout)
        {
            var offset = DataModelPortOffset(order, layout.DataModelRelationshipPortSpacing);
            return side switch
            {
                DataModelSide.Left => new Point(rect.X, DataModelVerticalPort(rect, other, offset)),
                DataModelSide.Right => new Point(rect.Right, DataModelVerticalPort(rect, other, offset)),
                DataModelSide.Top => new Point(DataModelHorizontalPort(rect, other, offset), rect.Y),
                _ => new Point(DataModelHorizontalPort(rect, other, offset), rect.Bottom)
            };
        }

        private static int DataModelVerticalPort(Rect rect, Rect other, int offset)
        {
            var top = Math.Max(rect.Y + 20, other.Y + 20);
            var bottom = Math.Min(rect.Bottom - 20, other.Bottom - 20);
            var y = top <= bottom
                ? top + (bottom - top) / 2
                : other.CenterY;
            return Clamp(y + offset, rect.Y + 20, rect.Bottom - 20);
        }

        private static int DataModelHorizontalPort(Rect rect, Rect other, int offset)
        {
            var left = Math.Max(rect.X + 20, other.X + 20);
            var right = Math.Min(rect.Right - 20, other.Right - 20);
            var x = left <= right
                ? left + (right - left) / 2
                : other.CenterX;
            return Clamp(x + offset, rect.X + 20, rect.Right - 20);
        }

        private static int DataModelPortOffset(int order, int spacing)
        {
            if (order == 0)
            {
                return 0;
            }

            var distance = ((order + 1) / 2) * spacing;
            return order % 2 == 0 ? -distance : distance;
        }

        private static Point StubPoint(Point point, DataModelSide side, int length)
        {
            return side switch
            {
                DataModelSide.Left => new Point(point.X - length, point.Y),
                DataModelSide.Right => new Point(point.X + length, point.Y),
                DataModelSide.Top => new Point(point.X, point.Y - length),
                _ => new Point(point.X, point.Y + length)
            };
        }

        private static IEnumerable<IReadOnlyList<Point>> DataModelRouteCandidates(
            Point sourceStub,
            Point targetStub,
            IReadOnlyList<Rect> obstacles,
            int order,
            LayoutSettings layout)
        {
            if (sourceStub.X == targetStub.X || sourceStub.Y == targetStub.Y)
            {
                yield return new[] { sourceStub, targetStub };
            }

            yield return new[] { sourceStub, new Point(sourceStub.X, targetStub.Y), targetStub };
            yield return new[] { sourceStub, new Point(targetStub.X, sourceStub.Y), targetStub };

            foreach (var laneX in DataModelLaneXs(sourceStub, targetStub, obstacles, order, layout))
            {
                yield return new[] { sourceStub, new Point(laneX, sourceStub.Y), new Point(laneX, targetStub.Y), targetStub };
            }

            foreach (var laneY in DataModelLaneYs(sourceStub, targetStub, obstacles, order, layout))
            {
                yield return new[] { sourceStub, new Point(sourceStub.X, laneY), new Point(targetStub.X, laneY), targetStub };
            }

            foreach (var laneX in DataModelLaneXs(sourceStub, targetStub, obstacles, order, layout).Take(6))
            {
                foreach (var laneY in DataModelLaneYs(sourceStub, targetStub, obstacles, order, layout).Take(6))
                {
                    yield return new[]
                    {
                        sourceStub,
                        new Point(laneX, sourceStub.Y),
                        new Point(laneX, laneY),
                        new Point(targetStub.X, laneY),
                        targetStub
                    };
                }
            }
        }

        private static IEnumerable<int> DataModelLaneXs(
            Point sourceStub,
            Point targetStub,
            IReadOnlyList<Rect> obstacles,
            int order,
            LayoutSettings layout)
        {
            var laneOffset = (order % 8) * layout.DataModelRelationshipLaneSpacing;
            var minX = Math.Min(sourceStub.X, targetStub.X);
            var maxX = Math.Max(sourceStub.X, targetStub.X);

            foreach (var lane in GapLanes(
                obstacles.Select(obstacle => new Span(obstacle.X, obstacle.Right)).OrderBy(span => span.Start).ToArray(),
                minX,
                maxX,
                layout.DataModelRelationshipStubLength + layout.LinkPadding))
            {
                yield return lane + laneOffset;
            }

            var left = obstacles.Select(obstacle => obstacle.X).DefaultIfEmpty(minX).Min() - layout.DataModelRelationshipSideOffset - laneOffset;
            var right = obstacles.Select(obstacle => obstacle.Right).DefaultIfEmpty(maxX).Max() + layout.DataModelRelationshipSideOffset + laneOffset;
            yield return Math.Abs(sourceStub.X - left) + Math.Abs(targetStub.X - left) < Math.Abs(sourceStub.X - right) + Math.Abs(targetStub.X - right)
                ? left
                : right;
            yield return left;
            yield return right;
        }

        private static IEnumerable<int> DataModelLaneYs(
            Point sourceStub,
            Point targetStub,
            IReadOnlyList<Rect> obstacles,
            int order,
            LayoutSettings layout)
        {
            var laneOffset = (order % 8) * layout.DataModelRelationshipLaneSpacing;
            var minY = Math.Min(sourceStub.Y, targetStub.Y);
            var maxY = Math.Max(sourceStub.Y, targetStub.Y);

            foreach (var lane in GapLanes(
                obstacles.Select(obstacle => new Span(obstacle.Y, obstacle.Bottom)).OrderBy(span => span.Start).ToArray(),
                minY,
                maxY,
                layout.DataModelRelationshipStubLength + layout.LinkPadding))
            {
                yield return lane + laneOffset;
            }

            var top = obstacles.Select(obstacle => obstacle.Y).DefaultIfEmpty(minY).Min() - layout.DataModelRelationshipSideOffset - laneOffset;
            var bottom = obstacles.Select(obstacle => obstacle.Bottom).DefaultIfEmpty(maxY).Max() + layout.DataModelRelationshipSideOffset + laneOffset;
            yield return Math.Abs(sourceStub.Y - top) + Math.Abs(targetStub.Y - top) < Math.Abs(sourceStub.Y - bottom) + Math.Abs(targetStub.Y - bottom)
                ? top
                : bottom;
            yield return top;
            yield return bottom;
        }

        private static IEnumerable<int> GapLanes(IReadOnlyList<Span> spans, int min, int max, int clearance)
        {
            var merged = new List<Span>();
            foreach (var span in spans)
            {
                var expanded = new Span(span.Start - clearance, span.End + clearance);
                if (merged.Count == 0 || expanded.Start > merged[merged.Count - 1].End)
                {
                    merged.Add(expanded);
                    continue;
                }

                var previous = merged[merged.Count - 1];
                merged[merged.Count - 1] = new Span(previous.Start, Math.Max(previous.End, expanded.End));
            }

            for (var index = 0; index < merged.Count - 1; index++)
            {
                var gapStart = merged[index].End;
                var gapEnd = merged[index + 1].Start;
                if (gapEnd <= gapStart)
                {
                    continue;
                }

                var lane = gapStart + (gapEnd - gapStart) / 2;
                if (lane >= min - clearance && lane <= max + clearance)
                {
                    yield return lane;
                }
            }
        }

        private static long DataModelRouteScore(
            Point sourcePoint,
            IReadOnlyList<Point> route,
            Point targetPoint,
            IReadOnlyList<Rect> obstacles,
            IReadOnlyList<Segment> routedSegments,
            DataModelTerminals terminals)
        {
            var points = new List<Point> { sourcePoint };
            points.AddRange(route);
            points.Add(targetPoint);
            var segments = Segments(points).ToArray();
            var nodeHits = segments.Sum(segment => obstacles.Count(segment.Intersects));
            var overlaps = segments.Sum(segment => routedSegments.Sum(segment.OverlapLength));
            var crossings = segments.Sum(segment => routedSegments.Count(segment.Crosses));
            var reversals = CountDataModelRouteReversals(points);
            var sidePenalty = DataModelTerminalSidePenalty(terminals);
            return nodeHits * 1_000_000_000L +
                overlaps * 500L +
                crossings * 50_000L +
                reversals * 250_000L +
                sidePenalty * 10_000L +
                Math.Max(0, route.Count - 2) * 8_000L +
                RouteLength(sourcePoint, route, targetPoint);
        }

        private static int CountDataModelRouteReversals(IReadOnlyList<Point> points)
        {
            var reversals = 0;
            for (var index = 2; index < points.Count; index++)
            {
                var first = Direction(points[index - 2], points[index - 1]);
                var second = Direction(points[index - 1], points[index]);
                if (first.Axis != 0 && first.Axis == second.Axis && first.Sign == -second.Sign)
                {
                    reversals++;
                }
            }

            return reversals;
        }

        private static int DataModelTerminalSidePenalty(DataModelTerminals terminals)
        {
            return OppositeSide(terminals.SourceSide) == terminals.TargetSide ? 0 : 1;
        }

        private static DataModelSide OppositeSide(DataModelSide side)
        {
            return side switch
            {
                DataModelSide.Left => DataModelSide.Right,
                DataModelSide.Right => DataModelSide.Left,
                DataModelSide.Top => DataModelSide.Bottom,
                _ => DataModelSide.Top
            };
        }

        private static DirectionInfo Direction(Point start, Point end)
        {
            if (start.X != end.X)
            {
                return new DirectionInfo(1, Math.Sign(end.X - start.X));
            }

            if (start.Y != end.Y)
            {
                return new DirectionInfo(2, Math.Sign(end.Y - start.Y));
            }

            return new DirectionInfo(0, 0);
        }

        private static int RouteLength(Point sourcePoint, IReadOnlyList<Point> route, Point targetPoint)
        {
            var points = new List<Point> { sourcePoint };
            points.AddRange(route);
            points.Add(targetPoint);
            return Segments(points).Sum(segment => segment.Length);
        }

        private static IEnumerable<Segment> Segments(IReadOnlyList<Point> points)
        {
            for (var index = 0; index < points.Count - 1; index++)
            {
                yield return new Segment(points[index], points[index + 1]);
            }
        }

        private static double Ratio(int x, Rect rect)
        {
            return Math.Max(0, Math.Min(1, (x - rect.X) / (double)rect.Width));
        }

        private static double RatioY(int y, Rect rect)
        {
            return Math.Max(0, Math.Min(1, (y - rect.Y) / (double)rect.Height));
        }

        private static IReadOnlyList<Point> SimplifyRoute(IEnumerable<Point> points)
        {
            var result = new List<Point>();
            foreach (var point in points)
            {
                if (result.Count > 0 && result[result.Count - 1] == point)
                {
                    continue;
                }

                if (result.Count >= 2)
                {
                    var previous = result[result.Count - 1];
                    var beforePrevious = result[result.Count - 2];
                    if ((beforePrevious.X == previous.X && previous.X == point.X) ||
                        (beforePrevious.Y == previous.Y && previous.Y == point.Y))
                    {
                        result[result.Count - 1] = point;
                        continue;
                    }
                }

                result.Add(point);
            }

            return result;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private readonly record struct DataModelRelationshipRoute(DataModelTerminals Terminals, IReadOnlyList<Point> Route);

        private readonly record struct DataModelTerminals(Point Source, Point Target, DataModelSide SourceSide, DataModelSide TargetSide);

        private readonly record struct DataModelSidePair(DataModelSide Source, DataModelSide Target);

        private readonly record struct DirectionInfo(int Axis, int Sign);

        private readonly record struct Span(int Start, int End);

        private enum DataModelSide
        {
            Left,
            Right,
            Top,
            Bottom
        }
}


