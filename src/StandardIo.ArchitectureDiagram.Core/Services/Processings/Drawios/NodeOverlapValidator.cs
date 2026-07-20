using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class NodeOverlapValidator
{
    public static IReadOnlyList<ValidationFinding> Validate(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLabelGeometry> projectLabels)
    {
        var findings = new List<ValidationFinding>();
        var ordered = nodes.Values.OrderBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
        for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
            {
                var left = ordered[leftIndex];
                var right = ordered[rightIndex];
                if (!InteriorOverlap(left.Rect, right.Rect)) continue;
                findings.Add(Finding(
                    left.Node.Id, right.Node.Id,
                    $"Architecture nodes {left.Node.Id} and {right.Node.Id} overlap in their interiors.",
                    Intersection(left.Rect, right.Rect), category: "NodeOverlap"));
            }
        }

        foreach (var node in ordered)
        foreach (var label in projectLabels.Values.OrderBy(label => label.ProjectId, StringComparer.Ordinal))
        {
            if (!InteriorOverlap(node.Rect, label.ProjectLabelObstacleBounds)) continue;
            findings.Add(Finding(
                node.Node.Id, null,
                $"Architecture node {node.Node.Id} overlaps protected project label {label.ProjectId}.",
                Intersection(node.Rect, label.ProjectLabelObstacleBounds), label.ProjectId, "NodeProjectLabelOverlap"));
        }
        return findings;
    }

    private static ValidationFinding Finding(
        string nodeId,
        string? otherNodeId,
        string description,
        Rect intersection,
        string? labelId = null,
        string category = "NodeOverlap") => new(
        category,
        nodeId,
        null,
        otherNodeId ?? labelId,
        intersection.Width * intersection.Height,
        description,
        [new ValidationPoint(intersection.X, intersection.Y), new ValidationPoint(intersection.Right, intersection.Bottom)],
        [], null, null, null, true);

    private static bool InteriorOverlap(Rect left, Rect right) =>
        Math.Min(left.Right, right.Right) > Math.Max(left.X, right.X) &&
        Math.Min(left.Bottom, right.Bottom) > Math.Max(left.Y, right.Y);

    private static Rect Intersection(Rect left, Rect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        return new Rect(x, y, Math.Min(left.Right, right.Right) - x, Math.Min(left.Bottom, right.Bottom) - y);
    }
}
