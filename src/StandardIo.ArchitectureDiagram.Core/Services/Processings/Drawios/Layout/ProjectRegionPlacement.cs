using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectRegionPlacement
{
    public static PlacedGraph Place(RenderGraph graph, DiagramSettings settings, LayoutRevision revision)
    {
        if (graph.Projects.Count <= 1)
            return PlacementPipeline.Place(graph, settings, revision,
                disconnectedPlacement: PlacementPipeline.DisconnectedPlacementPolicy.DedicatedRegionBelow);

        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var local = graph.Projects.OrderBy(project => project.Id, StringComparer.Ordinal)
            .ToDictionary(project => project.Id, project => PlaceProject(project), StringComparer.Ordinal);
        var dependencyEdges = graph.Links.Select(link => new
            {
                Source = nodeById[link.SourceId].ProjectId,
                Target = nodeById[link.TargetId].ProjectId
            })
            .Where(edge => edge.Source is not null && edge.Target is not null && edge.Source != edge.Target)
            .Select(edge => (Source: edge.Source!, Target: edge.Target!)).Distinct().ToArray();
        var components = StronglyConnectedComponents(graph.Projects.Select(project => project.Id), dependencyEdges);
        var componentByProject = components.SelectMany((component, index) =>
                component.Select(projectId => new { projectId, index }))
            .ToDictionary(item => item.projectId, item => item.index, StringComparer.Ordinal);
        var componentEdges = dependencyEdges.Select(edge =>
                (Source: componentByProject[edge.Source], Target: componentByProject[edge.Target]))
            .Where(edge => edge.Source != edge.Target).Distinct().ToArray();
        var componentDepth = ComponentDepths(components.Count, componentEdges);
        var gapX = Math.Max(settings.Layout.HorizontalSpacing * 2,
            settings.Layout.ContainerPadding * 4 + settings.Layout.ParallelLaneSpacing * 4);
        var gapY = Math.Max(settings.Layout.VerticalSpacing * 2,
            settings.Layout.ProjectHeaderHeight + settings.Layout.ContainerPadding * 4 +
            settings.Layout.ParallelLaneSpacing * 4);
        var componentSizes = components.Select((component, index) =>
            MeasureComponent(component, local, gapX, gapY, index)).ToArray();
        var origins = new Dictionary<string, Point>(StringComparer.Ordinal);
        var levelTop = settings.Layout.ContainerPadding;
        foreach (var level in componentDepth.Values.Distinct().OrderBy(value => value))
        {
            var cursorX = settings.Layout.ContainerPadding;
            var levelComponents = componentSizes.Where(size => componentDepth[size.Index] == level)
                .OrderBy(size => size.StableId, StringComparer.Ordinal).ToArray();
            var maxHeight = levelComponents.Select(size => size.Height).DefaultIfEmpty(0).Max();
            foreach (var component in levelComponents)
            {
                foreach (var item in component.ProjectOffsets)
                    origins[item.Key] = new Point(cursorX + item.Value.X, levelTop + item.Value.Y);
                cursorX += component.Width + gapX;
            }
            levelTop += maxHeight + gapY;
        }

        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);
        var projects = new Dictionary<string, ProjectLayout>(StringComparer.Ordinal);
        foreach (var project in graph.Projects.OrderBy(project => project.Id, StringComparer.Ordinal))
        {
            var placed = local[project.Id];
            var localBounds = placed.Projects[project.Id].Rect;
            var origin = origins[project.Id];
            var dx = origin.X - localBounds.X;
            var dy = origin.Y - localBounds.Y;
            projects[project.Id] = new ProjectLayout(project, localBounds.Translate(dx, dy));
            foreach (var node in placed.Nodes.Values)
                nodes[node.Node.Id] = node with { Rect = node.Rect.Translate(dx, dy) };
        }

        PlaceRootOwnedNodes(graph, settings, revision, nodes, projects, gapX);
        return new PlacedGraph(graph, nodes, projects, revision);

        PlacedGraph PlaceProject(RenderProject project)
        {
            var projectNodes = graph.Nodes.Where(node => node.ProjectId == project.Id)
                .OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal).ToArray();
            var ids = new HashSet<string>(projectNodes.Select(node => node.Id), StringComparer.Ordinal);
            var projectLinks = graph.Links.Where(link => ids.Contains(link.SourceId) && ids.Contains(link.TargetId))
                .OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal).ToArray();
            var parents = graph.PlacementParentByNode.Where(pair => ids.Contains(pair.Key) && ids.Contains(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var subgraph = RenderGraph.Create([project], projectNodes, projectLinks, parents);
            var placed = PlacementPipeline.Place(subgraph, settings, revision,
                disconnectedPlacement: PlacementPipeline.DisconnectedPlacementPolicy.DedicatedRegionBelow);
            if (placed.Projects.ContainsKey(project.Id)) return placed;
            var emptyBounds = new Rect(
                settings.Layout.ContainerPadding,
                settings.Layout.ContainerPadding,
                settings.Layout.NodeWidth + settings.Layout.ContainerPadding * 2,
                settings.Layout.ProjectHeaderHeight + settings.Layout.ContainerPadding * 2);
            return new PlacedGraph(subgraph, placed.Nodes,
                new Dictionary<string, ProjectLayout>(StringComparer.Ordinal)
                {
                    [project.Id] = new ProjectLayout(project, emptyBounds)
                }, revision);
        }
    }

    private static void PlaceRootOwnedNodes(
        RenderGraph graph,
        DiagramSettings settings,
        LayoutRevision revision,
        IDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects,
        int gap)
    {
        var rootNodes = graph.Nodes.Where(node => node.ProjectId is null)
            .OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal).ToArray();
        if (rootNodes.Length == 0) return;
        var ids = new HashSet<string>(rootNodes.Select(node => node.Id), StringComparer.Ordinal);
        var links = graph.Links.Where(link => ids.Contains(link.SourceId) && ids.Contains(link.TargetId)).ToArray();
        var placed = PlacementPipeline.Place(RenderGraph.Create(Array.Empty<RenderProject>(), rootNodes, links),
            settings, revision, disconnectedPlacement: PlacementPipeline.DisconnectedPlacementPolicy.DedicatedRegionBelow);
        var left = placed.Nodes.Values.Select(node => node.Rect.X).DefaultIfEmpty(0).Min();
        var top = placed.Nodes.Values.Select(node => node.Rect.Y).DefaultIfEmpty(0).Min();
        var targetX = projects.Values.Select(project => project.Rect.Right).DefaultIfEmpty(0).Max() + gap;
        var targetY = projects.Values.Select(project => project.Rect.Y).DefaultIfEmpty(settings.Layout.ContainerPadding).Min();
        foreach (var node in placed.Nodes.Values)
            nodes[node.Node.Id] = node with { Rect = node.Rect.Translate(targetX - left, targetY - top) };
    }

    private static ComponentSize MeasureComponent(
        IReadOnlyList<string> projectIds,
        IReadOnlyDictionary<string, PlacedGraph> local,
        int gapX,
        int gapY,
        int index)
    {
        var ordered = projectIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ordered.Length)));
        var widths = ordered.Select(id => local[id].Projects[id].Rect.Width).ToArray();
        var heights = ordered.Select(id => local[id].Projects[id].Rect.Height).ToArray();
        var columnWidths = Enumerable.Range(0, columns).Select(column =>
            ordered.Select((_, item) => item).Where(item => item % columns == column)
                .Select(item => widths[item]).DefaultIfEmpty(0).Max()).ToArray();
        var rows = (int)Math.Ceiling(ordered.Length / (double)columns);
        var rowHeights = Enumerable.Range(0, rows).Select(row =>
            ordered.Select((_, item) => item).Where(item => item / columns == row)
                .Select(item => heights[item]).DefaultIfEmpty(0).Max()).ToArray();
        var offsets = new Dictionary<string, Point>(StringComparer.Ordinal);
        for (var item = 0; item < ordered.Length; item++)
        {
            var column = item % columns;
            var row = item / columns;
            offsets[ordered[item]] = new Point(
                columnWidths.Take(column).Sum() + column * gapX,
                rowHeights.Take(row).Sum() + row * gapY);
        }
        return new ComponentSize(index, ordered[0],
            columnWidths.Sum() + Math.Max(0, columns - 1) * gapX,
            rowHeights.Sum() + Math.Max(0, rows - 1) * gapY, offsets);
    }

    private static Dictionary<int, int> ComponentDepths(
        int count,
        IReadOnlyList<(int Source, int Target)> edges)
    {
        var depth = Enumerable.Range(0, count).ToDictionary(index => index, _ => 0);
        for (var pass = 0; pass < count; pass++)
        {
            var changed = false;
            foreach (var edge in edges.OrderBy(edge => edge.Source).ThenBy(edge => edge.Target))
            {
                var next = depth[edge.Source] + 1;
                if (next <= depth[edge.Target]) continue;
                depth[edge.Target] = next;
                changed = true;
            }
            if (!changed) break;
        }
        return depth;
    }

    private static IReadOnlyList<string[]> StronglyConnectedComponents(
        IEnumerable<string> projectIds,
        IReadOnlyList<(string Source, string Target)> edges)
    {
        var outgoing = edges.GroupBy(edge => edge.Source, StringComparer.Ordinal).ToDictionary(
            group => group.Key, group => group.Select(edge => edge.Target).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var index = 0;
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string[]>();
        foreach (var id in projectIds.OrderBy(id => id, StringComparer.Ordinal))
            if (!indexes.ContainsKey(id)) Visit(id);
        return result.OrderBy(component => component[0], StringComparer.Ordinal).ToArray();

        void Visit(string id)
        {
            indexes[id] = low[id] = index++;
            stack.Push(id);
            onStack.Add(id);
            if (outgoing.TryGetValue(id, out var targets))
                foreach (var target in targets)
                    if (!indexes.ContainsKey(target)) { Visit(target); low[id] = Math.Min(low[id], low[target]); }
                    else if (onStack.Contains(target)) low[id] = Math.Min(low[id], indexes[target]);
            if (low[id] != indexes[id]) return;
            var component = new List<string>();
            string current;
            do { current = stack.Pop(); onStack.Remove(current); component.Add(current); }
            while (current != id);
            result.Add(component.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }
    }

    private sealed record ComponentSize(
        int Index,
        string StableId,
        int Width,
        int Height,
        IReadOnlyDictionary<string, Point> ProjectOffsets);
}
