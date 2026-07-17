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
    string Description,
    string? OtherNodeId = null,
    IReadOnlyList<Point>? Locations = null,
    IReadOnlyList<Segment>? OffendingSegments = null,
    int? RequiredSpacing = null,
    int? ActualSpacing = null,
    int? ParallelOverlapLength = null);

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
                var collidingSegments = route.Where(segment => segment.Intersects(node.Rect)).ToArray();
                if (collidingSegments.Length > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.NodeCollision,
                        link.Link.Id,
                        null,
                        1,
                        $"Edge {link.Link.Id} intersects node {node.Node.Id}.",
                        node.Node.Id,
                        collidingSegments.Select(segment => CollisionLocation(segment, node.Rect)).Distinct().ToArray(),
                        collidingSegments));
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
                var sharedSegments = leftSegments.SelectMany(leftSegment =>
                    rightSegments.Select(rightSegment => SharedSegment(leftSegment, rightSegment)))
                    .Where(segment => segment is not null)
                    .Select(segment => segment!.Value)
                    .ToArray();
                var sharedLength = sharedSegments.Sum(segment => segment.Length);
                if (sharedLength > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.SharedSegment,
                        left.Link.Id,
                        right.Link.Id,
                        sharedLength,
                        $"Edges {left.Link.Id} and {right.Link.Id} share {sharedLength}px of route.",
                        Locations: sharedSegments.Select(segment => Midpoint(segment)).Distinct().ToArray(),
                        OffendingSegments: sharedSegments,
                        ParallelOverlapLength: sharedLength));
                }

                var spacing = ParallelSpacing(leftSegments, rightSegments, requiredParallelSpacing);
                var spacingDeficit = spacing.Deficit;
                if (spacingDeficit > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.ParallelSpacing,
                        left.Link.Id,
                        right.Link.Id,
                        spacingDeficit,
                        $"Edges {left.Link.Id} and {right.Link.Id} violate parallel spacing by up to {spacingDeficit}px.",
                        Locations: spacing.Segments.Select(segment => Midpoint(segment)).Distinct().ToArray(),
                        OffendingSegments: spacing.Segments,
                        RequiredSpacing: requiredParallelSpacing,
                        ActualSpacing: spacing.Actual,
                        ParallelOverlapLength: spacing.OverlapLength));
                }

                var reusedBends = leftBends.Intersect(rightBends).Count();
                if (reusedBends > 0)
                {
                    violations.Add(new TraceabilityViolation(
                        TraceabilityViolationCode.ReusedBend,
                        left.Link.Id,
                        right.Link.Id,
                        reusedBends,
                        $"Edges {left.Link.Id} and {right.Link.Id} reuse {reusedBends} bend point(s).",
                        Locations: leftBends.Intersect(rightBends).ToArray()));
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

    private static (int Deficit, int Actual, int OverlapLength, IReadOnlyList<Segment> Segments) ParallelSpacing(
        IReadOnlyList<Segment> leftSegments,
        IReadOnlyList<Segment> rightSegments,
        int requiredSpacing)
    {
        var maximumDeficit = 0;
        var actual = requiredSpacing;
        var overlapLength = 0;
        var offending = new List<Segment>();
        foreach (var left in leftSegments)
        {
            foreach (var right in rightSegments)
            {
                if (left.IsHorizontal && right.IsHorizontal && RangesOverlap(left.Start.X, left.End.X, right.Start.X, right.End.X))
                {
                    var distance = Math.Abs(left.Start.Y - right.Start.Y);
                    if (distance > 0 && distance < requiredSpacing)
                    {
                        maximumDeficit = Math.Max(maximumDeficit, requiredSpacing - distance);
                        actual = Math.Min(actual, distance);
                        var overlap = AxisOverlap(left.Start.X, left.End.X, right.Start.X, right.End.X);
                        overlapLength += overlap.End - overlap.Start;
                        offending.Add(new Segment(new Point(overlap.Start, left.Start.Y), new Point(overlap.End, left.Start.Y)));
                    }
                }
                else if (left.IsVertical && right.IsVertical && RangesOverlap(left.Start.Y, left.End.Y, right.Start.Y, right.End.Y))
                {
                    var distance = Math.Abs(left.Start.X - right.Start.X);
                    if (distance > 0 && distance < requiredSpacing)
                    {
                        maximumDeficit = Math.Max(maximumDeficit, requiredSpacing - distance);
                        actual = Math.Min(actual, distance);
                        var overlap = AxisOverlap(left.Start.Y, left.End.Y, right.Start.Y, right.End.Y);
                        overlapLength += overlap.End - overlap.Start;
                        offending.Add(new Segment(new Point(left.Start.X, overlap.Start), new Point(left.Start.X, overlap.End)));
                    }
                }
            }
        }

        return (Math.Max(0, maximumDeficit), actual, overlapLength, offending);
    }

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        Math.Min(firstStart, firstEnd) < Math.Max(secondStart, secondEnd) &&
        Math.Min(secondStart, secondEnd) < Math.Max(firstStart, firstEnd);

    private static (int Start, int End) AxisOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        (Math.Max(Math.Min(firstStart, firstEnd), Math.Min(secondStart, secondEnd)),
            Math.Min(Math.Max(firstStart, firstEnd), Math.Max(secondStart, secondEnd)));

    private static Segment? SharedSegment(Segment left, Segment right)
    {
        if (left.IsHorizontal && right.IsHorizontal && left.Start.Y == right.Start.Y)
        {
            var overlap = AxisOverlap(left.Start.X, left.End.X, right.Start.X, right.End.X);
            return overlap.End > overlap.Start
                ? new Segment(new Point(overlap.Start, left.Start.Y), new Point(overlap.End, left.Start.Y))
                : null;
        }

        if (left.IsVertical && right.IsVertical && left.Start.X == right.Start.X)
        {
            var overlap = AxisOverlap(left.Start.Y, left.End.Y, right.Start.Y, right.End.Y);
            return overlap.End > overlap.Start
                ? new Segment(new Point(left.Start.X, overlap.Start), new Point(left.Start.X, overlap.End))
                : null;
        }

        return null;
    }

    private static Point CollisionLocation(Segment segment, Rect rect)
    {
        if (segment.IsHorizontal)
        {
            var start = Math.Max(Math.Min(segment.Start.X, segment.End.X), rect.X);
            var end = Math.Min(Math.Max(segment.Start.X, segment.End.X), rect.Right);
            return new Point((start + end) / 2, segment.Start.Y);
        }

        if (segment.IsVertical)
        {
            var start = Math.Max(Math.Min(segment.Start.Y, segment.End.Y), rect.Y);
            var end = Math.Min(Math.Max(segment.Start.Y, segment.End.Y), rect.Bottom);
            return new Point(segment.Start.X, (start + end) / 2);
        }

        return Midpoint(segment);
    }

    private static Point Midpoint(Segment segment) =>
        new((segment.Start.X + segment.End.X) / 2, (segment.Start.Y + segment.End.Y) / 2);
}
