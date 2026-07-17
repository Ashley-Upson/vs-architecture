using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record RouteRepairBudget(
    int MaximumAffectedRoutes = 64,
    int MaximumCandidatesPerFinding = 8,
    int MaximumPasses = 3,
    int MaximumEstimatedWork = 10000);

internal sealed record RouteRepairResult(
    IReadOnlyDictionary<string, LinkLayout> Links,
    CorridorObservation Corridors,
    CorridorLaneAllocation Lanes,
    EdgeTraversalCompilation Traversals,
    TraceabilityValidationResult PreRepairValidation,
    TraceabilityValidationResult PostRepairValidation,
    IReadOnlyList<RouteRepairAttempt> Attempts,
    int EstimatedWorkUsed,
    bool WorkBudgetExhausted);

internal static class RouteRepairCoordinator
{
    public static RouteRepairResult Repair(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> selectedLinks,
        DiagramSettings settings,
        RouteRepairBudget? budget = null)
    {
        budget ??= new RouteRepairBudget();
        var initial = Compile(nodes, selectedLinks, settings);
        var current = initial;
        var attempts = new List<RouteRepairAttempt>();
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var work = 0;
        var exhausted = false;

        for (var pass = 0; pass < budget.MaximumPasses; pass++)
        {
            var improved = false;
            var findings = current.Validation.Violations
                .Where(IsRepairInput)
                .OrderBy(Priority)
                .ThenBy(finding => finding.EdgeId, StringComparer.Ordinal)
                .ThenBy(finding => finding.OtherEdgeId, StringComparer.Ordinal)
                .ToArray();
            foreach (var finding in findings)
            {
                if (!affected.Contains(finding.EdgeId) && affected.Count >= budget.MaximumAffectedRoutes)
                {
                    exhausted = true;
                    continue;
                }

                var beforeLink = current.Links[finding.EdgeId];
                var beforePoints = CompletePoints(beforeLink);
                var best = current;
                var bestScore = Score(current.Validation);
                var candidateCount = 0;
                foreach (var candidate in Candidates(finding, beforeLink, nodes, settings)
                    .Take(budget.MaximumCandidatesPerFinding))
                {
                    candidateCount++;
                    work++;
                    if (work > budget.MaximumEstimatedWork)
                    {
                        exhausted = true;
                        break;
                    }

                    var trialLinks = current.Links.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                    trialLinks[finding.EdgeId] = candidate;
                    var trial = Compile(nodes, trialLinks, settings);
                    var score = Score(trial.Validation);
                    if (score.CompareTo(bestScore) < 0)
                    {
                        best = trial;
                        bestScore = score;
                    }
                }

                var applied = !ReferenceEquals(best, current);
                if (applied)
                {
                    current = best;
                    affected.Add(finding.EdgeId);
                    improved = true;
                }

                attempts.Add(new RouteRepairAttempt(
                    finding.EdgeId,
                    DrawioDiagnosticReportBuilder.CategoryName(finding),
                    applied,
                    applied
                        ? $"Accepted strict lexicographic repair after evaluating {candidateCount} candidate(s)."
                        : candidateCount == 0
                            ? "No bounded repair candidate could be constructed."
                            : "No candidate strictly improved the repair score.",
                    beforePoints.Select(ToValidationPoint).ToArray(),
                    CompletePoints(current.Links[finding.EdgeId]).Select(ToValidationPoint).ToArray(),
                    exhausted));
                if (exhausted)
                {
                    break;
                }
            }

            if (!improved || exhausted)
            {
                break;
            }
        }

        return new RouteRepairResult(
            current.Links,
            current.Corridors,
            current.Lanes,
            current.Traversals,
            initial.Validation,
            current.Validation,
            attempts,
            work,
            exhausted);
    }

    private static bool IsRepairInput(TraceabilityViolation finding) =>
        finding.Code == TraceabilityViolationCode.NodeCollision ||
        finding.Code == TraceabilityViolationCode.SharedSegment ||
        finding.Code == TraceabilityViolationCode.ParallelSpacing ||
        finding.Code == TraceabilityViolationCode.ReusedBend;

    private static int Priority(TraceabilityViolation finding) => finding.Code switch
    {
        TraceabilityViolationCode.NodeCollision => 0,
        TraceabilityViolationCode.SharedSegment => 1,
        TraceabilityViolationCode.ParallelSpacing when finding.Magnitude >= 6 => 2,
        TraceabilityViolationCode.ReusedBend => 3,
        _ => 4
    };

    private static IEnumerable<LinkLayout> Candidates(
        TraceabilityViolation finding,
        LinkLayout link,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        DiagramSettings settings)
    {
        return finding.Code switch
        {
            TraceabilityViolationCode.NodeCollision when finding.OtherNodeId is not null &&
                nodes.TryGetValue(finding.OtherNodeId, out var obstacle) =>
                    ObstacleBypasses(link, obstacle.Rect.Inflate(settings.Layout.LinkPadding), nodes),
            TraceabilityViolationCode.SharedSegment or TraceabilityViolationCode.ParallelSpacing or TraceabilityViolationCode.ReusedBend =>
                ParallelOffsets(link, finding, settings.Layout.ParallelLaneSpacing, nodes),
            _ => Array.Empty<LinkLayout>()
        };
    }

    private static IEnumerable<LinkLayout> ObstacleBypasses(
        LinkLayout link,
        Rect obstacle,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var points = CompletePoints(link);
        for (var index = 0; index < points.Count - 1; index++)
        {
            var segment = new Segment(points[index], points[index + 1]);
            if (!segment.Intersects(obstacle))
            {
                continue;
            }

            if (segment.IsHorizontal)
            {
                foreach (var y in new[] { obstacle.Y, obstacle.Bottom }.Distinct())
                {
                    var candidate = Replace(points, index, new[]
                    {
                        segment.Start,
                        new Point(segment.Start.X, y),
                        new Point(segment.End.X, y),
                        segment.End
                    });
                    if (NodeSafe(candidate, link, nodes)) yield return ToLink(link, candidate);
                }
            }
            else if (segment.IsVertical)
            {
                foreach (var x in new[] { obstacle.X, obstacle.Right }.Distinct())
                {
                    var candidate = Replace(points, index, new[]
                    {
                        segment.Start,
                        new Point(x, segment.Start.Y),
                        new Point(x, segment.End.Y),
                        segment.End
                    });
                    if (NodeSafe(candidate, link, nodes)) yield return ToLink(link, candidate);
                }
            }
        }
    }

    private static IEnumerable<LinkLayout> ParallelOffsets(
        LinkLayout link,
        TraceabilityViolation finding,
        int spacing,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var points = CompletePoints(link);
        var offending = finding.OffendingSegments ?? Array.Empty<Segment>();
        for (var index = 0; index < points.Count - 1; index++)
        {
            var segment = new Segment(points[index], points[index + 1]);
            if (!offending.Any(item => item.OverlapLength(segment) > 0 || item.Start == segment.Start || item.End == segment.End))
            {
                continue;
            }

            foreach (var offset in new[] { -spacing, spacing, -spacing * 2, spacing * 2 })
            {
                var shiftedStart = segment.IsHorizontal
                    ? new Point(segment.Start.X, segment.Start.Y + offset)
                    : new Point(segment.Start.X + offset, segment.Start.Y);
                var shiftedEnd = segment.IsHorizontal
                    ? new Point(segment.End.X, segment.End.Y + offset)
                    : new Point(segment.End.X + offset, segment.End.Y);
                var candidate = Replace(points, index, new[] { segment.Start, shiftedStart, shiftedEnd, segment.End });
                if (NodeSafe(candidate, link, nodes)) yield return ToLink(link, candidate);
            }
        }
    }

    private static Pipeline Compile(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> logicalLinks,
        DiagramSettings settings)
    {
        var corridors = CorridorObserver.Observe(nodes, logicalLinks, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
        var lanes = CorridorLaneAllocator.Allocate(corridors);
        var laneGeometry = CorridorLaneGeometryCompiler.Compile(logicalLinks, corridors, lanes);
        var traversals = EdgeTraversalCompiler.Compile(laneGeometry, corridors, lanes, nodes, logicalLinks);
        var links = EdgeTraversalCompiler.Apply(laneGeometry, traversals);
        var validation = TraceabilityValidator.Validate(nodes, links, settings.Layout.ParallelLaneSpacing);
        return new Pipeline(links, corridors, lanes, traversals, validation);
    }

    private static RepairScore Score(TraceabilityValidationResult validation) => new(
        validation.Violations.Count(item => item.Code == TraceabilityViolationCode.NodeCollision),
        validation.Violations.Where(item => item.Code == TraceabilityViolationCode.SharedSegment).Sum(item => item.Magnitude),
        validation.Violations.Where(item => item.Code == TraceabilityViolationCode.ParallelSpacing && item.Magnitude >= 6).Sum(item => item.Magnitude),
        validation.Violations.Where(item => item.Code == TraceabilityViolationCode.ReusedBend).Sum(item => item.Magnitude),
        validation.Violations.Where(item => item.Code == TraceabilityViolationCode.ParallelSpacing && item.Magnitude < 6).Sum(item => item.Magnitude));

    private static IReadOnlyList<Point> Replace(IReadOnlyList<Point> points, int segmentIndex, IReadOnlyList<Point> replacement) =>
        Normalize(points.Take(segmentIndex).Concat(replacement).Concat(points.Skip(segmentIndex + 2))).ToArray();

    private static IEnumerable<Point> Normalize(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            if (result.Count == 0 || result[result.Count - 1] != point) result.Add(point);
            while (result.Count >= 3)
            {
                var a = result[result.Count - 3];
                var b = result[result.Count - 2];
                var c = result[result.Count - 1];
                if (a.X == b.X && b.X == c.X || a.Y == b.Y && b.Y == c.Y) result.RemoveAt(result.Count - 2);
                else break;
            }
        }
        return result;
    }

    private static bool NodeSafe(IReadOnlyList<Point> points, LinkLayout link, IReadOnlyDictionary<string, NodeLayout> nodes) =>
        points.Zip(points.Skip(1), (start, end) => new Segment(start, end)).All(segment =>
            nodes.Values.Where(node => node.Node.Id != link.Link.SourceId && node.Node.Id != link.Link.TargetId)
                .All(node => !segment.Intersects(node.Rect)));

    private static LinkLayout ToLink(LinkLayout original, IReadOnlyList<Point> points) => new(
        original.Link, points[0], points[points.Count - 1], points.Skip(1).Take(points.Count - 2),
        original.ExitX, original.EntryX, original.ExitY, original.EntryY);

    private static IReadOnlyList<Point> CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();

    private static ValidationPoint ToValidationPoint(Point point) => new(point.X, point.Y);

    private sealed record Pipeline(
        IReadOnlyDictionary<string, LinkLayout> Links,
        CorridorObservation Corridors,
        CorridorLaneAllocation Lanes,
        EdgeTraversalCompilation Traversals,
        TraceabilityValidationResult Validation);

    private sealed record RepairScore(int NodeCollisions, int SharedLength, int SevereSpacing, int ReusedBends, int MinorSpacing)
        : IComparable<RepairScore>
    {
        public int CompareTo(RepairScore? other)
        {
            if (other is null) return -1;
            var left = new[] { NodeCollisions, SharedLength, SevereSpacing, ReusedBends, MinorSpacing };
            var right = new[] { other.NodeCollisions, other.SharedLength, other.SevereSpacing, other.ReusedBends, other.MinorSpacing };
            for (var index = 0; index < left.Length; index++)
            {
                var comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return comparison;
            }
            return 0;
        }
    }
}
