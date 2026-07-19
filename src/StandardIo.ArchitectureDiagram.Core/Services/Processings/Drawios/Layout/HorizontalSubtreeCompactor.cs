using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class HorizontalSubtreeCompactor
{
    public static HorizontalCompactionResult Compact(
        PlacedGraph placement,
        DiagramSettings settings)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        var nodes = placement.Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var moves = new List<HorizontalCompactionMove>();
        var beforeGaps = new List<int>();

        foreach (var project in placement.Graph.Projects.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var roots = placement.PositionalHierarchy.RootNodeIds
                .Where(id => string.Equals(placement.Nodes[id].Node.ProjectId, project.Id, StringComparison.Ordinal))
                .OrderBy(id => placement.PositionalHierarchy.EnvelopesByRootNode[id].OverallBounds.X)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray();
            for (var index = 1; index < roots.Length; index++)
            {
                var previous = ShiftedEnvelope(placement.PositionalHierarchy.EnvelopesByRootNode[roots[index - 1]], nodes, placement);
                var current = ShiftedEnvelope(placement.PositionalHierarchy.EnvelopesByRootNode[roots[index]], nodes, placement);
                var gap = ClosestLayerGap(previous, current);
                beforeGaps.Add(gap);
                var delta = settings.Layout.HorizontalSpacing - gap;
                if (delta == 0) continue;
                foreach (var id in HorizontalMovementConstraintMaterializer.Members(
                             new MovementScopeIdentity(MovementScopeKind.LayoutSubtree, roots[index]), placement))
                    nodes[id] = nodes[id] with { Rect = nodes[id].Rect.Translate(delta, 0) };
                moves.Add(new HorizontalCompactionMove(roots[index], delta, gap, settings.Layout.HorizontalSpacing));
            }
        }

        if (moves.Count == 0)
            return new HorizontalCompactionResult(placement, Array.Empty<HorizontalCompactionMove>(),
                beforeGaps.DefaultIfEmpty(settings.Layout.HorizontalSpacing).Max(),
                beforeGaps.DefaultIfEmpty(settings.Layout.HorizontalSpacing).Max());
        var revised = placement.Revise(nodes, PlacementPipeline.PositionProjects(placement.Graph, settings, nodes));
        var afterGaps = ProjectRootGaps(revised);
        return new HorizontalCompactionResult(
            revised,
            moves.AsReadOnly(),
            beforeGaps.DefaultIfEmpty(0).Max(),
            afterGaps.DefaultIfEmpty(0).Max());
    }

    private static PositionalSubtreeEnvelope ShiftedEnvelope(
        PositionalSubtreeEnvelope basis,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        PlacedGraph placement)
    {
        var delta = nodes[basis.RootNodeId].Rect.X - placement.Nodes[basis.RootNodeId].Rect.X;
        return basis with
        {
            OverallBounds = basis.OverallBounds.Translate(delta, 0),
            BoundsByLayer = basis.BoundsByLayer.ToDictionary(item => item.Key, item => item.Value.Translate(delta, 0))
        };
    }

    private static IReadOnlyList<int> ProjectRootGaps(PlacedGraph placement)
    {
        var result = new List<int>();
        foreach (var project in placement.Graph.Projects)
        {
            var roots = placement.PositionalHierarchy.RootNodeIds
                .Where(id => string.Equals(placement.Nodes[id].Node.ProjectId, project.Id, StringComparison.Ordinal))
                .Select(id => placement.PositionalHierarchy.EnvelopesByRootNode[id])
                .OrderBy(envelope => envelope.OverallBounds.X).ThenBy(envelope => envelope.RootNodeId, StringComparer.Ordinal).ToArray();
            for (var index = 1; index < roots.Length; index++) result.Add(ClosestLayerGap(roots[index - 1], roots[index]));
        }
        return result;
    }

    private static int ClosestLayerGap(PositionalSubtreeEnvelope left, PositionalSubtreeEnvelope right)
    {
        var sharedLayers = left.BoundsByLayer.Keys.Intersect(right.BoundsByLayer.Keys).ToArray();
        return sharedLayers.Length == 0
            ? right.OverallBounds.X - left.OverallBounds.Right
            : sharedLayers.Min(layer => right.BoundsByLayer[layer].X - left.BoundsByLayer[layer].Right);
    }
}
