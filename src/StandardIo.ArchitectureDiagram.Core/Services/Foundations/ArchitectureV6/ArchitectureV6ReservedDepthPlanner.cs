using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6ReservedDepthPlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly ArchitectureProjectionResult projection;
    private readonly IReadOnlyDictionary<string, int> order;

    public ArchitectureV6ReservedDepthPlanner(ArchitecturePlanningRequest request, ArchitectureProjectionResult projection)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        order = projection.PhysicalNodes.Select((node, index) => (node.PhysicalNodeId, index))
            .ToDictionary(item => item.PhysicalNodeId, item => item.index, StringComparer.Ordinal);
    }

    public IReadOnlyList<ArchitectureV6ReservedDepthRequirement> Inspect()
    {
        var nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var parents = projection.PhysicalLinks
            .GroupBy(link => link.DestinationPhysicalNodeId)
            .ToDictionary(group => group.Key, group => group
                .Where(link => nodes.ContainsKey(link.SourcePhysicalNodeId))
                .Select(link => link.SourcePhysicalNodeId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => order[id])
                .ToArray(), StringComparer.Ordinal);
        var owner = nodes.Values.ToDictionary(node => node.PhysicalNodeId, node =>
            node.PositionalOwnerId is not null && nodes.ContainsKey(node.PositionalOwnerId)
                ? node.PositionalOwnerId
                : parents.TryGetValue(node.PhysicalNodeId, out var candidates)
                    ? candidates.FirstOrDefault(parent => nodes[parent].ProjectId == node.ProjectId)
                    : null, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        int Depth(string id, HashSet<string> visiting)
        {
            if (depths.TryGetValue(id, out var known)) return known;
            if (!visiting.Add(id)) return 0;
            var parent = owner[id];
            var depth = parent is null ? 0 : Depth(parent, visiting) + 1;
            visiting.Remove(id);
            depths[id] = depth;
            return depth;
        }

        foreach (var node in projection.PhysicalNodes.OrderBy(node => order[node.PhysicalNodeId]))
            Depth(node.PhysicalNodeId, new HashSet<string>(StringComparer.Ordinal));

        var rules = (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
            .OrderBy(rule => rule.Order).ThenBy(rule => rule.Name, StringComparer.Ordinal).ToArray();
        var constraints = rules.ToDictionary(rule => rule.Name, _ => new List<ArchitectureV6ReservedDepthConstraint>(), StringComparer.Ordinal);
        foreach (var node in projection.PhysicalNodes.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var reservationName = node.IsExternal
                ? "External"
                : ArchitectureV6RoleResolver.Resolve(node.SemanticName, rules);
            if (!node.IsExternal && reservationName == "Unmatched") continue;
            var requiredRow = RequiredNodeRow(depths[node.PhysicalNodeId]);
            if (!constraints.TryGetValue(reservationName, out var list))
            {
                list = new List<ArchitectureV6ReservedDepthConstraint>();
                constraints[reservationName] = list;
            }
            list.Add(new ArchitectureV6ReservedDepthConstraint(reservationName, node.PhysicalNodeId,
                depths[node.PhysicalNodeId], requiredRow, node.IsExternal,
                $"natural-depth:{depths[node.PhysicalNodeId]};positional-owner:{owner[node.PhysicalNodeId] ?? "root"}"));
        }

        return rules.Select(rule => new ArchitectureV6ReservedDepthRequirement(rule.Name,
                constraints.TryGetValue(rule.Name, out var matches) ? matches.Count : 0,
                constraints.TryGetValue(rule.Name, out matches) && matches.Count > 0 ? matches.Max(item => item.RequiredNodeRow) : 1,
                Array.AsReadOnly((constraints.TryGetValue(rule.Name, out matches) ? matches : new List<ArchitectureV6ReservedDepthConstraint>()).ToArray())))
            .Where(item => item.MatchCount > 0)
            .Concat(new[]
            {
                new ArchitectureV6ReservedDepthRequirement("External", constraints.TryGetValue("External", out var external) ? external.Count : 0,
                    constraints.TryGetValue("External", out external) && external.Count > 0 ? external.Max(item => item.RequiredNodeRow) : 1,
                    Array.AsReadOnly((constraints.TryGetValue("External", out external) ? external : new List<ArchitectureV6ReservedDepthConstraint>()).ToArray()))
            })
            .ToArray();
    }

    public ArchitectureV6ReservedDepthTable BuildFrozenTable(IReadOnlyList<ArchitectureV6ReservedDepthRequirement> requirements)
    {
        var rules = (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
            .OrderBy(rule => rule.Order).ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .Where(rule => requirements.Any(item => item.ReservationName == rule.Name))
            .ToArray();
        var byName = requirements.ToDictionary(item => item.ReservationName, StringComparer.Ordinal);
        var result = new List<ArchitectureV6FrozenReservation>();
        var nextRow = 1;
        foreach (var rule in rules)
        {
            var requirement = byName[rule.Name];
            nextRow = Math.Max(nextRow, OddAtOrAbove(requirement.RequiredNodeRow));
            result.Add(new ArchitectureV6FrozenReservation(rule.Name, rule.Pattern, rule.Order,
                requirement.MatchCount, nextRow, false));
            nextRow += 2;
        }

        var external = byName["External"];
        nextRow = Math.Max(nextRow, OddAtOrAbove(external.RequiredNodeRow));
        result.Add(new ArchitectureV6FrozenReservation("External", "<external>", int.MaxValue,
            external.MatchCount, nextRow, true));
        return new ArchitectureV6ReservedDepthTable(result);
    }

    private static int RequiredNodeRow(int naturalDepth) => Math.Max(1, naturalDepth * 2 + 1);
    private static int OddAtOrAbove(int value)
    {
        var row = Math.Max(1, value);
        return row % 2 == 0 ? row + 1 : row;
    }
}
