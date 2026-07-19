using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ConsolidatedRoutingFoundationTests
{
    [Fact]
    public void Independent_terminal_sides_do_not_create_one_component()
    {
        var claims = new[]
        {
            new LinkConnectionClaim("incoming", "node", LinkConnectionSide.IncomingTop, "top"),
            new LinkConnectionClaim("outgoing", "node", LinkConnectionSide.OutgoingBottom, "bottom")
        };

        var components = ConflictComponentBuilder.Build(
            new[] { "incoming", "outgoing" }, id => id, LinkConnectionInteractions.BeforeAllocation(claims));

        Assert.Equal(2, components.Count);
    }

    [Fact]
    public void Same_side_terminal_demand_is_temporarily_coupled_then_separates_after_allocation()
    {
        var unresolved = new[]
        {
            new LinkConnectionClaim("a", "node", LinkConnectionSide.OutgoingBottom, "bottom"),
            new LinkConnectionClaim("b", "node", LinkConnectionSide.OutgoingBottom, "bottom")
        };
        var resolved = new[]
        {
            unresolved[0] with { AssignedAxisCoordinate = 40 },
            unresolved[1] with { AssignedAxisCoordinate = 64 }
        };

        Assert.Single(ConflictComponentBuilder.Build(
            new[] { "a", "b" }, id => id, LinkConnectionInteractions.BeforeAllocation(unresolved)));
        Assert.Equal(2, ConflictComponentBuilder.Build(
            new[] { "a", "b" }, id => id, LinkConnectionInteractions.AfterAllocation(resolved)).Count);
    }

    [Fact]
    public void Invalid_topology_maps_to_regeneration_not_spacing()
    {
        foreach (var defect in new[]
                 { HardGeometryDefectKind.ImmediateReversal, HardGeometryDefectKind.NonOrthogonalSegment })
        {
            var contract = DefectDemandContracts.For(defect);
            Assert.Equal(DefectResolutionKind.RejectTopologyAndRegenerate, contract.Resolution);
            Assert.False(contract.IsSpacingDemand);
            Assert.Empty(contract.LinkSegmentRoles);
        }
    }
}
