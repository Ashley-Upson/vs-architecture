using System;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class TerminalRouteCompatibility
{
    private const int ProtectedPointCount = 2;

    public static bool Preserves(CorridorPathCandidate accepted, CorridorPathCandidate candidate)
    {
        if (accepted.FanoutMemberships is null || accepted.FanoutMemberships.Count == 0)
        {
            return true;
        }

        var prefixLength = Math.Min(ProtectedPointCount, accepted.Points.Count);
        var suffixLength = Math.Min(ProtectedPointCount, accepted.Points.Count);
        return candidate.Points.Take(prefixLength).SequenceEqual(accepted.Points.Take(prefixLength)) &&
            candidate.Points.Skip(candidate.Points.Count - suffixLength)
                .SequenceEqual(accepted.Points.Skip(accepted.Points.Count - suffixLength));
    }
}
