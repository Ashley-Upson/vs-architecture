using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ColumnDifferenceCycle(
    string FirstConstraintId,
    string SecondConstraintId,
    string FirstDestinationSubtreeId,
    string SecondDestinationSubtreeId);

internal static class ColumnDifferenceCycleAnalyzer
{
    public static IReadOnlyList<ColumnDifferenceCycle> FindMutualDestinationCycles(
        IEnumerable<ColumnToEnvelopeDifferenceConstraint> source)
    {
        var constraints = source.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var result = new List<ColumnDifferenceCycle>();
        for (var firstIndex = 0; firstIndex < constraints.Length; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < constraints.Length; secondIndex++)
        {
            var first = constraints[firstIndex];
            var second = constraints[secondIndex];
            if (first.DestinationSubtreeId != second.BlockingSubtreeId ||
                second.DestinationSubtreeId != first.BlockingSubtreeId) continue;
            result.Add(new ColumnDifferenceCycle(first.Id, second.Id,
                first.DestinationSubtreeId, second.DestinationSubtreeId));
        }
        return result;
    }
}
