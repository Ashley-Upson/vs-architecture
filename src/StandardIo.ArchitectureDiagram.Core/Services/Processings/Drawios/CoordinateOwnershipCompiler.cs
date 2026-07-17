using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CoordinateOwnershipCompiler
{
    private const string RootParentId = "1";

    public static CoordinateOwnershipCompilation Compile(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects,
        IReadOnlyDictionary<string, LinkLayout> links,
        bool projectContainersEnabled)
    {
        var anchors = new List<BoundaryAnchor>();
        var segments = new List<PhysicalEdgeSegment>();

        foreach (var link in links.Values.OrderBy(item => item.Link.Order).ThenBy(item => item.Link.Id, StringComparer.Ordinal))
        {
            var compilation = CompileLink(nodes, projects, link, projectContainersEnabled);
            anchors.AddRange(compilation.Anchors);
            segments.AddRange(compilation.Segments);
        }

        return new CoordinateOwnershipCompilation(anchors, segments);
    }

    public static IReadOnlyList<Point> ReconstructAbsolutePoints(
        CoordinateOwnershipCompilation compilation,
        string logicalEdgeId)
    {
        var ordered = compilation.Segments
            .Where(segment => string.Equals(segment.LogicalEdgeId, logicalEdgeId, StringComparison.Ordinal))
            .OrderBy(segment => segment.SegmentIndex)
            .ToArray();
        var result = new List<Point>();

        foreach (var segment in ordered)
        {
            AppendDistinct(result, segment.AbsoluteStart);
            foreach (var point in segment.AbsoluteWaypoints)
            {
                AppendDistinct(result, point);
            }

            AppendDistinct(result, segment.AbsoluteEnd);
        }

        return result;
    }

    public static CoordinateOwnershipCompilation Rebase(
        CoordinateOwnershipCompilation compilation,
        IReadOnlyDictionary<string, ProjectLayout> projects)
    {
        var anchors = compilation.Anchors
            .Select(anchor => anchor with
            {
                RelativePoint = ToRelative(anchor.AbsolutePoint, projects[anchor.OwnerProjectId].Rect)
            })
            .ToArray();
        var segments = compilation.Segments
            .Select(segment => segment.OwnerProjectId is null
                ? segment
                : segment with
                {
                    RelativeWaypoints = segment.AbsoluteWaypoints
                        .Select(point => ToRelative(point, projects[segment.OwnerProjectId].Rect))
                        .ToArray()
                })
            .ToArray();
        return new CoordinateOwnershipCompilation(anchors, segments);
    }

    private static CoordinateOwnershipCompilation CompileLink(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects,
        LinkLayout link,
        bool projectContainersEnabled)
    {
        var completePoints = CompletePoints(link);
        var normalizedPoints = NormalizePolyline(completePoints);
        var logicalPoints = completePoints.Length >= 4 && normalizedPoints.Count == 2 &&
            Enumerable.Range(0, completePoints.Length - 2).Any(index =>
                TraceabilityValidator.IsImmediateReversal(
                    completePoints[index],
                    completePoints[index + 1],
                    completePoints[index + 2]))
                ? normalizedPoints
                : completePoints;
        var sourceProjectId = OwnedProjectId(nodes, projects, link.Link.SourceId, projectContainersEnabled);
        var targetProjectId = OwnedProjectId(nodes, projects, link.Link.TargetId, projectContainersEnabled);
        var relevantProjects = new[] { sourceProjectId, targetProjectId }
            .Where(id => id is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(id => projects[id!])
            .ToArray();
        var expanded = InsertBoundaryIntersections(logicalPoints, relevantProjects);
        var runs = OwnershipRuns(expanded, sourceProjectId, targetProjectId, projects);
        if (runs.Count == 0)
        {
            throw new InvalidOperationException($"Logical edge {link.Link.Id} contains no non-zero route segments.");
        }

        var anchors = new List<BoundaryAnchor>();
        var transitionAnchorIds = new string[Math.Max(0, runs.Count - 1)];
        for (var transitionIndex = 0; transitionIndex < runs.Count - 1; transitionIndex++)
        {
            var left = runs[transitionIndex];
            var right = runs[transitionIndex + 1];
            var ownerProjectId = left.OwnerProjectId ?? right.OwnerProjectId;
            if (ownerProjectId is null)
            {
                throw new InvalidOperationException(
                    $"Logical edge {link.Link.Id} transitions directly between incompatible coordinate owners.");
            }

            // Adjacent project rectangles can produce a direct project-to-project handoff with no
            // positive-length root interval. Keep one anchor in the source-side owner and connect
            // the next owner-parented segment to it; emitting a zero-length root edge would add no
            // visible geometry and would violate zero-length segment suppression.

            var absolutePoint = left.Points[left.Points.Count - 1];
            var anchorId = $"{link.Link.Id}__anchor__{transitionIndex:D3}";
            transitionAnchorIds[transitionIndex] = anchorId;
            anchors.Add(new BoundaryAnchor(
                anchorId,
                link.Link.Id,
                ownerProjectId,
                transitionIndex,
                absolutePoint,
                ToRelative(absolutePoint, projects[ownerProjectId].Rect)));
        }

        var physicalSegments = new List<PhysicalEdgeSegment>();
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var absoluteWaypoints = run.Points.Skip(1).Take(run.Points.Count - 2).ToArray();
            var relativeWaypoints = run.OwnerProjectId is null
                ? absoluteWaypoints
                : absoluteWaypoints.Select(point => ToRelative(point, projects[run.OwnerProjectId].Rect)).ToArray();
            var role = runs.Count == 1
                ? PhysicalEdgeSegmentRole.Complete
                : index == 0
                    ? PhysicalEdgeSegmentRole.Source
                    : index == runs.Count - 1
                        ? PhysicalEdgeSegmentRole.Target
                        : PhysicalEdgeSegmentRole.Middle;
            physicalSegments.Add(new PhysicalEdgeSegment(
                runs.Count == 1 ? link.Link.Id : $"{link.Link.Id}__segment__{index:D3}",
                link.Link.Id,
                link.Link.SemanticSourceId ?? link.Link.SourceId,
                link.Link.SemanticTargetId ?? link.Link.TargetId,
                index,
                role,
                run.OwnerProjectId ?? RootParentId,
                run.OwnerProjectId,
                index == 0 ? link.Link.SourceId : transitionAnchorIds[index - 1],
                index == runs.Count - 1 ? link.Link.TargetId : transitionAnchorIds[index],
                run.Points[0],
                run.Points[run.Points.Count - 1],
                absoluteWaypoints,
                relativeWaypoints,
                index == 0,
                index == runs.Count - 1,
                false,
                link));
        }

        var labelOwner = physicalSegments
            .Where(segment => segment.OwnerProjectId is null)
            .OrderByDescending(Length)
            .ThenBy(segment => segment.SegmentIndex)
            .FirstOrDefault() ?? physicalSegments
                .OrderByDescending(Length)
                .ThenBy(segment => segment.SegmentIndex)
                .First();
        physicalSegments = physicalSegments
            .Select(segment => segment with { OwnsLabel = segment.Id == labelOwner.Id })
            .ToList();

        var result = new CoordinateOwnershipCompilation(anchors, physicalSegments);
        var reconstructed = ReconstructAbsolutePoints(result, link.Link.Id);
        if (!NormalizePolyline(logicalPoints).SequenceEqual(NormalizePolyline(reconstructed)))
        {
            throw new InvalidOperationException(
                $"Coordinate ownership compilation changed logical edge {link.Link.Id} geometry.");
        }

        return result;
    }

    private static string? OwnedProjectId(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects,
        string nodeId,
        bool enabled)
    {
        if (!enabled || !nodes.TryGetValue(nodeId, out var node) ||
            node.Node.ProjectId is null || !projects.ContainsKey(node.Node.ProjectId))
        {
            return null;
        }

        return node.Node.ProjectId;
    }

    private static Point[] CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();

    private static IReadOnlyList<Point> InsertBoundaryIntersections(
        IReadOnlyList<Point> points,
        IReadOnlyList<ProjectLayout> projects)
    {
        var result = new List<Point>();
        for (var index = 0; index < points.Count - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            AppendDistinct(result, start);
            var intersections = projects
                .SelectMany(project => BoundaryIntersections(start, end, project.Rect))
                .Distinct()
                .OrderBy(point => Distance(start, point))
                .ToArray();
            foreach (var intersection in intersections)
            {
                AppendDistinct(result, intersection);
            }
        }

        AppendDistinct(result, points[points.Count - 1]);
        return result;
    }

    private static IEnumerable<Point> BoundaryIntersections(Point start, Point end, Rect rect)
    {
        if (start.Y == end.Y)
        {
            foreach (var x in new[] { rect.X, rect.Right })
            {
                if (BetweenInclusive(x, start.X, end.X) && BetweenInclusive(start.Y, rect.Y, rect.Bottom))
                {
                    yield return new Point(x, start.Y);
                }
            }
        }
        else if (start.X == end.X)
        {
            foreach (var y in new[] { rect.Y, rect.Bottom })
            {
                if (BetweenInclusive(y, start.Y, end.Y) && BetweenInclusive(start.X, rect.X, rect.Right))
                {
                    yield return new Point(start.X, y);
                }
            }
        }
    }

    private static IReadOnlyList<OwnershipRun> OwnershipRuns(
        IReadOnlyList<Point> points,
        string? sourceProjectId,
        string? targetProjectId,
        IReadOnlyDictionary<string, ProjectLayout> projects)
    {
        var runs = new List<OwnershipRun>();
        for (var index = 0; index < points.Count - 1; index++)
        {
            var start = points[index];
            var end = points[index + 1];
            if (start == end)
            {
                continue;
            }

            var owner = Owner(start, end, sourceProjectId, targetProjectId, projects);
            if (runs.Count > 0 && string.Equals(runs[runs.Count - 1].OwnerProjectId, owner, StringComparison.Ordinal))
            {
                AppendDistinct(runs[runs.Count - 1].Points, end);
            }
            else
            {
                runs.Add(new OwnershipRun(owner, new List<Point> { start, end }));
            }
        }

        return runs;
    }

    private static string? Owner(
        Point start,
        Point end,
        string? sourceProjectId,
        string? targetProjectId,
        IReadOnlyDictionary<string, ProjectLayout> projects)
    {
        if (sourceProjectId is not null && MidpointInside(start, end, projects[sourceProjectId].Rect))
        {
            return sourceProjectId;
        }

        if (targetProjectId is not null && MidpointInside(start, end, projects[targetProjectId].Rect))
        {
            return targetProjectId;
        }

        return null;
    }

    private static bool MidpointInside(Point start, Point end, Rect rect)
    {
        var doubledX = start.X + end.X;
        var doubledY = start.Y + end.Y;
        return doubledX >= rect.X * 2 && doubledX <= rect.Right * 2 &&
            doubledY >= rect.Y * 2 && doubledY <= rect.Bottom * 2;
    }

    private static Point ToRelative(Point point, Rect owner) => new(point.X - owner.X, point.Y - owner.Y);

    private static int Length(PhysicalEdgeSegment segment)
    {
        var points = new[] { segment.AbsoluteStart }
            .Concat(segment.AbsoluteWaypoints)
            .Concat(new[] { segment.AbsoluteEnd })
            .ToArray();
        return Enumerable.Range(0, points.Length - 1).Sum(index => Distance(points[index], points[index + 1]));
    }

    private static int Distance(Point left, Point right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private static IReadOnlyList<Point> NormalizePolyline(IReadOnlyList<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            AppendDistinct(result, point);
            while (result.Count >= 3)
            {
                var first = result[result.Count - 3];
                var middle = result[result.Count - 2];
                var last = result[result.Count - 1];
                if (!((first.X == middle.X && middle.X == last.X) ||
                    (first.Y == middle.Y && middle.Y == last.Y)))
                {
                    break;
                }

                result.RemoveAt(result.Count - 2);
            }
        }

        return result;
    }

    private static bool BetweenInclusive(int value, int first, int second) =>
        value >= Math.Min(first, second) && value <= Math.Max(first, second);

    private static void AppendDistinct(ICollection<Point> points, Point point)
    {
        if (points.Count == 0 || !points.Last().Equals(point))
        {
            points.Add(point);
        }
    }

    private sealed record OwnershipRun(string? OwnerProjectId, List<Point> Points);
}
