using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record PreAssignmentConstraintDemandReport(
    IReadOnlyList<PositionalConstraintDemand> Demands,
    IReadOnlyList<ColumnToEnvelopeDifferenceConstraint> ColumnToEnvelopeConstraints,
    IReadOnlyList<ColumnToColumnDifferenceConstraint> ColumnToColumnConstraints,
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
            var bandBlockers = placement.Nodes.Values.Where(node =>
                    node.Node.Id != column.SourceSubtreeId && node.Node.Id != column.DestinationSubtreeId &&
                    Math.Max(column.VerticalInterval.Minimum, node.Rect.Y - column.RequiredClearance) <=
                    Math.Min(column.VerticalInterval.Maximum, node.Rect.Bottom + column.RequiredClearance))
                .OrderBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
            var leftClear = NearestClearColumn(column.PreferredX, bandBlockers, column.RequiredClearance,
                HorizontalMovementDirection.Left);
            var rightClear = NearestClearColumn(column.PreferredX, bandBlockers, column.RequiredClearance,
                HorizontalMovementDirection.Right);
            foreach (var blocker in bandBlockers.Where(node =>
                         vertical.Intersects(node.Rect.Inflate(column.RequiredClearance)))
                     .OrderBy(node => node.Node.Id, StringComparer.Ordinal))
            {
                obstacles++;
                columnEnvelopeConstraints.Add(new ColumnToEnvelopeDifferenceConstraint(
                    $"column-envelope:{column.Id}:{blocker.Node.Id}", column.LinkId, column.Id,
                    column.PreferredX,
                    column.DestinationSubtreeId, target.Rect, blocker.Node.Id, blocker.Rect,
                    new AxisInterval(column.SourceLayer, column.DestinationLayer), column.RequiredClearance,
                    new HorizontalDifferenceAlternative(HorizontalMovementDirection.Left,
                        leftClear,
                        PreAssignmentMovementPlanner.CandidateScopes(column.DestinationSubtreeId, placement, HorizontalMovementDirection.Left)
                            .Concat(PreAssignmentMovementPlanner.CandidateScopes(blocker.Node.Id, placement, HorizontalMovementDirection.Right)).Distinct().ToArray()),
                    new HorizontalDifferenceAlternative(HorizontalMovementDirection.Right,
                        rightClear,
                        PreAssignmentMovementPlanner.CandidateScopes(column.DestinationSubtreeId, placement, HorizontalMovementDirection.Right)
                            .Concat(PreAssignmentMovementPlanner.CandidateScopes(blocker.Node.Id, placement, HorizontalMovementDirection.Left)).Distinct().ToArray()),
                    placement.Revision, placement.Revision));
            }
        }

        // Return departure and arrival clearance is allocated canonically in the adjacent InterLayers.
        // A fixed-Y obstacle test here would reintroduce the superseded return-stub assumption.
        var returnObstacles = 0;
        return new PreAssignmentConstraintDemandReport(
            demands.GroupBy(item => item.Id, StringComparer.Ordinal).Select(item => item.First())
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            columnEnvelopeConstraints.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            columnColumnConstraints.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            conflicts, obstacles, returnObstacles);
    }

    private static int NearestClearColumn(
        int preferredX,
        IReadOnlyList<NodeLayout> blockers,
        int clearance,
        HorizontalMovementDirection direction)
    {
        var candidate = preferredX;
        while (true)
        {
            var containing = blockers.Select(item => item.Rect.Inflate(clearance))
                .Where(rect => candidate >= rect.X && candidate <= rect.Right).ToArray();
            if (containing.Length == 0) return candidate;
            candidate = direction == HorizontalMovementDirection.Left
                ? containing.Min(item => item.X) - 1
                : containing.Max(item => item.Right) + 1;
        }
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
