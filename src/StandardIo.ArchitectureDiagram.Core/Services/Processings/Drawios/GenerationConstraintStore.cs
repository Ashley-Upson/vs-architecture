using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed class GenerationConstraintStore
{
    private readonly Dictionary<GenerationConstraintKey, GenerationConstraint> constraints = new();

    public bool Merge(GenerationConstraint proposal)
    {
        if (constraints.TryGetValue(proposal.Key, out var current))
        {
            if (proposal.Key.Kind == GenerationConstraintKind.MaximumX && current.Minimum <= proposal.Minimum)
                return false;
            if (proposal.Key.Kind != GenerationConstraintKind.MaximumX && current.Minimum >= proposal.Minimum)
                return false;
        }
        constraints[proposal.Key] = proposal;
        return true;
    }

    public int Minimum(GenerationConstraintKey key, int fallback = 0) =>
        constraints.TryGetValue(key, out var value) ? value.Minimum : fallback;

    public IReadOnlyList<GenerationConstraint> Snapshot() =>
        constraints.Values
            .OrderBy(value => value.Key.Scope.Kind)
            .ThenBy(value => value.Key.Scope.Id, StringComparer.Ordinal)
            .ThenBy(value => value.Key.Kind)
            .ThenBy(value => value.Reason, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyDictionary<MovementScopeIdentity, Rect> Materialize(
        IReadOnlyDictionary<MovementScopeIdentity, Rect> immutableBasePlacement)
    {
        var result = immutableBasePlacement.ToDictionary(item => item.Key, item => item.Value);
        foreach (var scope in immutableBasePlacement.Keys.OrderBy(key => key.Kind).ThenBy(key => key.Id, StringComparer.Ordinal))
        {
            var basis = immutableBasePlacement[scope];
            result[scope] = new Rect(
                MaterializeX(scope, basis),
                Math.Max(basis.Y, Minimum(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumY), basis.Y)),
                Math.Max(basis.Width, Minimum(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumWidth), basis.Width)),
                Math.Max(basis.Height, Minimum(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumHeight), basis.Height)));
        }
        return new ReadOnlyDictionary<MovementScopeIdentity, Rect>(result);
    }

    private int MaterializeX(MovementScopeIdentity scope, Rect basis)
    {
        var minimum = Minimum(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumX), basis.X);
        var maximum = constraints.TryGetValue(
            new GenerationConstraintKey(scope, GenerationConstraintKind.MaximumX), out var value)
            ? value.Minimum
            : basis.X;
        return minimum != basis.X ? Math.Max(basis.X, minimum) : Math.Min(basis.X, maximum);
    }
}
