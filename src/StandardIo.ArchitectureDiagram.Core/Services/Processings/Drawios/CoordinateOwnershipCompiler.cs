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

    private static CoordinateOwnershipCompilation CompileLink(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects,
        LinkLayout link,
        bool projectContainersEnabled)
    {
        var completePoints = CompletePoints(link);
        var sourceProjectId = OwnedProjectId(nodes, projects, link.Link.SourceId, projectContainersEnabled);
        var absoluteWaypoints = link.Points.ToArray();
        var relativeWaypoints = sourceProjectId is null
            ? absoluteWaypoints
            : absoluteWaypoints.Select(point => ToRelative(point, projects[sourceProjectId].Rect)).ToArray();
        var edge = new PhysicalEdgeSegment(
            link.Link.Id,
            link.Link.Id,
            link.Link.SemanticSourceId ?? link.Link.SourceId,
            link.Link.SemanticTargetId ?? link.Link.TargetId,
            0,
            PhysicalEdgeSegmentRole.Complete,
            sourceProjectId ?? RootParentId,
            sourceProjectId,
            link.Link.SourceId,
            link.Link.TargetId,
            completePoints[0],
            completePoints[completePoints.Length - 1],
            absoluteWaypoints,
            relativeWaypoints,
            true,
            true,
            true,
            link);
        var result = new CoordinateOwnershipCompilation(Array.Empty<BoundaryAnchor>(), new[] { edge });
        var reconstructed = ReconstructAbsolutePoints(result, link.Link.Id);
        if (!EquivalentPolyline(completePoints, reconstructed))
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

    private static Point ToRelative(Point point, Rect owner) => new(point.X - owner.X, point.Y - owner.Y);

    private static bool EquivalentPolyline(
        IReadOnlyList<Point> expected,
        IReadOnlyList<Point> actual) =>
        RemoveRedundantSubdivisions(expected).SequenceEqual(RemoveRedundantSubdivisions(actual));

    private static IReadOnlyList<Point> RemoveRedundantSubdivisions(IReadOnlyList<Point> points)
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
                var verticalSubdivision = first.X == middle.X && middle.X == last.X &&
                    BetweenInclusive(middle.Y, first.Y, last.Y);
                var horizontalSubdivision = first.Y == middle.Y && middle.Y == last.Y &&
                    BetweenInclusive(middle.X, first.X, last.X);
                if (!verticalSubdivision && !horizontalSubdivision)
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
}
