using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectLayerBandPlacement
{
    public static PlacedGraph Align(PlacedGraph placement, DiagramSettings settings)
    {
        var owned = placement.Nodes.Values.Where(node => node.Node.ProjectId is not null).ToArray();
        if (owned.Length == 0) return placement;
        var layerY = new Dictionary<int, int>();
        var y = owned.Min(node => node.Rect.Y);
        foreach (var layer in owned.GroupBy(node => node.Depth).OrderBy(group => group.Key))
        {
            layerY[layer.Key] = y;
            y += layer.Max(node => node.Rect.Height) + settings.Layout.VerticalSpacing;
        }
        var nodes = placement.Nodes.ToDictionary(item => item.Key, item =>
        {
            if (item.Value.Node.ProjectId is null) return item.Value;
            return item.Value with { Rect = item.Value.Rect with { Y = layerY[item.Value.Depth] } };
        }, StringComparer.Ordinal);
        return placement.Revise(nodes, PlacementPipeline.PositionProjects(placement.Graph, settings, nodes));
    }

    public static PlacedGraph Expand(
        PlacedGraph immutableBase,
        DiagramSettings settings,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> expansions)
    {
        var nodes = immutableBase.Nodes.ToDictionary(item => item.Key, item =>
        {
            if (item.Value.Node.ProjectId is null) return item.Value;
            var delta = expansions.Where(expansion =>
                    string.Equals(expansion.Key.ProjectId, item.Value.Node.ProjectId, StringComparison.Ordinal) &&
                    item.Value.Depth >= expansion.Key.LowerDepth)
                .Sum(expansion => expansion.Value);
            return item.Value with { Rect = item.Value.Rect with { Y = item.Value.Rect.Y + delta } };
        }, StringComparer.Ordinal);
        return immutableBase.Revise(nodes, PlacementPipeline.PositionProjects(immutableBase.Graph, settings, nodes));
    }
}
