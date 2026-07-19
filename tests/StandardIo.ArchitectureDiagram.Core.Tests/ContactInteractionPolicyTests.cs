using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ContactInteractionPolicyTests
{
    [Theory]
    [InlineData((int)CanonicalContactKind.CleanPerpendicularCrossover, false)]
    [InlineData((int)CanonicalContactKind.BendInvolvedPerpendicularContact, true)]
    [InlineData((int)CanonicalContactKind.EndpointToInterior, true)]
    [InlineData((int)CanonicalContactKind.PositiveCollinearOverlap, true)]
    [InlineData((int)CanonicalContactKind.NearParallelSpacingConflict, true)]
    [InlineData((int)CanonicalContactKind.IntentionalSemanticJunction, false)]
    public void Final_geometry_policy_is_explicit(int kind, bool expected) =>
        Assert.Equal(expected, ContactInteractionPolicy.CreatesFinalGeometryEdge((CanonicalContactKind)kind));

    [Fact]
    public void Unassigned_competing_demands_create_an_edge()
    {
        var first = Demand("first", 0, 100, 20, 60);
        var second = Demand("second", 40, 120, 40, 80);

        Assert.True(ContactInteractionPolicy.CreatesUnassignedRailEdge(first, second));
    }

    [Fact]
    public void Independent_assigned_rails_with_overlapping_projections_do_not_create_an_edge()
    {
        var first = Assigned("first", 20, 0, 100);
        var second = Assigned("second", 50, 20, 80);

        Assert.False(ContactInteractionPolicy.CreatesAssignedLinkSegmentEdge(first, second, 12));
    }

    [Fact]
    public void Validator_and_component_policy_consume_the_same_clean_crossover_fact()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["horizontal"] = Link("horizontal", 0, new Point(0, 50), new Point(100, 50)),
            ["vertical"] = Link("vertical", 1, new Point(40, 0), new Point(40, 100))
        };
        var facts = CanonicalRouteContactDiscovery.Discover(links["horizontal"], links["vertical"], 12);

        var fact = Assert.Single(facts);
        Assert.Equal(CanonicalContactKind.CleanPerpendicularCrossover, fact.Contact.Kind);
        Assert.False(ContactInteractionPolicy.CreatesFinalGeometryEdge(fact.Contact.Kind));
        Assert.DoesNotContain(
            TraceabilityValidator.Validate(new Dictionary<string, NodeLayout>(), links, 12).Violations,
            item => item.Code == TraceabilityViolationCode.PerpendicularCrossing);
    }

    [Fact]
    public void Reversed_route_enumeration_produces_identical_findings()
    {
        var forward = new Dictionary<string, LinkLayout>
        {
            ["a"] = Link("a", 0, new Point(0, 50), new Point(40, 80), new Point(40, 50)),
            ["b"] = Link("b", 1, new Point(40, 0), new Point(40, 100))
        };
        var reverse = forward.Reverse().ToDictionary(item => item.Key, item => item.Value);

        var first = TraceabilityValidator.Validate(new Dictionary<string, NodeLayout>(), forward, 12).Violations;
        var second = TraceabilityValidator.Validate(new Dictionary<string, NodeLayout>(), reverse, 12).Violations;

        Assert.Equal(
            first.Select(item => (item.Code, item.EdgeId, item.OtherEdgeId, item.Magnitude, item.Description)),
            second.Select(item => (item.Code, item.EdgeId, item.OtherEdgeId, item.Magnitude, item.Description)));
    }

    private static LinkSegmentDemand Demand(string id, int start, int end, int allowedStart, int allowedEnd) => new(
        id, id, LinkSegmentOrientation.Horizontal, new AxisInterval(start, end), new AxisInterval(allowedStart, allowedEnd),
        null, LinkSegmentRole.Through, null, null, null, new LayoutRevision(1), new RouteRevision(1));

    private static AssignedLinkSegment Assigned(string id, int axis, int start, int end) => new(
        id, id, id, LinkSegmentOrientation.Horizontal, axis, 0, new AxisInterval(start, end), LinkSegmentRole.Through,
        new LayoutRevision(1), new RouteRevision(1));

    private static LinkLayout Link(string id, int order, Point source, Point target, params Point[] points) => new(
        new RenderLink(id, $"{id}_source", $"{id}_target", "internal", order), source, target, points, 0.5, 0.5);
}
