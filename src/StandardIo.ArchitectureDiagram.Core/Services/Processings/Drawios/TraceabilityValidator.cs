using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum TraceabilityViolationCode
{
    NodeCollision,
    SharedSegment,
    ParallelSpacing,
    ReusedBend,
    ImmediateReversal
}

internal sealed record TraceabilityViolation(
    TraceabilityViolationCode Code,
    string EdgeId,
    string? OtherEdgeId,
    int Magnitude,
    string Description);

internal sealed class TraceabilityValidationResult
{
    public TraceabilityValidationResult(IEnumerable<TraceabilityViolation> violations)
    {
        Violations = violations.ToArray();
    }

    public IReadOnlyList<TraceabilityViolation> Violations { get; }

    public bool IsValid => Violations.Count == 0;
}

internal static class TraceabilityValidator
{
    public static void ThrowIfInvalid(
        TraceabilityValidationResult result,
        Func<TraceabilityViolation, bool>? isEnforced = null)
    {
        var enforced = result.Violations
            .Where(violation => isEnforced is null || isEnforced(violation))
            .ToArray();
        if (enforced.Length == 0)
        {
            return;
        }

        var examples = string.Join(
            Environment.NewLine,
            enforced.Take(10).Select(violation => violation.Description));
        throw new InvalidOperationException(
            $"Final routed geometry failed traceability validation with {enforced.Length} violation(s) in lane-owned geometry." +
            Environment.NewLine + examples);
    }

    public static TraceabilityValidationResult Validate(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links,
        int requiredParallelSpacing)
    {
        var violations = new List<TraceabilityViolation>();
        var orderedLinks = links.Values
            .OrderBy(link => link.Link.Order)
            .ThenBy(link => link.Link.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var link in orderedLinks)
        {
            var points = CompletePoints(link);
            var route = CompleteSegments(link);
            for (var index = 0; index + 2 < points.Length; index++)
            {
                var first = points[index];
                var middle = points[index + 1];
                var last = points[index + 2];
                if (!IsImmediateReversal(first, middle, last))
                {
                    continue;
                }

                violations.Add(new TraceabilityViolation(
                    TraceabilityViolationCode.ImmediateReversal,
                    link.Link.Id,
                    null,
                    Distance(first, middle) + Distance(middle, last),
                    $"Edge {link.Link.Id} immediately reverses at ({middle.X},{middle.Y})."));
            }

            foreach (var node in nodes.Values.Where(node =>
                !string.Equals(node.Node.Id, link.Link.SourceId, StringComparison.Ordinal) &&
                !string.Equals(node.Node.Id, link.Link.TargetId, StringComparison.Ordinal)))
            {
                if (route.Any(segment => segment.Intersects(node.Rect)))
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.NodeCollision,
                        link.Link.Id,
                        null,
                        1,
                        $"Edge {link.Link.Id} intersects node {node.Node.Id}."));
                }
            }
        }

        for (var leftIndex = 0; leftIndex < orderedLinks.Length; leftIndex++)
        {
            var left = orderedLinks[leftIndex];
            var leftSegments = CompleteSegments(left);
            var leftBends = InteriorPoints(left);
            for (var rightIndex = leftIndex + 1; rightIndex < orderedLinks.Length; rightIndex++)
            {
                var right = orderedLinks[rightIndex];
                var rightSegments = CompleteSegments(right);
                var rightBends = InteriorPoints(right);
                var sharedLength = leftSegments.Sum(leftSegment =>
                    rightSegments.Sum(rightSegment => leftSegment.OverlapLength(rightSegment)));
                if (sharedLength > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.SharedSegment,
                        left.Link.Id,
                        right.Link.Id,
                        sharedLength,
                        $"Edges {left.Link.Id} and {right.Link.Id} share {sharedLength}px of route."));
                }

                var spacingDeficit = ParallelSpacingDeficit(leftSegments, rightSegments, requiredParallelSpacing);
                if (spacingDeficit > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.ParallelSpacing,
                        left.Link.Id,
                        right.Link.Id,
                        spacingDeficit,
                        $"Edges {left.Link.Id} and {right.Link.Id} violate parallel spacing by up to {spacingDeficit}px."));
                }

                var reusedBends = leftBends.Intersect(rightBends).Count();
                if (reusedBends > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.ReusedBend,
                        left.Link.Id,
                        right.Link.Id,
                        reusedBends,
                        $"Edges {left.Link.Id} and {right.Link.Id} reuse {reusedBends} bend point(s)."));
                }
            }
        }

        return new TraceabilityValidationResult(violations);
    }

    internal static bool IsImmediateReversal(Point first, Point middle, Point last) =>
        first.X == middle.X && middle.X == last.X &&
            (middle.Y < Math.Min(first.Y, last.Y) || middle.Y > Math.Max(first.Y, last.Y)) ||
        first.Y == middle.Y && middle.Y == last.Y &&
            (middle.X < Math.Min(first.X, last.X) || middle.X > Math.Max(first.X, last.X));

    private static int Distance(Point first, Point second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private static Point[] CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }
            .Concat(link.Points)
            .Concat(new[] { link.TargetPoint })
            .ToArray();

    private static Segment[] CompleteSegments(LinkLayout link)
    {
        var points = CompletePoints(link);
        return Enumerable.Range(0, Math.Max(0, points.Length - 1))
            .Select(index => new Segment(points[index], points[index + 1]))
            .ToArray();
    }

    private static Point[] InteriorPoints(LinkLayout link)
    {
        var points = CompletePoints(link);
        return points.Length <= 2
            ? Array.Empty<Point>()
            : points.Skip(1).Take(points.Length - 2).ToArray();
    }

    private static int ParallelSpacingDeficit(
        IReadOnlyList<Segment> leftSegments,
        IReadOnlyList<Segment> rightSegments,
        int requiredSpacing)
    {
        var maximumDeficit = 0;
        foreach (var left in leftSegments)
        {
            foreach (var right in rightSegments)
            {
                if (left.IsHorizontal && right.IsHorizontal && RangesOverlap(left.Start.X, left.End.X, right.Start.X, right.End.X))
                {
                    var distance = Math.Abs(left.Start.Y - right.Start.Y);
                    if (distance > 0)
                    {
                        maximumDeficit = Math.Max(maximumDeficit, requiredSpacing - distance);
                    }
                }
                else if (left.IsVertical && right.IsVertical && RangesOverlap(left.Start.Y, left.End.Y, right.Start.Y, right.End.Y))
                {
                    var distance = Math.Abs(left.Start.X - right.Start.X);
                    if (distance > 0)
                    {
                        maximumDeficit = Math.Max(maximumDeficit, requiredSpacing - distance);
                    }
                }
            }
        }

        return Math.Max(0, maximumDeficit);
    }

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        Math.Min(firstStart, firstEnd) < Math.Max(secondStart, secondEnd) &&
        Math.Min(secondStart, secondEnd) < Math.Max(firstStart, firstEnd);
}
