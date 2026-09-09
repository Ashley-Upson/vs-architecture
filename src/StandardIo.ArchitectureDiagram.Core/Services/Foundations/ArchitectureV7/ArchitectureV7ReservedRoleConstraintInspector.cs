using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7ReservedRoleConstraintInspector
{
    public ArchitectureV7ReservationInspectionResult Inspect(
        ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7PrePlacementConfiguration configuration)
    {
        if (ownership is null) throw new ArgumentNullException(nameof(ownership));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var nodes = ownership.Projection.PhysicalNodes.OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        var decisions = ownership.Decisions.ToDictionary(decision => decision.PhysicalNodeId, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int Depth(string physicalNodeId)
        {
            if (depths.TryGetValue(physicalNodeId, out var known)) return known;
            if (!visiting.Add(physicalNodeId))
            {
                diagnostics.Add("v7-reservation-cycle:" + physicalNodeId);
                return 0;
            }

            var depth = 0;
            if (decisions.TryGetValue(physicalNodeId, out var decision) && decision.PositionalParentPhysicalNodeId is { } parentId)
                depth = Depth(parentId) + 1;
            visiting.Remove(physicalNodeId);
            depths[physicalNodeId] = depth;
            return depth;
        }

        foreach (var node in nodes) Depth(node.PhysicalNodeId);

        var rules = (configuration.ReservedLayerTypePatterns ?? Array.Empty<ArchitectureV7ReservedRoleRule>())
            .Select((rule, index) => (rule, index))
            .OrderBy(item => item.rule.Order)
            .ThenBy(item => item.index)
            .ToArray();
        var grouped = new Dictionary<string, List<ArchitectureV7ReservedDepthConstraint>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var role = ResolveRole(node, rules);
            if (role is null) continue;
            var depth = depths[node.PhysicalNodeId];
            if (!grouped.TryGetValue(role.Name, out var constraints))
            {
                constraints = new List<ArchitectureV7ReservedDepthConstraint>();
                grouped.Add(role.Name, constraints);
            }
            constraints.Add(new ArchitectureV7ReservedDepthConstraint(role.Name, node.PhysicalNodeId, depth,
                RequiredNodeRow(depth), node.IsExternal,
                "v7-reservation-inspection;positional-parent-depth=" + depth));
        }

        var requirements = rules.Select(item =>
        {
            grouped.TryGetValue(item.rule.Name, out var constraints);
            constraints ??= new List<ArchitectureV7ReservedDepthConstraint>();
            var ordered = constraints.OrderBy(constraint => constraint.PhysicalNodeId, StringComparer.Ordinal).ToArray();
            return new ArchitectureV7ReservedDepthRequirement(item.rule.Name, item.rule.Pattern, item.rule.Order,
                ordered.Length, ordered.Length == 0 ? 1 : ordered.Max(constraint => constraint.RequiredNodeRow), ordered);
        }).ToList();
        var externalConstraints = nodes.Where(node => node.IsExternal).Select(node =>
        {
            var depth = depths[node.PhysicalNodeId];
            return new ArchitectureV7ReservedDepthConstraint("External", node.PhysicalNodeId, depth,
                RequiredNodeRow(depth), true, "v7-reservation-inspection;external-depth=" + depth);
        }).OrderBy(constraint => constraint.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        var deepestNaturalNodeDepth = depths.Values.DefaultIfEmpty(0).Max();
        var deepestRequiredExternalRow = externalConstraints.Length == 0
            ? RequiredNodeRow(deepestNaturalNodeDepth + 1)
            : externalConstraints.Max(constraint => constraint.RequiredNodeRow);
        requirements.Add(new ArchitectureV7ReservedDepthRequirement("External", "<external>", int.MaxValue,
            externalConstraints.Length,
            deepestRequiredExternalRow,
            externalConstraints));

        var orderedRequirements = requirements.OrderBy(requirement => requirement.Order).ThenBy(requirement => requirement.ReservationName, StringComparer.Ordinal).ToArray();
        return new ArchitectureV7ReservationInspectionResult(ownership,
            nodes.ToDictionary(node => node.PhysicalNodeId, node => depths[node.PhysicalNodeId], StringComparer.Ordinal),
            orderedRequirements, diagnostics.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            Fingerprint(ownership.FreezeFingerprint, depths, orderedRequirements));
    }

    private static ArchitectureV7ReservedRoleRule? ResolveRole(
        ArchitectureV7PhysicalNode node,
        IReadOnlyList<(ArchitectureV7ReservedRoleRule rule, int index)> rules)
    {
        var names = new[]
        {
            node.Name,
            node.Name.Split(new[] { " : " }, StringSplitOptions.None)[0],
            node.FullName
        }.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var item in rules)
        {
            var suffix = item.rule.Pattern?.Trim() ?? string.Empty;
            if (suffix.StartsWith("*", StringComparison.Ordinal)) suffix = suffix.Substring(1);
            if (suffix.EndsWith("$", StringComparison.Ordinal)) suffix = suffix.Substring(0, suffix.Length - 1);
            if (suffix.Length > 0 && names.Any(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) return item.rule;
        }
        return null;
    }

    private static int RequiredNodeRow(int depth) => ArchitectureV7ReservationCoordinates.ReservedNodeRowFromSemanticDepth(depth);

    private static string Fingerprint(string ownershipFingerprint, IReadOnlyDictionary<string, int> depths,
        IReadOnlyList<ArchitectureV7ReservedDepthRequirement> requirements)
    {
        var text = ownershipFingerprint + "#" + string.Join("|", depths.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + ":" + item.Value)) + "#" +
            string.Join("|", requirements.Select(requirement => requirement.ReservationName + ":" + requirement.Pattern + ":" + requirement.Order + ":" + requirement.MatchCount + ":" + requirement.RequiredNodeRow + ":" +
                string.Join(",", requirement.Constraints.Select(constraint => constraint.PhysicalNodeId + ":" + constraint.NaturalDepth))));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }
}
