using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class PreAssignmentMovementPlannerTests
{
    [Fact]
    public void Destination_column_conflict_moves_the_smallest_coherent_scope()
    {
        var placement = Placement(withChild: false);
        var result = Solve(placement, Demand(placement, 50));

        var accepted = Assert.Single(Assert.Single(result.Solutions).AcceptedCandidates);
        Assert.Equal(MovementScopeKind.Node, accepted.Scope.Kind);
        Assert.Equal("right", accepted.Scope.Id);
        Assert.Equal(HorizontalMovementDirection.Right, accepted.Direction);
        Assert.Equal(50, accepted.Delta);
        Assert.Equal(90, result.Placement.Nodes["right"].Rect.X);
    }

    [Fact]
    public void Non_leaf_destination_moves_as_a_complete_subtree()
    {
        var placement = Placement(withChild: true);
        var result = Solve(placement, Demand(placement, 50));

        var accepted = Assert.Single(Assert.Single(result.Solutions).AcceptedCandidates);
        Assert.Equal(MovementScopeKind.LayoutSubtree, accepted.Scope.Kind);
        Assert.Equal(90, result.Placement.Nodes["right"].Rect.X);
        Assert.Equal(150, result.Placement.Nodes["right-child"].Rect.X);
    }

    [Fact]
    public void Candidate_scoring_is_deterministic_when_scope_enumeration_is_reversed()
    {
        var placement = Placement(withChild: true);
        var demand = Demand(placement, 50);
        var reversed = demand with { CandidateMovementScopes = demand.CandidateMovementScopes.Reverse().ToArray() };

        var forward = Solve(placement, demand);
        var backward = Solve(placement, reversed);

        var forwardCandidate = Assert.Single(Assert.Single(forward.Solutions).AcceptedCandidates);
        var backwardCandidate = Assert.Single(Assert.Single(backward.Solutions).AcceptedCandidates);
        Assert.Equal(forwardCandidate.Scope, backwardCandidate.Scope);
        Assert.Equal(forwardCandidate.Direction, backwardCandidate.Direction);
        Assert.Equal(forwardCandidate.Delta, backwardCandidate.Delta);
        Assert.Equal(forward.Placement.Nodes.Select(item => item.Value.Rect),
            backward.Placement.Nodes.Select(item => item.Value.Rect));
    }

    [Fact]
    public void Overlapping_constraints_form_one_atomic_component()
    {
        var placement = Placement(withChild: false);
        var first = Demand(placement, 50);
        var second = first with
        {
            Id = "second", Reason = PositionalConstraintReason.VerticalColumnClearance,
            SourceLinkIds = new[] { "left-right" }
        };

        var result = PreAssignmentMovementPlanner.Solve(placement, new[] { first, second }, Settings(), Routes(placement));

        Assert.Single(result.Components);
        Assert.True(Assert.Single(result.Solutions).IsValid);
    }

    private static PreAssignmentMovementResult Solve(PlacedGraph placement, PositionalConstraintDemand demand) =>
        PreAssignmentMovementPlanner.Solve(placement, new[] { demand }, Settings(), Routes(placement));

    private static PositionalConstraintDemand Demand(PlacedGraph placement, int separation) => new(
        "column-conflict", PositionalConstraintReason.DestinationColumnSeparation,
        HorizontalMovementDirection.Right, separation, new AxisInterval(0, 2), "left", "right",
        PreAssignmentMovementPlanner.CandidateScopes("left", placement, HorizontalMovementDirection.Left)
            .Concat(PreAssignmentMovementPlanner.CandidateScopes("right", placement, HorizontalMovementDirection.Right)).ToArray(),
        new[] { "left-right" }, placement.Revision, new RouteRevision(0));

    private static PlacedGraph Placement(bool withChild)
    {
        var ids = withChild ? new[] { "root", "left", "right", "right-child" } : new[] { "root", "left", "right" };
        var edges = new List<DependencyEdge> { Edge("root-left", "root", "left"), Edge("left-right", "left", "right") };
        if (withChild) edges.Add(Edge("right-child-edge", "right", "right-child"));
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", ids.Select(Type).ToArray()) },
            Array.Empty<ExternalDependencyNode>(), edges));
        var renderNodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var layouts = new Dictionary<string, NodeLayout>
        {
            ["root"] = Layout(renderNodes["root"], 40, 0, 0),
            ["left"] = Layout(renderNodes["left"], 0, 100, 1),
            ["right"] = Layout(renderNodes["right"], 40, 100, 1)
        };
        if (withChild) layouts["right-child"] = Layout(renderNodes["right-child"], 100, 200, 2);
        return new PlacedGraph(graph, layouts, new Dictionary<string, ProjectLayout>(), new LayoutRevision(0));
    }

    private static IReadOnlyDictionary<string, LinkLayout> Routes(PlacedGraph placement) =>
        placement.Graph.Links.ToDictionary(link => link.Id, link => new LinkLayout(link,
            new Point(placement.Nodes[link.SourceId].Rect.X, placement.Nodes[link.SourceId].Rect.Bottom),
            new Point(placement.Nodes[link.TargetId].Rect.X, placement.Nodes[link.TargetId].Rect.Y),
            Array.Empty<Point>(), .5, .5), StringComparer.Ordinal);

    private static DiagramSettings Settings()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.ShowProjectContainers = false;
        return settings;
    }

    private static TypeNode Type(string id) => new(id, "project", id, id, "Class");
    private static DependencyEdge Edge(string id, string source, string target) => new(id, source, target, "Dependency");
    private static NodeLayout Layout(RenderNode node, int x, int y, int depth) => new(node, new Rect(x, y, 40, 40), depth, false);
}
