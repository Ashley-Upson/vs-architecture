using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ConsolidatedRoutingFoundationTests
{
    [Fact]
    public void Absolute_coordinate_constraints_may_be_negative()
    {
        var store = new GenerationConstraintStore();
        var constraint = new GenerationConstraint(
            new GenerationConstraintKey(
                new MovementScopeIdentity(MovementScopeKind.ProjectRoot, "project"),
                GenerationConstraintKind.MaximumX),
            -120,
            "negative canvas coordinate");

        Assert.True(store.Merge(constraint));
        Assert.Equal(-120, Assert.Single(store.Snapshot()).Minimum);
    }

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
    public void Constraint_merge_retains_greatest_minimum_across_iterations()
    {
        var scope = new MovementScopeIdentity(MovementScopeKind.Node, "node");
        var key = new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumWidth);
        var store = new GenerationConstraintStore();

        Assert.True(store.Merge(new GenerationConstraint(key, 240, "iteration-1")));
        Assert.True(store.Merge(new GenerationConstraint(key, 280, "iteration-2")));
        Assert.False(store.Merge(new GenerationConstraint(key, 260, "iteration-3")));
        Assert.Equal(280, store.Minimum(key));
    }

    [Fact]
    public void Materialization_always_uses_immutable_base_placement()
    {
        var scope = new MovementScopeIdentity(MovementScopeKind.Node, "node");
        var basis = new Dictionary<MovementScopeIdentity, Rect> { [scope] = new Rect(10, 20, 100, 80) };
        var store = new GenerationConstraintStore();
        store.Merge(new GenerationConstraint(
            new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumX), 50, "first"));
        var first = store.Materialize(basis);
        store.Merge(new GenerationConstraint(
            new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumWidth), 140, "second"));
        var second = store.Materialize(basis);

        Assert.Equal(new Rect(50, 20, 100, 80), first[scope]);
        Assert.Equal(new Rect(50, 20, 140, 80), second[scope]);
        Assert.Equal(new Rect(10, 20, 100, 80), basis[scope]);
    }

    [Fact]
    public void Reversed_constraint_enumeration_is_deterministic()
    {
        var scope = new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "layer-2");
        var proposals = new[]
        {
            new GenerationConstraint(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumY), 300, "a"),
            new GenerationConstraint(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumY), 420, "b")
        };
        var forward = new GenerationConstraintStore();
        var reverse = new GenerationConstraintStore();
        foreach (var proposal in proposals) forward.Merge(proposal);
        foreach (var proposal in proposals.AsEnumerable().Reverse()) reverse.Merge(proposal);

        Assert.Equal(forward.Snapshot(), reverse.Snapshot());
    }

    [Fact]
    public void Node_resize_invalidates_every_incident_route_only()
    {
        var routes = new[]
        {
            new SemanticLinkReference("incoming", "other", "changed", new RouteRevision(1)),
            new SemanticLinkReference("outgoing", "changed", "linked", new RouteRevision(2)),
            new SemanticLinkReference("unrelated", "x", "y", new RouteRevision(3))
        };

        var invalidations = LinkInvalidationCalculator.ForChangedNodes(
            routes, new[] { "changed" }, LinkInvalidationCause.EndpointResized,
            new LayoutRevision(4), new LayoutRevision(5));

        Assert.Equal(new[] { "incoming", "outgoing" }, invalidations.Select(item => item.LogicalRouteId));
        Assert.All(invalidations, item => Assert.Equal(LinkInvalidationCause.EndpointResized, item.Cause));
        Assert.DoesNotContain(invalidations, item => item.LogicalRouteId == "unrelated");
    }

    [Fact]
    public void Node_width_constraint_does_not_move_linked_node()
    {
        var changed = new MovementScopeIdentity(MovementScopeKind.Node, "changed");
        var linked = new MovementScopeIdentity(MovementScopeKind.Node, "linked");
        var basis = new Dictionary<MovementScopeIdentity, Rect>
        {
            [changed] = new Rect(0, 0, 100, 80),
            [linked] = new Rect(200, 0, 100, 80)
        };
        var store = new GenerationConstraintStore();
        store.Merge(new GenerationConstraint(
            new GenerationConstraintKey(changed, GenerationConstraintKind.MinimumWidth), 160, "terminal demand"));

        var materialized = store.Materialize(basis);

        Assert.Equal(new Rect(0, 0, 160, 80), materialized[changed]);
        Assert.Equal(basis[linked], materialized[linked]);
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
