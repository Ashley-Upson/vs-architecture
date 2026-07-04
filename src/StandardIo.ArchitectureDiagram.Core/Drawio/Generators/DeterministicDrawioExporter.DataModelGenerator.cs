using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed partial class DeterministicDrawioExporter
{
    private sealed partial class DiagramFileBuilder
    {
        private XElement DataModelRoot(IReadOnlyList<RenderNode> models)
        {
            var root = new XElement("root",
                new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
            var modelsById = models.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var modelIds = new HashSet<string>(models.Select(model => model.Id), StringComparer.Ordinal);
            var orderedModels = models.OrderBy(model => model.FullName, StringComparer.Ordinal).ToArray();
            var rects = PositionDataModelTables(orderedModels, _settings.Layout);
            var incomingPorts = DataModelIncomingPorts(DataModelRelationships(modelsById), rects);

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
                    var propertyY = DataModelPropertyY(source, model, property.Name);
                    var incomingPort = incomingPorts[DataModelRelationshipKey(model.Id, property.TypeId!, property.Name)];
                    var terminals = GetDataModelTerminals(source, target, propertyY, incomingPort.Index, incomingPort.Total);
                    var route = DataModelRoute(
                        terminals,
                        rects
                            .Where(item => item.Key != model.Id && item.Key != property.TypeId)
                            .Select(item => item.Value.Inflate(_settings.Layout.LinkPadding))
                            .ToArray(),
                        routedSegments,
                        edgeOrder,
                        _settings.Layout);
                    var link = new LinkLayout(
                        new RenderLink($"data_model_edge_{edgeOrder++}", DataModelNodeId(model.Id), DataModelNodeId(property.TypeId!), "property", edgeOrder),
                        terminals.Source,
                        terminals.Target,
                        route,
                        Ratio(terminals.Source.X, source),
                        Ratio(terminals.Target.X, target),
                        RatioY(terminals.Source.Y, source),
                        RatioY(terminals.Target.Y, target));
                    root.Add(Edge(link));
                    routedSegments.AddRange(Segments(new[] { terminals.Source }.Concat(route).Concat(new[] { terminals.Target }).ToArray()));
                }
            }

            return root;
        }

        private static Dictionary<string, DataModelPort> DataModelIncomingPorts(
            IReadOnlyList<DataModelRelationship> relationships,
            IReadOnlyDictionary<string, Rect> rects)
        {
            var result = new Dictionary<string, DataModelPort>(StringComparer.Ordinal);
            foreach (var group in relationships.GroupBy(relationship => relationship.TargetId, StringComparer.Ordinal))
            {
                var ordered = group
                    .OrderBy(relationship => rects.TryGetValue(relationship.SourceId, out var source) ? source.CenterY : 0)
                    .ThenBy(relationship => relationship.SourceId, StringComparer.Ordinal)
                    .ThenBy(relationship => relationship.PropertyName, StringComparer.Ordinal)
                    .ToArray();

                for (var index = 0; index < ordered.Length; index++)
                {
                    result[DataModelRelationshipKey(ordered[index].SourceId, ordered[index].TargetId, ordered[index].PropertyName)] =
                        new DataModelPort(index, ordered.Length);
                }
            }

            return result;
        }

        private static string DataModelRelationshipKey(string sourceId, string targetId, string propertyName)
        {
            return $"{sourceId}|{targetId}|{propertyName}";
        }

        private static Dictionary<string, Rect> PositionDataModelTables(
            IReadOnlyList<RenderNode> models,
            LayoutSettings layout)
        {
            var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var modelsById = models.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var relationships = DataModelRelationships(modelsById);
            var adjacency = models.ToDictionary(
                model => model.Id,
                _ => new HashSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);

            foreach (var relationship in relationships)
            {
                adjacency[relationship.SourceId].Add(relationship.TargetId);
                adjacency[relationship.TargetId].Add(relationship.SourceId);
            }

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

            var y = layout.DataModelCanvasMargin;
            var rowGap = Math.Max(layout.DataModelRowSpacing, layout.DataModelPropertyRowHeight * 2);

            foreach (var component in connectedComponents)
            {
                var componentRects = PositionDataModelComponent(component, relationships, layout, layout.DataModelCanvasMargin, y);
                foreach (var rect in componentRects)
                {
                    rects[rect.Key] = rect.Value;
                }

                y = componentRects.Values.Max(rect => rect.Bottom) + rowGap * 2;
            }

            if (isolatedModels.Length > 0)
            {
                var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(isolatedModels.Length)));
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
                            layout.DataModelCanvasMargin + column * layout.DataModelColumnWidth,
                            y,
                            layout.DataModelTableWidth,
                            DataModelTableHeight(item.model, layout));
                    }

                    y += rowHeight + rowGap;
                }
            }

            return rects;
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
            LayoutSettings layout,
            int x,
            int y)
        {
            var componentIds = new HashSet<string>(component.Select(model => model.Id), StringComparer.Ordinal);
            var componentRelationships = relationships
                .Where(relationship => componentIds.Contains(relationship.SourceId) && componentIds.Contains(relationship.TargetId))
                .ToArray();
            var modelsById = component.ToDictionary(model => model.Id, StringComparer.Ordinal);
            var outgoing = componentRelationships
                .GroupBy(relationship => relationship.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(relationship => relationship.TargetId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var incoming = componentRelationships
                .GroupBy(relationship => relationship.TargetId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(relationship => relationship.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var root = component
                .OrderByDescending(model => (outgoing.TryGetValue(model.Id, out var outDegree) ? outDegree.Length : 0) + (incoming.TryGetValue(model.Id, out var inDegree) ? inDegree.Length : 0))
                .ThenByDescending(model => outgoing.TryGetValue(model.Id, out var outIds) ? outIds.Length : 0)
                .ThenBy(model => model.FullName, StringComparer.Ordinal)
                .First();

            var levels = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [root.Id] = 0
            };
            var queue = new Queue<string>();
            queue.Enqueue(root.Id);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                var level = levels[id];

                var outgoingIds = outgoing.TryGetValue(id, out var targets)
                    ? targets
                    : Array.Empty<string>();
                foreach (var targetId in outgoingIds.OrderBy(id => modelsById[id].FullName, StringComparer.Ordinal))
                {
                    if (!levels.ContainsKey(targetId))
                    {
                        levels[targetId] = level + 1;
                        queue.Enqueue(targetId);
                    }
                }

                var incomingIds = incoming.TryGetValue(id, out var sources)
                    ? sources
                    : Array.Empty<string>();
                foreach (var sourceId in incomingIds.OrderBy(id => modelsById[id].FullName, StringComparer.Ordinal))
                {
                    if (!levels.ContainsKey(sourceId))
                    {
                        levels[sourceId] = level - 1;
                        queue.Enqueue(sourceId);
                    }
                }
            }

            foreach (var model in component.Where(model => !levels.ContainsKey(model.Id)).OrderBy(model => model.FullName, StringComparer.Ordinal))
            {
                levels[model.Id] = levels.Values.DefaultIfEmpty(0).Max() + 1;
            }

            var minLevel = levels.Values.Min();
            var normalized = levels.ToDictionary(item => item.Key, item => item.Value - minLevel, StringComparer.Ordinal);
            var levelGroups = component
                .GroupBy(model => normalized[model.Id])
                .OrderBy(group => group.Key)
                .ToArray();
            var columnWidth = Math.Max(layout.DataModelColumnWidth, layout.DataModelTableWidth + layout.DataModelRelationshipStubLength * 2);
            var rowGap = Math.Max(layout.DataModelRowSpacing, layout.DataModelPropertyRowHeight * 2);
            var columnHeights = levelGroups.ToDictionary(
                group => group.Key,
                group => group.Sum(model => DataModelTableHeight(model, layout)) + Math.Max(0, group.Count() - 1) * rowGap);
            var componentHeight = columnHeights.Values.DefaultIfEmpty(0).Max();
            var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);

            foreach (var levelGroup in levelGroups)
            {
                var groupModels = levelGroup
                    .OrderByDescending(model => (outgoing.TryGetValue(model.Id, out var outDegree) ? outDegree.Length : 0) + (incoming.TryGetValue(model.Id, out var inDegree) ? inDegree.Length : 0))
                    .ThenBy(model => model.FullName, StringComparer.Ordinal)
                    .ToArray();
                var currentY = y + (componentHeight - columnHeights[levelGroup.Key]) / 2;

                foreach (var model in groupModels)
                {
                    var height = DataModelTableHeight(model, layout);
                    rects[model.Id] = new Rect(
                        x + levelGroup.Key * columnWidth,
                        currentY,
                        layout.DataModelTableWidth,
                        height);
                    currentY += height + rowGap;
                }
            }

            return rects;
        }

        private static int DataModelTableHeight(RenderNode model, LayoutSettings layout)
        {
            return layout.DataModelHeaderHeight +
                Math.Max(1, model.Properties.Count) * layout.DataModelPropertyRowHeight;
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

        private int DataModelPropertyY(Rect rect, RenderNode model, string propertyName)
        {
            var index = model.Properties
                .Select((property, order) => new { property.Name, order })
                .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                ?.order ?? 0;
            return rect.Y +
                _settings.Layout.DataModelHeaderHeight +
                index * _settings.Layout.DataModelPropertyRowHeight +
                _settings.Layout.DataModelPropertyRowHeight / 2;
        }

        private static DataModelTerminals GetDataModelTerminals(
            Rect source,
            Rect target,
            int sourcePropertyY,
            int targetPortIndex,
            int targetPortTotal)
        {
            if (target.X >= source.Right)
            {
                return new DataModelTerminals(
                    new Point(source.Right, sourcePropertyY),
                    new Point(target.X, DataModelPortY(target, targetPortIndex, targetPortTotal)),
                    DataModelSide.Right,
                    DataModelSide.Left);
            }

            if (target.Right <= source.X)
            {
                return new DataModelTerminals(
                    new Point(source.X, sourcePropertyY),
                    new Point(target.Right, DataModelPortY(target, targetPortIndex, targetPortTotal)),
                    DataModelSide.Left,
                    DataModelSide.Right);
            }

            var dy = target.Y + target.Height / 2 - (source.Y + source.Height / 2);
            return dy >= 0
                ? new DataModelTerminals(
                    new Point(Clamp(target.CenterX, source.X + 12, source.Right - 12), source.Bottom),
                    new Point(DataModelPortX(target, targetPortIndex, targetPortTotal), target.Y),
                    DataModelSide.Bottom,
                    DataModelSide.Top)
                : new DataModelTerminals(
                    new Point(Clamp(target.CenterX, source.X + 12, source.Right - 12), source.Y),
                    new Point(DataModelPortX(target, targetPortIndex, targetPortTotal), target.Bottom),
                    DataModelSide.Top,
                    DataModelSide.Bottom);
        }

        private static int DataModelPortY(Rect rect, int index, int total)
        {
            var top = rect.Y + 16;
            var span = Math.Max(1, rect.Height - 32);
            return top + span * (index + 1) / (Math.Max(1, total) + 1);
        }

        private static int DataModelPortX(Rect rect, int index, int total)
        {
            var left = rect.X + 16;
            var span = Math.Max(1, rect.Width - 32);
            return left + span * (index + 1) / (Math.Max(1, total) + 1);
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
                .OrderBy(route => DataModelRouteScore(sourcePoint, route, targetPoint, obstacles, routedSegments))
                .ThenBy(route => RouteLength(sourcePoint, route, targetPoint))
                .First();
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

        private static bool DataModelRouteCrossesNode(
            Point sourcePoint,
            IReadOnlyList<Point> route,
            Point targetPoint,
            IReadOnlyList<Rect> obstacles)
        {
            var points = new List<Point> { sourcePoint };
            points.AddRange(route);
            points.Add(targetPoint);
            return Segments(points).Any(segment => obstacles.Any(segment.Intersects));
        }

        private static IEnumerable<IReadOnlyList<Point>> DataModelRouteCandidates(
            Point sourceStub,
            Point targetStub,
            IReadOnlyList<Rect> obstacles,
            int order,
            LayoutSettings layout)
        {
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
            IReadOnlyList<Segment> routedSegments)
        {
            var points = new List<Point> { sourcePoint };
            points.AddRange(route);
            points.Add(targetPoint);
            var segments = Segments(points).ToArray();
            var nodeHits = segments.Sum(segment => obstacles.Count(segment.Intersects));
            var overlaps = segments.Sum(segment => routedSegments.Sum(segment.OverlapLength));
            var crossings = segments.Sum(segment => routedSegments.Count(segment.Crosses));
            return nodeHits * 1_000_000_000L +
                overlaps * 500L +
                crossings * 50_000L +
                Math.Max(0, route.Count - 2) * 25 +
                RouteLength(sourcePoint, route, targetPoint);
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

        private readonly record struct DataModelTerminals(Point Source, Point Target, DataModelSide SourceSide, DataModelSide TargetSide);

        private readonly record struct DataModelPort(int Index, int Total);

        private readonly record struct Span(int Start, int End);

        private enum DataModelSide
        {
            Left,
            Right,
            Top,
            Bottom
        }

    }
}
