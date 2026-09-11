using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7TraversalPolicyTests
{
    [Theory]
    [InlineData(ArchitectureV7CellCapability.GeneralRouting, ArchitectureV7TraversalDirection.Left, ArchitectureV7TraversalDirection.Right)]
    [InlineData(ArchitectureV7CellCapability.GeneralRouting, ArchitectureV7TraversalDirection.Up, ArchitectureV7TraversalDirection.Down)]
    [InlineData(ArchitectureV7CellCapability.GeneralRouting, ArchitectureV7TraversalDirection.Down, ArchitectureV7TraversalDirection.Left)]
    [InlineData(ArchitectureV7CellCapability.RoutingAllowed, ArchitectureV7TraversalDirection.Up, ArchitectureV7TraversalDirection.Up)]
    [InlineData(ArchitectureV7CellCapability.NodeAllowed, ArchitectureV7TraversalDirection.Up, ArchitectureV7TraversalDirection.Up)]
    [InlineData(ArchitectureV7CellCapability.StraightPassthroughOnly, ArchitectureV7TraversalDirection.Down, ArchitectureV7TraversalDirection.Down)]
    public void Capability_allows_only_its_documented_traversal(ArchitectureV7CellCapability capability, ArchitectureV7TraversalDirection entry, ArchitectureV7TraversalDirection exit)
    {
        Assert.True(ArchitectureV7CellTraversalPolicy.Allows(capability, entry, exit));
    }

    [Theory]
    [InlineData(ArchitectureV7CellCapability.RoutingAllowed, ArchitectureV7TraversalDirection.Left, ArchitectureV7TraversalDirection.Up)]
    [InlineData(ArchitectureV7CellCapability.NodeAllowed, ArchitectureV7TraversalDirection.Left, ArchitectureV7TraversalDirection.Right)]
    [InlineData(ArchitectureV7CellCapability.StraightPassthroughOnly, ArchitectureV7TraversalDirection.Up, ArchitectureV7TraversalDirection.Down)]
    [InlineData(ArchitectureV7CellCapability.GeneralRouting, ArchitectureV7TraversalDirection.None, ArchitectureV7TraversalDirection.Down)]
    [InlineData(ArchitectureV7CellCapability.HeaderBlocked | ArchitectureV7CellCapability.GeneralRouting, ArchitectureV7TraversalDirection.Left, ArchitectureV7TraversalDirection.Right)]
    [InlineData(ArchitectureV7CellCapability.NonRoutingSeparator | ArchitectureV7CellCapability.GeneralRouting, ArchitectureV7TraversalDirection.Left, ArchitectureV7TraversalDirection.Right)]
    [InlineData(ArchitectureV7CellCapability.Blocked | ArchitectureV7CellCapability.StraightPassthroughOnly, ArchitectureV7TraversalDirection.Up, ArchitectureV7TraversalDirection.Up)]
    public void Capability_rejects_undocumented_or_blocked_traversal(ArchitectureV7CellCapability capability, ArchitectureV7TraversalDirection entry, ArchitectureV7TraversalDirection exit)
    {
        Assert.False(ArchitectureV7CellTraversalPolicy.Allows(capability, entry, exit));
    }
}
