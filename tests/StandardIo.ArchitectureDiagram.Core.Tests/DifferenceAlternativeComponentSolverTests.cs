using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DifferenceAlternativeComponentSolverTests
{
    [Fact]
    public void Three_way_positive_cycle_selects_the_larger_acyclic_alternative()
    {
        var result = DifferenceAlternativeComponentSolver.Solve(new[]
        {
            Choice("ab", "ab-right", "a", "b", HorizontalMovementDirection.Right, 10, 1),
            Choice("ab", "ab-left", "a", "b", HorizontalMovementDirection.Left, 10, 20),
            Choice("bc", "bc-right", "b", "c", HorizontalMovementDirection.Right, 10, 1),
            Choice("bc", "bc-left", "b", "c", HorizontalMovementDirection.Left, 10, 20),
            Choice("ca", "ca-right", "c", "a", HorizontalMovementDirection.Right, 10, 1),
            Choice("ca", "ca-left", "c", "a", HorizontalMovementDirection.Left, 10, 20)
        });

        Assert.True(result.IsSatisfiable);
        Assert.Empty(DifferenceAlternativeComponentSolver.FindPositiveCycles(result.Selected));
        Assert.True(result.StatesEvaluated > 1);
        Assert.Contains(result.Selected, item => item.AlternativeId == "ab-left");
    }

    [Fact]
    public void Positive_cycle_is_detected_without_coordinate_iteration()
    {
        var cycle = Assert.Single(DifferenceAlternativeComponentSolver.FindPositiveCycles(new[]
        {
            Choice("ab", "ab", "a", "b", HorizontalMovementDirection.Right, 7, 1),
            Choice("bc", "bc", "b", "c", HorizontalMovementDirection.Right, 9, 1),
            Choice("ca", "ca", "c", "a", HorizontalMovementDirection.Right, 11, 1)
        }));

        Assert.Equal(27, cycle.TotalWeight);
        Assert.Equal(new[] { "a", "b", "c" }, cycle.ScopeIds);
    }

    [Fact]
    public void Zero_weight_cycle_is_accepted()
    {
        var choices = new[]
        {
            Choice("ab", "ab", "a", "b", HorizontalMovementDirection.Right, 0, 0),
            Choice("ba", "ba", "b", "a", HorizontalMovementDirection.Right, 0, 0)
        };

        Assert.Empty(DifferenceAlternativeComponentSolver.FindPositiveCycles(choices));
        Assert.True(DifferenceAlternativeComponentSolver.Solve(choices).IsSatisfiable);
    }

    [Fact]
    public void Independent_acyclic_choice_is_not_enumerated_when_breaking_cycle()
    {
        var result = DifferenceAlternativeComponentSolver.Solve(new[]
        {
            Choice("ab", "ab-right", "a", "b", HorizontalMovementDirection.Right, 1, 1),
            Choice("ab", "ab-left", "a", "b", HorizontalMovementDirection.Left, 1, 2),
            Choice("ba", "ba-right", "b", "a", HorizontalMovementDirection.Right, 1, 1),
            Choice("ba", "ba-left", "b", "a", HorizontalMovementDirection.Left, 1, 2),
            Choice("xy", "xy-right", "x", "y", HorizontalMovementDirection.Right, 1, 1),
            Choice("xy", "xy-left", "x", "y", HorizontalMovementDirection.Left, 1, 100)
        });

        Assert.True(result.IsSatisfiable);
        Assert.Contains(result.Selected, item => item.AlternativeId == "xy-right");
        Assert.DoesNotContain(result.Selected, item => item.AlternativeId == "xy-left");
    }

    [Fact]
    public void Reversed_enumeration_selects_identical_alternatives()
    {
        var choices = new[]
        {
            Choice("ab", "ab-right", "a", "b", HorizontalMovementDirection.Right, 1, 1),
            Choice("ab", "ab-left", "a", "b", HorizontalMovementDirection.Left, 1, 2),
            Choice("ba", "ba-right", "b", "a", HorizontalMovementDirection.Right, 1, 1),
            Choice("ba", "ba-left", "b", "a", HorizontalMovementDirection.Left, 1, 2)
        };

        var forward = DifferenceAlternativeComponentSolver.Solve(choices).Selected.Select(item => item.AlternativeId);
        var reverse = DifferenceAlternativeComponentSolver.Solve(choices.AsEnumerable().Reverse()).Selected
            .Select(item => item.AlternativeId);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void Every_hierarchy_preserving_selection_cyclic_is_unsatisfiable()
    {
        var result = DifferenceAlternativeComponentSolver.Solve(new[]
        {
            Choice("ab", "ab-only", "a", "b", HorizontalMovementDirection.Right, 3, 1),
            Choice("bc", "bc-only", "b", "c", HorizontalMovementDirection.Right, 5, 1),
            Choice("ca", "ca-only", "c", "a", HorizontalMovementDirection.Right, 7, 1)
        });

        Assert.False(result.IsSatisfiable);
        Assert.Equal(15, Assert.Single(result.PositiveCycles).TotalWeight);
        Assert.Equal(0, result.CompleteSolutions);
    }

    [Theory]
    [InlineData((int)MovementScopeKind.OrderedSiblingPrefix)]
    [InlineData((int)MovementScopeKind.OrderedSiblingSuffix)]
    [InlineData((int)MovementScopeKind.OrderedProjectPrefix)]
    [InlineData((int)MovementScopeKind.OrderedProjectSuffix)]
    public void Coherent_hierarchy_scope_can_break_three_way_cycle(int scopeKindValue)
    {
        var scopeKind = (MovementScopeKind)scopeKindValue;
        var alternativeScope = new MovementScopeIdentity(scopeKind, "a");
        var alternative = Choice("ab", "ab-outward", "a", "b", HorizontalMovementDirection.Left, 4, 10) with
        {
            MovingScope = alternativeScope,
            Constraint = new GenerationConstraint(new GenerationConstraintKey(alternativeScope,
                GenerationConstraintKind.MaximumX), 10, "outward")
        };
        var result = DifferenceAlternativeComponentSolver.Solve(new[]
        {
            Choice("ab", "ab-local", "a", "b", HorizontalMovementDirection.Right, 4, 1), alternative,
            Choice("bc", "bc", "b", "c", HorizontalMovementDirection.Right, 4, 1),
            Choice("ca", "ca", "c", "a", HorizontalMovementDirection.Right, 4, 1)
        });

        Assert.True(result.IsSatisfiable);
        Assert.Contains(result.Selected, item => item.MovingScope.Kind == scopeKind);
    }

    private static DifferenceAlternativeChoice Choice(string conflict, string id, string moving, string opposing,
        HorizontalMovementDirection direction, int weight, int movement)
    {
        var scope = new MovementScopeIdentity(MovementScopeKind.LayoutSubtree, moving);
        return new DifferenceAlternativeChoice(conflict, id, scope, moving, opposing, direction, weight, movement,
            1, movement, new GenerationConstraint(new GenerationConstraintKey(scope,
                direction == HorizontalMovementDirection.Left
                    ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX), movement, id));
    }
}
