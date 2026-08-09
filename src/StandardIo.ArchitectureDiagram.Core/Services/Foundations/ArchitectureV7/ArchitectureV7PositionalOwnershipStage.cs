using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7PositionalOwnershipStage
{
    public ArchitectureV7PositionalOwnershipResult Resolve(ArchitectureV7PhysicalProjectionResult projection)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));

        var nodesById = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var incomingByTarget = projection.PhysicalLinks
            .Where(link => nodesById.ContainsKey(link.SourcePhysicalNodeId) && nodesById.ContainsKey(link.DestinationPhysicalNodeId))
            .GroupBy(link => link.DestinationPhysicalNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(link => link.SemanticLinkId, StringComparer.Ordinal)
                .ThenBy(link => link.SourcePhysicalNodeId, StringComparer.Ordinal)
                .ThenBy(link => link.PhysicalLinkId, StringComparer.Ordinal)
                .ToArray(), StringComparer.Ordinal);

        var decisions = projection.PhysicalNodes.OrderBy(node => node.ProjectId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(node => node.SemanticNodeId, StringComparer.Ordinal)
            .ThenBy(node => node.PhysicalNodeId, StringComparer.Ordinal)
            .Select(node =>
            {
                var incoming = incomingByTarget.TryGetValue(node.PhysicalNodeId, out var links) ? links : Array.Empty<ArchitectureV7PhysicalLink>();
                var incomingIds = incoming.Select(link => link.SourcePhysicalNodeId).Distinct(StringComparer.Ordinal).ToArray();
                var eligible = incoming.Where(link => string.Equals(link.SourceProjectId, link.DestinationProjectId, StringComparison.Ordinal))
                    .Select(link => link.SourcePhysicalNodeId).Distinct(StringComparer.Ordinal).ToArray();
                var positional = eligible.FirstOrDefault();
                return new ArchitectureV7PositionalOwnershipDecision(node.PhysicalNodeId, node.SemanticNodeId,
                    positional, incomingIds, incomingIds.Where(id => !string.Equals(id, positional, StringComparison.Ordinal)).ToArray());
            }).ToArray();

        return new ArchitectureV7PositionalOwnershipResult(projection, decisions, projection.FreezeFingerprint,
            Fingerprint(projection.FreezeFingerprint, decisions));
    }

    private static string Fingerprint(string projectionFingerprint, IReadOnlyList<ArchitectureV7PositionalOwnershipDecision> decisions)
    {
        var text = projectionFingerprint + "#" + string.Join("|", decisions.Select(decision =>
            decision.PhysicalNodeId + ":" + decision.PositionalParentPhysicalNodeId + ":" +
            string.Join(",", decision.IncomingPhysicalParentIds) + ":" +
            string.Join(",", decision.AdditionalSemanticParentPhysicalNodeIds)));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }
}
