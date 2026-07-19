using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record PreAssignmentConstraintDemandReport(
    IReadOnlyList<PositionalConstraintDemand> Demands,
    int DestinationColumnConflicts,
    int VerticalColumnObstacles,
    int ReturnStubObstacles);

internal static class PreAssignmentConstraintDemandProducer
{
    public static PreAssignmentConstraintDemandReport Detect(
        PlacedGraph placement,
        GeneralDownwardObservationReport downward,
        IReadOnlyList<AdjacentDownwardLinkContext> contexts,
        int separation,
        int padding)
    {
        var demands = new List<PositionalConstraintDemand>();
        var columns = downward.Routes.Where(item => item.Observation.Eligible)
            .SelectMany(item => item.VerticalColumnDemands).OrderBy(item => item.LinkId, StringComparer.Ordinal).ToArray();
        var conflicts = 0;
        for (var leftIndex = 0; leftIndex < columns.Length; leftIndex++)
        for (var rightIndex = leftIndex + 1; rightIndex < columns.Length; rightIndex++)
        {
            var first = columns[leftIndex];
            var second = columns[rightIndex];
            if (first.VerticalInterval.PositiveLengthOverlap(second.VerticalInterval) == 0 ||
                Math.Abs(first.PreferredX - second.PreferredX) >= separation) continue;
            conflicts++;
            AddSeparation(demands, placement, first.DestinationSubtreeId, second.DestinationSubtreeId,
                separation, PositionalConstraintReason.DestinationColumnSeparation,
                new AxisInterval(Math.Min(first.SourceLayer, second.SourceLayer), Math.Max(first.DestinationLayer, second.DestinationLayer)),
                new[] { first.LinkId, second.LinkId }, placement.Revision, first.LinkRevision);
        }

        var obstacles = 0;
        foreach (var column in columns)
        {
            var target = placement.Nodes[column.DestinationSubtreeId];
            var vertical = new Segment(new Point(column.PreferredX, column.VerticalInterval.Minimum),
                new Point(column.PreferredX, column.VerticalInterval.Maximum));
            foreach (var blocker in placement.Nodes.Values.Where(node =>
                         node.Node.Id != column.SourceSubtreeId && node.Node.Id != column.DestinationSubtreeId &&
                         vertical.Intersects(node.Rect.Inflate(column.RequiredClearance)))
                     .OrderBy(node => node.Node.Id, StringComparer.Ordinal))
            {
                obstacles++;
                var inflated = blocker.Rect.Inflate(column.RequiredClearance);
                var currentGap = blocker.Rect.CenterX <= target.Rect.CenterX
                    ? target.Rect.X - blocker.Rect.Right
                    : blocker.Rect.X - target.Rect.Right;
                var requiredShift = blocker.Rect.CenterX <= target.Rect.CenterX
                    ? Math.Max(0, inflated.Right - column.PreferredX + 1)
                    : Math.Max(0, column.PreferredX - inflated.X + 1);
                AddSeparation(demands, placement,
                    blocker.Rect.CenterX <= target.Rect.CenterX ? blocker.Node.Id : target.Node.Id,
                    blocker.Rect.CenterX <= target.Rect.CenterX ? target.Node.Id : blocker.Node.Id,
                    Math.Max(separation, currentGap + requiredShift), PositionalConstraintReason.VerticalColumnClearance,
                    new AxisInterval(column.SourceLayer, column.DestinationLayer), new[] { column.LinkId },
                    placement.Revision, column.LinkRevision);
            }
        }

        var returnObstacles = 0;
        foreach (var context in contexts.Where(item => item.Target.Depth <= item.Source.Depth &&
                     !item.ExposureTreeSpecific && item.Source.Node.ProjectId is not null))
        {
            var projectNodes = placement.Nodes.Values.Where(item =>
                string.Equals(item.Node.ProjectId, context.Source.Node.ProjectId, StringComparison.Ordinal)).ToArray();
            var leftX = projectNodes.Min(item => item.Rect.X) - padding;
            var rightX = projectNodes.Max(item => item.Rect.Right) + padding;
            var departureY = context.Source.Rect.Bottom + padding;
            var arrivalY = context.Target.Rect.Y - padding;
            var leftBlockers = StubBlockers(context, leftX, departureY, arrivalY, placement.Nodes).ToArray();
            var rightBlockers = StubBlockers(context, rightX, departureY, arrivalY, placement.Nodes).ToArray();
            if (leftBlockers.Length == 0 || rightBlockers.Length == 0) continue;
            var selectedBlockers = leftBlockers.Length < rightBlockers.Length
                ? leftBlockers
                : rightBlockers.Length < leftBlockers.Length
                    ? rightBlockers
                    : Math.Abs(context.Route.SourcePoint.X - leftX) <= Math.Abs(rightX - context.Route.SourcePoint.X)
                        ? leftBlockers
                        : rightBlockers;
            foreach (var blocker in selectedBlockers.Distinct().OrderBy(item => item, StringComparer.Ordinal))
            {
                returnObstacles++;
                AddSeparation(demands, placement, blocker, context.Source.Node.Id, separation + padding,
                    PositionalConstraintReason.ReturnStubClearance,
                    new AxisInterval(context.Target.Depth, context.Source.Depth), new[] { context.Route.Link.Id },
                    placement.Revision, context.RouteRevision);
            }
        }
        return new PreAssignmentConstraintDemandReport(
            demands.GroupBy(item => item.Id, StringComparer.Ordinal).Select(item => item.First())
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), conflicts, obstacles, returnObstacles);
    }

    private static IEnumerable<string> StubBlockers(
        AdjacentDownwardLinkContext context, int columnX, int departureY, int arrivalY,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var segments = new[]
        {
            new Segment(new Point(context.Route.SourcePoint.X, departureY), new Point(columnX, departureY)),
            new Segment(new Point(columnX, arrivalY), new Point(context.Route.TargetPoint.X, arrivalY))
        };
        return nodes.Values.Where(node => node.Node.Id != context.Source.Node.Id && node.Node.Id != context.Target.Node.Id &&
                segments.Any(segment => segment.Intersects(node.Rect)))
            .Select(item => item.Node.Id);
    }

    private static void AddSeparation(
        ICollection<PositionalConstraintDemand> demands,
        PlacedGraph placement,
        string firstId,
        string secondId,
        int separation,
        PositionalConstraintReason reason,
        AxisInterval layers,
        IReadOnlyList<string> links,
        LayoutRevision placementRevision,
        RouteRevision topologyRevision)
    {
        if (firstId == secondId) return;
        var leftId = placement.Nodes[firstId].Rect.CenterX <= placement.Nodes[secondId].Rect.CenterX ? firstId : secondId;
        var rightId = leftId == firstId ? secondId : firstId;
        var scopes = PreAssignmentMovementPlanner.CandidateScopes(leftId, placement, HorizontalMovementDirection.Left)
            .Concat(PreAssignmentMovementPlanner.CandidateScopes(rightId, placement, HorizontalMovementDirection.Right))
            .Distinct().ToArray();
        demands.Add(new PositionalConstraintDemand(
            $"{reason}:{string.Join("+", links.OrderBy(item => item, StringComparer.Ordinal))}:{leftId}:{rightId}",
            reason, HorizontalMovementDirection.Right, separation, layers, leftId, rightId, scopes,
            links.OrderBy(item => item, StringComparer.Ordinal).ToArray(), placementRevision, topologyRevision));
    }
}
