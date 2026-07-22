using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectLayerExpansionReconciler
{
    public static IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> BaseBandExtents(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        int padding,
        int separation,
        int routeCount)
    {
        var result = new Dictionary<ProjectLayerExpansionIdentity, int>();
        foreach (var project in nodes.Values.Where(node => node.Node.ProjectId is not null)
                     .GroupBy(node => node.Node.ProjectId!, StringComparer.Ordinal))
        {
            var layers = project.GroupBy(node => node.Depth)
                .ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var upper in layers.Keys.OrderBy(depth => depth))
            {
                var upperBottom = layers[upper].Max(node => node.Rect.Bottom);
                var lowerTop = layers.TryGetValue(upper + 1, out var lower)
                    ? lower.Min(node => node.Rect.Y)
                    : upperBottom + padding * 2 + Math.Max(1, routeCount) * separation;
                result[new ProjectLayerExpansionIdentity(project.Key, upper + 1)] = lowerTop - upperBottom;
            }
        }
        return result;
    }

    public static IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> DesiredExpansions(
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> baseExtents,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> requiredExtents) =>
        baseExtents.Keys.Union(requiredExtents.Keys).Select(key => new
            {
                Key = key,
                Value = Math.Max(0,
                    (requiredExtents.TryGetValue(key, out var required) ? required : 0) -
                    (baseExtents.TryGetValue(key, out var available) ? available : 0))
            }).Where(item => item.Value > 0).OrderBy(item => item.Key.ProjectId, StringComparer.Ordinal)
            .ThenBy(item => item.Key.LowerDepth).ToDictionary(item => item.Key, item => item.Value);

    public static IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> ActualGaps(
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> baseExtents,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> expansions) =>
        baseExtents.ToDictionary(
            item => item.Key,
            item => item.Value + (expansions.TryGetValue(item.Key, out var expansion) ? expansion : 0));

    public static bool Fits(
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> actualGaps,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> requiredExtents) =>
        requiredExtents.All(required => actualGaps.TryGetValue(required.Key, out var gap) && gap >= required.Value);

    public static bool Same(
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> left,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> right) =>
        left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);

    public static IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> SafeCycleMap(
        IEnumerable<IReadOnlyDictionary<ProjectLayerExpansionIdentity, int>> states,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> required,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> baseExtents)
    {
        var requiredMap = DesiredExpansions(baseExtents, required);
        return states.Append(requiredMap).SelectMany(state => state)
            .GroupBy(item => item.Key)
            .OrderBy(group => group.Key.ProjectId, StringComparer.Ordinal).ThenBy(group => group.Key.LowerDepth)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Value));
    }

    public static string Hash(IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> map)
    {
        var text = string.Join("|", map.OrderBy(item => item.Key.ProjectId, StringComparer.Ordinal)
            .ThenBy(item => item.Key.LowerDepth)
            .Select(item => $"{item.Key.ProjectId}:{item.Key.LowerDepth}:{item.Value}"));
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text))
            .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    public static bool TryAddState(
        ISet<string> seenHashes,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> map) =>
        seenHashes.Add(Hash(map));
}
