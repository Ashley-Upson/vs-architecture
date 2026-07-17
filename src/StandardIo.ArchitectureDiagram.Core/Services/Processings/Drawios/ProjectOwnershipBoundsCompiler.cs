using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectOwnershipBoundsCompiler
{
    public static IReadOnlyDictionary<string, ProjectLayout> Compile(
        IReadOnlyDictionary<string, ProjectLayout> projects,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        CoordinateOwnershipCompilation ownership,
        int containerPadding,
        int projectHeaderHeight)
    {
        var result = new Dictionary<string, ProjectLayout>(StringComparer.Ordinal);
        foreach (var project in projects.Values.OrderBy(item => item.Project.Order))
        {
            var ownedNodes = nodes.Values
                .Where(node => string.Equals(node.Node.ProjectId, project.Project.Id, StringComparison.Ordinal))
                .ToArray();
            var ownedPoints = ownership.Segments
                .Where(segment => string.Equals(segment.OwnerProjectId, project.Project.Id, StringComparison.Ordinal))
                .SelectMany(segment => new[] { segment.AbsoluteStart }
                    .Concat(segment.AbsoluteWaypoints)
                    .Concat(new[] { segment.AbsoluteEnd }))
                .Concat(ownership.Anchors
                    .Where(anchor => string.Equals(anchor.OwnerProjectId, project.Project.Id, StringComparison.Ordinal))
                    .Select(anchor => anchor.AbsolutePoint))
                .ToArray();

            if (ownedNodes.Length == 0 && ownedPoints.Length == 0)
            {
                result[project.Project.Id] = project;
                continue;
            }

            var left = Math.Min(
                ownedNodes.Select(node => node.Rect.X).DefaultIfEmpty(int.MaxValue).Min(),
                ownedPoints.Select(point => point.X).DefaultIfEmpty(int.MaxValue).Min());
            var right = Math.Max(
                ownedNodes.Select(node => node.Rect.Right).DefaultIfEmpty(int.MinValue).Max(),
                ownedPoints.Select(point => point.X).DefaultIfEmpty(int.MinValue).Max());
            var top = Math.Min(
                ownedNodes.Select(node => node.Rect.Y - projectHeaderHeight).DefaultIfEmpty(int.MaxValue).Min(),
                ownedPoints.Select(point => point.Y).DefaultIfEmpty(int.MaxValue).Min());
            var bottom = Math.Max(
                ownedNodes.Select(node => node.Rect.Bottom).DefaultIfEmpty(int.MinValue).Max(),
                ownedPoints.Select(point => point.Y).DefaultIfEmpty(int.MinValue).Max());

            result[project.Project.Id] = project with
            {
                Rect = new Rect(
                    left - containerPadding,
                    top - containerPadding,
                    right - left + containerPadding * 2,
                    bottom - top + containerPadding * 2)
            };
        }

        return result;
    }
}
