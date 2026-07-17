using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CorridorPathCandidateReducer
{
    public static IReadOnlyList<CorridorPathCandidate> Retain(
        IEnumerable<CorridorPathCandidate> candidates,
        int maximumCandidates,
        int maximumDetour)
    {
        var valid = candidates
            .Where(candidate => !candidate.HasInvalidGeometry || candidate.IsAcceptedPath)
            .GroupBy(candidate => candidate.Signature.Value, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.IsAcceptedPath)
                .ThenBy(candidate => candidate.LocalCost.PathLength)
                .ThenBy(candidate => candidate.LocalCost.BendCount)
                .ThenBy(candidate => PointKey(candidate.Points), StringComparer.Ordinal)
                .First())
            .ToArray();
        if (valid.Length == 0)
        {
            return Array.Empty<CorridorPathCandidate>();
        }

        var shortest = valid.Min(candidate => candidate.LocalCost.PathLength);
        return valid
            .Where(candidate => candidate.IsAcceptedPath || candidate.LocalCost.PathLength <= shortest + maximumDetour)
            .OrderByDescending(candidate => candidate.IsAcceptedPath)
            .ThenBy(candidate => candidate.LocalCost.PathLength)
            .ThenBy(candidate => candidate.LocalCost.BendCount)
            .ThenBy(candidate => candidate.Signature.Value, StringComparer.Ordinal)
            .Take(maximumCandidates)
            .ToArray();
    }

    private static string PointKey(IEnumerable<Point> points) =>
        string.Join(";", points.Select(point => $"{point.X},{point.Y}"));
}
