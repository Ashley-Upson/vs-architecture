using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed class MonotonicSpacingConstraintStore
{
    private readonly Dictionary<SpacingConstraintKey, MinimumSpacingConstraint> _constraints = new();

    public bool Merge(MinimumSpacingConstraint proposal)
    {
        if (_constraints.TryGetValue(proposal.Key, out var current) && current.Minimum >= proposal.Minimum)
            return false;
        _constraints[proposal.Key] = proposal;
        return true;
    }

    public IReadOnlyList<MinimumSpacingConstraint> Snapshot() => _constraints.Values
        .OrderBy(item => item.Key.Y).ThenBy(item => item.Key.X).ThenBy(item => item.Key.Scope)
        .ThenBy(item => item.Key.StableIdentity, StringComparer.Ordinal).ToArray();

    public int Minimum(SpacingConstraintKey key) =>
        _constraints.TryGetValue(key, out var constraint) ? constraint.Minimum : 0;
}
