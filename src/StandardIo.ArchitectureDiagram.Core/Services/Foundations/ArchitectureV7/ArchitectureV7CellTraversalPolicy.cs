using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public enum ArchitectureV7TraversalDirection { None, Up, Down, Left, Right }

/// <summary>Neutral frozen-cell legality policy shared by routing and acceptance.</summary>
public static class ArchitectureV7CellTraversalPolicy
{
    public static bool Allows(ArchitectureV7CellCapability capability, ArchitectureV7TraversalDirection entry, ArchitectureV7TraversalDirection exit)
    {
        if (capability.HasFlag(ArchitectureV7CellCapability.NonRoutingSeparator) || capability.HasFlag(ArchitectureV7CellCapability.Blocked)) return false;
        if (capability.HasFlag(ArchitectureV7CellCapability.HeaderBlocked)) return false;
        if (capability.HasFlag(ArchitectureV7CellCapability.StraightPassthroughOnly)) return entry == exit && entry != ArchitectureV7TraversalDirection.None;
        if (capability.HasFlag(ArchitectureV7CellCapability.GeneralRouting)) return entry != ArchitectureV7TraversalDirection.None && exit != ArchitectureV7TraversalDirection.None;
        if (capability.HasFlag(ArchitectureV7CellCapability.RoutingAllowed)) return entry == exit && entry != ArchitectureV7TraversalDirection.None;
        if (capability.HasFlag(ArchitectureV7CellCapability.NodeAllowed)) return entry == exit && (entry == ArchitectureV7TraversalDirection.Up || entry == ArchitectureV7TraversalDirection.Down);
        return false;
    }
}
