using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record PreAssignmentConstraintDemandReport(
    IReadOnlyList<PositionalConstraintDemand> Demands,
    IReadOnlyList<ColumnToEnvelopeDifferenceConstraint> ColumnToEnvelopeConstraints,
    IReadOnlyList<ColumnToColumnDifferenceConstraint> ColumnToColumnConstraints,
    IReadOnlyList<ReturnColumnEnvelopeConstraint> ReturnColumnConstraints,
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
        var columnEnvelopeConstraints = new List<ColumnToEnvelopeDifferenceConstraint>();
        var columnColumnConstraints = new List<ColumnToColumnDifferenceConstraint>();
        var returnColumnConstraints = new List<ReturnColumnEnvelopeConstraint>();
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
            columnColumnConstraints.Add(new ColumnToColumnDifferenceConstraint(
                $"column-column:{first.Id}:{second.Id}", first.LinkId, second.LinkId,
                first.PreferredX, second.PreferredX,
                first.DestinationSubtreeId, second.DestinationSubtreeId,
                new AxisInterval(Math.Max(first.SourceLayer, second.SourceLayer),
                    Math.Min(first.DestinationLayer, second.DestinationLayer)), separation,
                PreAssignmentMovementPlanner.CandidateScopes(first.DestinationSubtreeId, placement, HorizontalMovementDirection.Left)
                    .Concat(PreAssignmentMovementPlanner.CandidateScopes(second.DestinationSubtreeId, placement, HorizontalMovementDirection.Right))
                    .Distinct().ToArray(), placement.Revision));
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
                columnEnvelopeConstraints.Add(new ColumnToEnvelopeDifferenceConstraint(
                    $"column-envelope:{column.Id}:{blocker.Node.Id}", column.LinkId, column.Id,
                    column.PreferredX,
                    column.DestinationSubtreeId, target.Rect, blocker.Node.Id, blocker.Rect,
                    new AxisInterval(column.SourceLayer, column.DestinationLayer), column.RequiredClearance,
                    new HorizontalDifferenceAlternative(HorizontalMovementDirection.Left,
                        inflated.X - 1,
                        PreAssignmentMovementPlanner.CandidateScopes(column.DestinationSubtreeId, placement, HorizontalMovementDirection.Left)
                            .Concat(PreAssignmentMovementPlanner.CandidateScopes(blocker.Node.Id, placement, HorizontalMovementDirection.Right)).Distinct().ToArray()),
                    new HorizontalDifferenceAlternative(HorizontalMovementDirection.Right,
                        inflated.Right + 1,
                        PreAssignmentMovementPlanner.CandidateScopes(column.DestinationSubtreeId, placement, HorizontalMovementDirection.Right)
                            .Concat(PreAssignmentMovementPlanner.CandidateScopes(blocker.Node.Id, placement, HorizontalMovementDirection.Left)).Distinct().ToArray()),
                    placement.Revision, placement.Revision));
            }
        }

        var returnObstacles = 0;
        foreach (var context in contexts.Where(item => item.Target.Depth <= item.Source.Depth &&
                     !item.ExposureTreeSpecific && item.Source.Node.ProjectId is not null))
        {
            var ownership = ReturnLinkCommonAllocator.Ownership(context, placement, padding);
            var leftX = ownership.OwnershipBounds.X - padding;
            var rightX = ownership.OwnershipBounds.Right + padding;
            var departureY = context.Source.Rect.Bottom + padding;
            var arrivalY = context.Target.Rect.Y - padding;
            var leftBlockers = StubBlockers(context, ownership, leftX, departureY, arrivalY, placement.Nodes).ToArray();
            var rightBlockers = StubBlockers(context, ownership, rightX, departureY, arrivalY, placement.Nodes).ToArray();
            if (leftBlockers.Length == 0 || rightBlockers.Length == 0) continue;
            returnColumnConstraints.Add(new ReturnColumnEnvelopeConstraint(
                $"return-envelope:{context.Route.Link.Id}:{ownership.Id}", context.Route.Link.Id, ownership,
                leftX, rightX, leftBlockers.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                rightBlockers.OrderBy(item => item, StringComparer.Ordinal).ToArray(), placement.Revision));
            var selectedBlockers = leftBlockers.Length < rightBlockers.Length
                ? leftBlockers
                : rightBlockers.Length < leftBlockers.Length
                    ? rightBlockers
                    : Math.Abs(context.Route.SourcePoint.X - leftX) <= Math.Abs(rightX - context.Route.SourcePoint.X)
                        ? leftBlockers
                        : rightBlockers;
            returnObstacles += selectedBlockers.Distinct(StringComparer.Ordinal).Count();
        }
        return new PreAssignmentConstraintDemandReport(
            demands.GroupBy(item => item.Id, StringComparer.Ordinal).Select(item => item.First())
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            columnEnvelopeConstraints.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            columnColumnConstraints.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            returnColumnConstraints.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            conflicts, obstacles, returnObstacles);
    }

    private static IEnumerable<string> StubBlockers(
        AdjacentDownwardLinkContext context, ReturnColumnOwnership ownership,
        int columnX, int departureY, int arrivalY,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var segments = new[]
        {
            new Segment(new Point(context.Route.SourcePoint.X, departureY), new Point(columnX, departureY)),
            new Segment(new Point(columnX, arrivalY), new Point(context.Route.TargetPoint.X, arrivalY))
        };
        return nodes.Values.Where(node => node.Node.Id != context.Source.Node.Id && node.Node.Id != context.Target.Node.Id &&
                node.Node.ProjectId is not null && ownership.ProjectIds.Contains(node.Node.ProjectId, StringComparer.Ordinal) &&
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
