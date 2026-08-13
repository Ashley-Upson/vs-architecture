using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>
/// Canonicalises already allocated physical route geometry. It has no routing
/// or allocation authority and never changes a lane/resource assignment.
/// </summary>
public sealed class ArchitectureV7AllocatedRouteSimplificationStage
{
    public ArchitectureV7RouteSimplificationResult Simplify(ArchitectureV7PhysicalSceneFreeze scene)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        var diagnostics = scene.Diagnostics.ToList();
        var evidence = new List<ArchitectureV7RouteSimplificationEvidence>();
        var routes = new List<ArchitectureV7PhysicalRoute>();
        foreach (var route in scene.Routes)
        {
            var result = SimplifyRoute(route, diagnostics);
            routes.Add(result.Route);
            evidence.Add(result.Evidence);
        }

        var fingerprintPayload = scene.PhysicalSceneFingerprint + "|" + string.Join(";", evidence.Select(item =>
            string.Join(":", item.PhysicalLinkId, item.PointsBefore, item.PointsAfter, item.RedundantPointsRemoved,
                item.ReversalTransitions, item.OvershootGroups, item.SameAxisLaneMismatchCount, item.DiagonalSegmentCount)));
        using var sha = SHA256.Create();
        var fingerprint = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintPayload))
            .Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        var simplified = new ArchitectureV7PhysicalSceneFreeze(scene.Rows, scene.Columns, scene.Nodes, scene.Terminals,
            routes, diagnostics, scene.PlacementFingerprint, scene.RouteFingerprint, scene.AllocationFingerprint, fingerprint,
            scene.AccountedPhysicalLinkIds);
        return new ArchitectureV7RouteSimplificationResult(simplified, evidence);
    }

    private static (ArchitectureV7PhysicalRoute Route, ArchitectureV7RouteSimplificationEvidence Evidence) SimplifyRoute(
        ArchitectureV7PhysicalRoute route, ICollection<ArchitectureV7PhysicalSceneDiagnostic> diagnostics)
    {
        var points = route.Points;
        if (points.Count < 2 || route.Segments.Count == 0)
            return (route, new(route.PhysicalLinkId, points.Count, points.Count, 0, 0, 0, 0, 0, false, Array.Empty<string>()));

        var intervals = new List<Interval>();
        var diagonalCount = 0;
        for (var index = 0; index + 1 < points.Count; index++)
        {
            var segment = route.Segments[Math.Min(index, route.Segments.Count - 1)];
            var a = points[index];
            var b = points[index + 1];
            var duplicate = a.X == b.X && a.Y == b.Y;
            var horizontal = a.Y == b.Y && a.X != b.X;
            var vertical = a.X == b.X && a.Y != b.Y;
            if (!duplicate && !horizontal && !vertical)
            {
                diagonalCount++;
                diagnostics.Add(new("SIMPLIFIER-DIAGONAL", "Allocated route contains a diagonal segment before canonicalisation.", true, route.PhysicalLinkId));
            }
            intervals.Add(new(index, index + 1, horizontal ? Axis.Horizontal : vertical ? Axis.Vertical : Axis.None,
                segment.LaneId, segment));
        }

        var laneMismatchCount = intervals.Zip(intervals.Skip(1), (left, right) => (left, right))
            .Count(pair =>
            {
                if (pair.left.Axis == Axis.None || pair.left.Axis != pair.right.Axis ||
                    string.Equals(pair.left.LaneId, pair.right.LaneId, StringComparison.Ordinal)) return false;
                // A compiled bend owns the transition point, so its resource
                // provenance can carry the incoming lane while the following
                // interval is already on the outgoing run. That is not a
                // same-axis straight-run conflict.
                if (IsEndpointResource(pair.left.Segment) || IsEndpointResource(pair.right.Segment) ||
                    IsBendResource(pair.left.Segment) || IsBendResource(pair.right.Segment)) return false;
                diagnostics.Add(new("SIMPLIFIER-SAME-AXIS-LANE-MISMATCH", "A same-axis allocated group uses more than one lane; simplification was refused.", true, route.PhysicalLinkId));
                return true;
            });

        var reversalTransitions = intervals.Where(item => item.Axis != Axis.None).Zip(
            intervals.Where(item => item.Axis != Axis.None).Skip(1), (left, right) => (left, right))
            .Count(pair => pair.left.Axis == pair.right.Axis && Direction(pair.left, points) != 0 && Direction(pair.right, points) != 0 && Direction(pair.left, points) != Direction(pair.right, points));
        var overshootGroups = reversalTransitions;
        var resourceProvenance = route.Segments.Select(segment => segment.AllocationProvenance)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (laneMismatchCount > 0 || diagonalCount > 0)
            return (route, new(route.PhysicalLinkId, points.Count, points.Count, 0, reversalTransitions, overshootGroups,
                laneMismatchCount, diagonalCount, false, resourceProvenance));

        var retainedIndexes = new List<int> { 0 };
        var intervalIndex = 0;
        while (intervalIndex < intervals.Count)
        {
            while (intervalIndex < intervals.Count && intervals[intervalIndex].Axis == Axis.None) intervalIndex++;
            if (intervalIndex == intervals.Count) break;

            // Duplicate points are not route topology. Ignore them while deciding
            // whether the allocated same-axis run is one canonical group. This
            // also prevents endpoint handoff bookkeeping points from turning a
            // straight run into an artificial reversal.
            var first = intervals[intervalIndex];
            var last = first;
            var probe = intervalIndex + 1;
            while (probe < intervals.Count)
            {
                while (probe < intervals.Count && intervals[probe].Axis == Axis.None) probe++;
                if (probe == intervals.Count || intervals[probe].Axis != first.Axis || !CanJoin(last, intervals[probe])) break;
                last = intervals[probe];
                probe++;
            }
            retainedIndexes.Add(last.EndPointIndex);
            intervalIndex = probe;
        }
        if (retainedIndexes[retainedIndexes.Count - 1] != points.Count - 1) retainedIndexes.Add(points.Count - 1);
        retainedIndexes = retainedIndexes.Distinct().OrderBy(value => value).ToList();
        retainedIndexes = retainedIndexes
            .Where((value, index) => index == 0 || points[value].X != points[retainedIndexes[index - 1]].X || points[value].Y != points[retainedIndexes[index - 1]].Y)
            .ToList();

        var simplifiedPoints = retainedIndexes.Select(value => points[value]).ToArray();
        var simplifiedSegments = new List<ArchitectureV7PhysicalSegment>();
        for (var outputIndex = 0; outputIndex + 1 < retainedIndexes.Count; outputIndex++)
        {
            var start = retainedIndexes[outputIndex];
            var end = retainedIndexes[outputIndex + 1];
            var covered = route.Segments.Skip(start).Take(Math.Max(1, end - start)).ToArray();
            var source = covered.FirstOrDefault(segment => !IsEndpointResource(segment)) ?? covered[0];
            simplifiedSegments.Add(new(route.PhysicalLinkId, simplifiedPoints[outputIndex], simplifiedPoints[outputIndex + 1],
                covered.SelectMany(segment => segment.RouteCellIndices).Distinct().ToArray(),
                covered.SelectMany(segment => segment.LogicalCells).Distinct().ToArray(), source.RunId, source.LaneId,
                "canonical-simplification;" + string.Join("|", covered.Select(segment => segment.AllocationProvenance).Distinct(StringComparer.Ordinal))));
        }
        var canonicalPoints = new List<ArchitectureV7PhysicalPoint> { simplifiedPoints[0] };
        var canonicalSegments = new List<ArchitectureV7PhysicalSegment>();
        foreach (var segment in simplifiedSegments)
        {
            if (canonicalSegments.Count > 0 && SameAxis(canonicalSegments[canonicalSegments.Count - 1].Start, canonicalSegments[canonicalSegments.Count - 1].End, segment.Start, segment.End) &&
                CanJoin(new Interval(0, 1, AxisOf(canonicalSegments[canonicalSegments.Count - 1].Start, canonicalSegments[canonicalSegments.Count - 1].End), canonicalSegments[canonicalSegments.Count - 1].LaneId, canonicalSegments[canonicalSegments.Count - 1]),
                    new Interval(0, 1, AxisOf(segment.Start, segment.End), segment.LaneId, segment)))
            {
                var previous = canonicalSegments[canonicalSegments.Count - 1];
                canonicalSegments[canonicalSegments.Count - 1] = MergeSegments(previous, segment, segment.End);
                canonicalPoints[canonicalPoints.Count - 1] = segment.End;
            }
            else
            {
                canonicalSegments.Add(segment);
                canonicalPoints.Add(segment.End);
            }
        }
        simplifiedPoints = canonicalPoints.ToArray();
        simplifiedSegments = canonicalSegments;
        var collapsed = CollapseCollinearReversals(simplifiedPoints, simplifiedSegments);
        simplifiedPoints = collapsed.Points.ToArray();
        simplifiedSegments = collapsed.Segments.ToList();
        var simplifiedRoute = new ArchitectureV7PhysicalRoute(route.PhysicalLinkId, simplifiedPoints, simplifiedSegments,
            "allocated-route-canonicalisation;source=" + route.Provenance);
        return (simplifiedRoute, new(route.PhysicalLinkId, points.Count, simplifiedPoints.Length,
            points.Count - simplifiedPoints.Length, reversalTransitions, overshootGroups, 0, 0, true, resourceProvenance));
    }

    private static int Direction(Interval interval, IReadOnlyList<ArchitectureV7PhysicalPoint> points)
    {
        var a = points[interval.StartPointIndex];
        var b = points[interval.EndPointIndex];
        return interval.Axis == Axis.Horizontal ? Math.Sign(b.X - a.X) : Math.Sign(b.Y - a.Y);
    }

    private static bool IsEndpointResource(ArchitectureV7PhysicalSegment segment) =>
        segment.RunId.StartsWith("handoff:", StringComparison.Ordinal) || segment.RunId.StartsWith("terminal:", StringComparison.Ordinal);

    private static bool IsBendResource(ArchitectureV7PhysicalSegment segment) =>
        segment.AllocationProvenance.Contains("bend-resource=", StringComparison.Ordinal);

    private static (IReadOnlyList<ArchitectureV7PhysicalPoint> Points, IReadOnlyList<ArchitectureV7PhysicalSegment> Segments)
        CollapseCollinearReversals(IReadOnlyList<ArchitectureV7PhysicalPoint> points,
            IReadOnlyList<ArchitectureV7PhysicalSegment> segments)
    {
        var outputPoints = points.ToList();
        var outputSegments = segments.ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var index = 1; index + 1 < outputPoints.Count; index++)
            {
                var before = outputPoints[index - 1];
                var middle = outputPoints[index];
                var after = outputPoints[index + 1];
                var horizontal = before.Y == middle.Y && middle.Y == after.Y;
                var vertical = before.X == middle.X && middle.X == after.X;
                if (!horizontal && !vertical) continue;
                var firstDirection = horizontal ? Math.Sign(middle.X - before.X) : Math.Sign(middle.Y - before.Y);
                var secondDirection = horizontal ? Math.Sign(after.X - middle.X) : Math.Sign(after.Y - middle.Y);
                if (firstDirection == 0 || secondDirection == 0 || firstDirection == secondDirection) continue;

                var merged = MergeSegments(outputSegments[index - 1], outputSegments[index], after);
                outputPoints.RemoveAt(index);
                outputSegments[index - 1] = merged;
                outputSegments.RemoveAt(index);
                changed = true;
                break;
            }
        }
        return (outputPoints, outputSegments);
    }

    private static bool CanJoin(Interval left, Interval right) =>
        string.Equals(left.LaneId, right.LaneId, StringComparison.Ordinal) ||
        IsEndpointResource(left.Segment) || IsEndpointResource(right.Segment);

    private static Axis AxisOf(ArchitectureV7PhysicalPoint a, ArchitectureV7PhysicalPoint b) =>
        a.Y == b.Y && a.X != b.X ? Axis.Horizontal : a.X == b.X && a.Y != b.Y ? Axis.Vertical : Axis.None;

    private static bool SameAxis(ArchitectureV7PhysicalPoint a, ArchitectureV7PhysicalPoint b,
        ArchitectureV7PhysicalPoint c, ArchitectureV7PhysicalPoint d) =>
        AxisOf(a, b) != Axis.None && AxisOf(a, b) == AxisOf(c, d);

    private static ArchitectureV7PhysicalSegment MergeSegments(ArchitectureV7PhysicalSegment left,
        ArchitectureV7PhysicalSegment right, ArchitectureV7PhysicalPoint end) =>
        new(left.PhysicalLinkId, left.Start, end,
            left.RouteCellIndices.Concat(right.RouteCellIndices).Distinct().ToArray(),
            left.LogicalCells.Concat(right.LogicalCells).Distinct().ToArray(),
            IsEndpointResource(left) ? right.RunId : left.RunId,
            IsEndpointResource(left) ? right.LaneId : left.LaneId,
            "canonical-simplification;" + left.AllocationProvenance + "|" + right.AllocationProvenance);

    private enum Axis { None, Horizontal, Vertical }
    private sealed record Interval(int StartPointIndex, int EndPointIndex, Axis Axis, string LaneId, ArchitectureV7PhysicalSegment Segment);
}

internal static class ArchitectureV7SimplificationEnumerableExtensions
{
    public static IEnumerable<IReadOnlyList<T>> GroupAdjacent<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
    {
        var group = new List<T>();
        var comparer = EqualityComparer<TKey>.Default;
        var hasKey = false;
        TKey? key = default;
        foreach (var item in source)
        {
            var itemKey = keySelector(item);
            if (hasKey && !comparer.Equals(key!, itemKey))
            {
                yield return group.ToArray();
                group.Clear();
            }
            key = itemKey;
            hasKey = true;
            group.Add(item);
        }
        if (group.Count > 0) yield return group.ToArray();
    }
}
