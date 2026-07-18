using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record TerminalAttachmentClaim(
    string LogicalRouteId,
    string NodeId,
    TerminalAttachmentSide Side,
    string RegionId,
    int? AssignedAxisCoordinate = null);

internal static class TerminalInteractionEdges
{
    public static IReadOnlyList<ConflictEdge> BeforeAllocation(IEnumerable<TerminalAttachmentClaim> claims) =>
        PairGroups(claims.GroupBy(KeyBeforeAllocation), "TerminalSideCompetition");

    public static IReadOnlyList<ConflictEdge> AfterAllocation(IEnumerable<TerminalAttachmentClaim> claims) =>
        PairGroups(
            claims.Where(claim => claim.AssignedAxisCoordinate.HasValue)
                .GroupBy(claim => $"{KeyBeforeAllocation(claim)}:{claim.AssignedAxisCoordinate!.Value}"),
            "AssignedTerminalContact");

    private static string KeyBeforeAllocation(TerminalAttachmentClaim claim) =>
        $"{claim.NodeId}:{claim.Side}:{claim.RegionId}";

    private static IReadOnlyList<ConflictEdge> PairGroups(
        IEnumerable<IGrouping<string, TerminalAttachmentClaim>> groups,
        string cause)
    {
        var edges = new List<ConflictEdge>();
        foreach (var group in groups.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var routes = group.Select(item => item.LogicalRouteId).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            for (var left = 0; left < routes.Length; left++)
                for (var right = left + 1; right < routes.Length; right++)
                    edges.Add(new ConflictEdge(routes[left], routes[right], cause));
        }
        return edges;
    }
}
