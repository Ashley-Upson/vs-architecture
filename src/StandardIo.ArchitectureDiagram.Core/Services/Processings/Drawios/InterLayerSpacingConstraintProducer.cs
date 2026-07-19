using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record InterLayerSpacingConstraintPlan(
    IReadOnlyList<InterLayerConflictComponent> Groups,
    IReadOnlyList<MinimumSpacingConstraint> Constraints,
    IReadOnlyList<string> InvalidatedRoutes,
    GroupedSpacingTelemetry Telemetry);

internal sealed record InterLayerSpacingConstraintResult(
    PlacedGraph Placement,
    GeneratedLogicalRoutes Routes,
    InterLayerSpacingConstraintPlan Plan,
    int Iterations);

internal static class InterLayerSpacingConstraintProducer
{
    public static bool Supports(PlacedGraph placement, GeneratedLogicalRoutes routes, InterLayerReport report)
    {
        routes.EnsureCompatible(placement);
        if (report.Telemetry.UnsupportedShapeCount > 0 ||
            !report.InterLayers.Any(interLayer => interLayer.MissingExtent > 0) ||
            placement.Graph.Nodes.Any(node => node.Id.StartsWith("tree_", StringComparison.Ordinal))) return false;
        return routes.Links.Values.All(link =>
            placement.Nodes.TryGetValue(link.Link.SourceId, out var source) &&
            placement.Nodes.TryGetValue(link.Link.TargetId, out var target) &&
            target.Depth == source.Depth + 1 &&
            report.InterLayers.SelectMany(interLayer => interLayer.Memberships)
                .Count(item => item.LogicalEdgeIdentity == link.Link.Id) == 1);
    }

    public static InterLayerSpacingConstraintPlan Plan(
        PlacedGraph placement,
        GeneratedLogicalRoutes routes,
        InterLayerReport report,
        DiagramSettings settings)
    {
        if (!Supports(placement, routes, report))
            throw new InvalidOperationException("Grouped vertical interLayers support only orthogonal adjacent-layer downward routes.");
        var groups = new List<InterLayerConflictComponent>();
        var constraints = new MonotonicSpacingConstraintStore();
        long comparisons = 0;
        var proposals = 0;
        var increased = 0;
        foreach (var interLayer in report.InterLayers.OrderBy(item => item.Id.UpperLayer))
        {
            var interLayerGroups = InterLayerConflictGrouper.Group(
                interLayer, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding, out var interLayerComparisons);
            comparisons += interLayerComparisons;
            groups.AddRange(interLayerGroups);
            var required = interLayerGroups.Select(item => item.RequiredExtent).DefaultIfEmpty(interLayer.CurrentExtent).Max();
            var proposal = new MinimumSpacingConstraint(
                new SpacingConstraintKey(interLayer.LowerBoundary, 0, SpacingConstraintScope.LayerBoundary,
                    $"{interLayer.Id.UpperLayer}-{interLayer.Id.LowerLayer}"),
                required,
                string.Join("+", interLayerGroups.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal)));
            proposals++;
            if (constraints.Merge(proposal)) increased++;
        }
        var invalidated = groups.SelectMany(group => group.Demands)
            .Select(item => item.LogicalEdgeIdentity).Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return new InterLayerSpacingConstraintPlan(groups.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            constraints.Snapshot(), invalidated,
            new GroupedSpacingTelemetry(groups.Count,
                groups.Sum(group => Math.Max(0, group.Demands.Count - 1)), proposals, increased, comparisons));
    }

    public static InterLayerSpacingConstraintResult Apply(
        PlacedGraph placement,
        GeneratedLogicalRoutes routes,
        InterLayerReport report,
        DiagramSettings settings)
    {
        var plan = Plan(placement, routes, report, settings);
        var deltaByLowerLayer = new Dictionary<int, int>();
        foreach (var interLayer in report.InterLayers)
        {
            var constraint = plan.Constraints.Single(item =>
                item.Key.StableIdentity == $"{interLayer.Id.UpperLayer}-{interLayer.Id.LowerLayer}");
            deltaByLowerLayer[interLayer.Id.LowerLayer] = Math.Max(0, constraint.Minimum - interLayer.CurrentExtent);
        }
        int DeltaForDepth(int depth) => deltaByLowerLayer.Where(item => item.Key <= depth).Sum(item => item.Value);
        var movedNodes = placement.Nodes.ToDictionary(item => item.Key, item =>
        {
            var delta = DeltaForDepth(item.Value.Depth);
            return item.Value with { Rect = item.Value.Rect.Translate(0, delta) };
        }, StringComparer.Ordinal);
        var revised = placement.Revise(movedNodes,
            PlacementPipeline.PositionProjects(placement.Graph, settings, movedNodes));
        var slotByLink = plan.Groups.SelectMany(group => group.Demands.Select(demand => new
            {
                demand.LogicalEdgeIdentity,
                Slot = group.AssignedSlots[demand.Id],
                group.InterLayerId
            })).GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.InterLayerId.UpperLayer).First(), StringComparer.Ordinal);
        var regenerated = routes.Links.ToDictionary(item => item.Key, item =>
        {
            if (!slotByLink.TryGetValue(item.Key, out var assignment)) return item.Value;
            var old = item.Value;
            var sourceNode = revised.Nodes[old.Link.SourceId];
            var targetNode = revised.Nodes[old.Link.TargetId];
            var sourcePoint = new Point(old.SourcePoint.X, sourceNode.Rect.Bottom);
            var targetPoint = new Point(old.TargetPoint.X, targetNode.Rect.Y);
            var interLayer = report.InterLayers.Single(value => value.Id.UpperLayer == assignment.InterLayerId.UpperLayer);
            var slotY = interLayer.UpperBoundary + settings.Layout.LinkPadding +
                assignment.Slot * settings.Layout.ParallelLaneSpacing;
            var sourceExit = new Point(sourcePoint.X, sourcePoint.Y + settings.Layout.LinkPadding);
            var targetEntry = new Point(targetPoint.X, targetPoint.Y - settings.Layout.LinkPadding);
            var points = Simplify(new[]
            {
                sourceExit, new Point(sourceExit.X, slotY), new Point(targetEntry.X, slotY), targetEntry
            });
            return new LinkLayout(old.Link, sourcePoint, targetPoint, points,
                old.ExitX, old.EntryX, old.ExitY, old.EntryY);
        }, StringComparer.Ordinal);
        return new InterLayerSpacingConstraintResult(revised,
            new GeneratedLogicalRoutes(revised, regenerated, routes.Revision.Next()), plan,
            deltaByLowerLayer.Values.Any(value => value > 0) ? 1 : 0);
    }

    private static IReadOnlyList<Point> Simplify(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            if (result.Count > 0 && result[result.Count - 1] == point) continue;
            if (result.Count >= 2 &&
                (result[result.Count - 2].X == result[result.Count - 1].X && result[result.Count - 1].X == point.X ||
                 result[result.Count - 2].Y == result[result.Count - 1].Y && result[result.Count - 1].Y == point.Y))
                result[result.Count - 1] = point;
            else result.Add(point);
        }
        return result;
    }
}
