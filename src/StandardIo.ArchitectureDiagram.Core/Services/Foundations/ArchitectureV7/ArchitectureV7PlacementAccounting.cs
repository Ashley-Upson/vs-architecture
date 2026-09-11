using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7PlacementAccountingException : InvalidOperationException
{
    public ArchitectureV7PlacementAccountingException(string message) : base(message) { }
}

public static class ArchitectureV7PlacementAccounting
{
    public static void Validate(
        ArchitectureV7PhysicalProjectionResult projection,
        IReadOnlyList<ArchitectureV7FrozenNodePlacement> placements,
        ArchitectureV7ExternalRegion external,
        ArchitectureV7StandaloneRegion standalone)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));
        if (placements is null) throw new ArgumentNullException(nameof(placements));
        if (external is null) throw new ArgumentNullException(nameof(external));
        if (standalone is null) throw new ArgumentNullException(nameof(standalone));

        var projected = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var byId = placements.GroupBy(node => node.PhysicalNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var group in byId.Values.Where(group => group.Length > 1))
            errors.Add("duplicate physical node ID " + group[0].PhysicalNodeId + ": " + Describe(group));

        foreach (var node in placements.Where(node => !projected.ContainsKey(node.PhysicalNodeId)))
            errors.Add("unprojected physical node ID " + node.PhysicalNodeId + ": " + Describe(node));

        foreach (var node in projection.PhysicalNodes.Where(node => !byId.ContainsKey(node.PhysicalNodeId)))
            errors.Add("missing placement for projected physical node ID " + node.PhysicalNodeId +
                " (semantic=" + node.SemanticNodeId + ", project=" + (node.ProjectId ?? "<none>") + ")");

        var externalIds = new HashSet<string>(external.Placements.Select(node => node.PhysicalNodeId), StringComparer.Ordinal);
        var standaloneIds = new HashSet<string>(standalone.Placements.Select(node => node.PhysicalNodeId), StringComparer.Ordinal);
        foreach (var node in external.Placements.Where(node => node.DiagramRow != external.NodeRow))
            errors.Add("External node is not on the frozen External row: " + Describe(node));
        foreach (var node in placements.Where(node => node.DiagramRow == external.NodeRow && !node.IsExternal))
            errors.Add("non-External node occupies the frozen External row: " + Describe(node));
        if (external.Placements.Any(node => node.DiagramRow == external.NodeRow) == false && external.Placements.Count > 0)
            errors.Add("External region has no placement on its frozen External row.");
        foreach (var node in placements)
        {
            var memberships = (node.IsExternal ? 1 : 0) + (node.IsStandalone ? 1 : 0) +
                (node.IsDetached ? 1 : 0);
            if (node.IsExternal != externalIds.Contains(node.PhysicalNodeId))
                errors.Add("External membership mismatch: " + Describe(node));
            if (node.IsStandalone != standaloneIds.Contains(node.PhysicalNodeId))
                errors.Add("standalone membership mismatch: " + Describe(node));
            if (memberships > 1)
                errors.Add("mutually exclusive placement roles violated: " + Describe(node));
        }

        if (errors.Count > 0)
            throw new ArchitectureV7PlacementAccountingException(
                "V7 placement accounting failed before routing: " + string.Join("; ", errors));
    }

    private static string Describe(IEnumerable<ArchitectureV7FrozenNodePlacement> nodes) =>
        string.Join(" | ", nodes.Select(Describe));

    private static string Describe(ArchitectureV7FrozenNodePlacement node) =>
        "physical=" + node.PhysicalNodeId +
        ", semantic=" + node.SemanticNodeId +
        ", project=" + (node.ProjectId ?? "<none>") +
        ", tree=" + node.TreeId +
        ", detached=" + node.IsDetached +
        ", row=" + node.DiagramRow +
        ", column=" + node.DiagramColumn +
        ", span=" + node.LogicalSpan +
        ", centre=" + node.CentreCell +
        ", provenance=" + node.Provenance;
}
