using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record CorridorPathSignature(string Value);

internal sealed record CorridorPathLocalCost(int PathLength, int BendCount, int CanvasEscape);

internal sealed record CorridorPathCandidate(
    string EdgeId,
    IReadOnlyList<string> CorridorIds,
    IReadOnlyList<string> JunctionIds,
    CorridorPathSignature Signature,
    CorridorPathLocalCost LocalCost,
    IReadOnlyList<Point> Points,
    bool HasInvalidGeometry = false,
    int AmbiguousTransitions = 0,
    bool IsAcceptedPath = false);

internal sealed record GlobalRouteScore(
    int InvalidGeometry,
    int SharedSegmentLength,
    int SpacingDeficit,
    int AmbiguousTransitions,
    int CapacityFailure,
    int CrossingsAndCongestion,
    int CanvasEscape,
    int PathEconomy) : IComparable<GlobalRouteScore>
{
    public int CompareTo(GlobalRouteScore? other)
    {
        if (other is null)
        {
            return -1;
        }

        var left = new[] { InvalidGeometry, SharedSegmentLength, SpacingDeficit, AmbiguousTransitions,
            CapacityFailure, CrossingsAndCongestion, CanvasEscape, PathEconomy };
        var right = new[] { other.InvalidGeometry, other.SharedSegmentLength, other.SpacingDeficit,
            other.AmbiguousTransitions, other.CapacityFailure, other.CrossingsAndCongestion,
            other.CanvasEscape, other.PathEconomy };
        for (var index = 0; index < left.Length; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}

internal sealed record CorridorPathDecision(
    string EdgeId,
    string InitialSignature,
    string FinalSignature,
    string Reason);

internal sealed record CorridorPathSelectionResult(
    IReadOnlyDictionary<string, CorridorPathCandidate> Selected,
    GlobalRouteScore InitialScore,
    GlobalRouteScore FinalScore,
    IReadOnlyList<CorridorPathDecision> Decisions,
    int CompletedPasses);
