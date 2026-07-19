using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class VerticalLinkColumnAllocator
{
    public static VerticalLinkColumnAssignment Assign(
        IEnumerable<VerticalLinkColumnDemand> source,
        int separation)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (separation < 0) throw new ArgumentOutOfRangeException(nameof(separation));
        var demands = source.OrderBy(item => item.SourceLayer).ThenBy(item => item.DestinationLayer)
            .ThenBy(item => item.PreferredX).ThenBy(item => item.LinkId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var assigned = new List<AssignedVerticalLinkColumn>();
        var comparisons = 0L;
        var components = 0;
        foreach (var demand in demands)
        {
            var conflicting = assigned.Where(column =>
            {
                comparisons++;
                return PositiveOverlap(column.VerticalInterval, demand.VerticalInterval);
            }).ToArray();
            if (conflicting.Length > 0) components++;
            var candidates = Coordinates(demand, separation);
            var selected = candidates.Cast<int?>().FirstOrDefault(x =>
                conflicting.All(column => Math.Abs(column.X - x!.Value) >= separation));
            if (selected is null)
                throw new InvalidOperationException($"No valid vertical link column is available for demand {demand.Id}.");
            assigned.Add(new AssignedVerticalLinkColumn(
                demand.Id, demand.LinkId, selected.Value, demand.SourceLayer, demand.DestinationLayer,
                demand.VerticalInterval, ColumnIndex(selected.Value, demand.PreferredX, separation),
                demand.PlacementRevision, demand.LinkRevision));
        }
        return new VerticalLinkColumnAssignment(
            assigned.ToDictionary(item => item.DemandId, StringComparer.Ordinal),
            components,
            comparisons);
    }

    private static IEnumerable<int> Coordinates(VerticalLinkColumnDemand demand, int separation)
    {
        yield return demand.PreferredX;
        if (separation == 0) yield break;
        var maximumSteps = Math.Max(
            Math.Abs(demand.PreferredX - demand.AllowedXInterval.Minimum),
            Math.Abs(demand.AllowedXInterval.Maximum - demand.PreferredX)) / separation + 1;
        for (var step = 1; step <= maximumSteps; step++)
        {
            var left = demand.PreferredX - step * separation;
            var right = demand.PreferredX + step * separation;
            if (left >= demand.AllowedXInterval.Minimum) yield return left;
            if (right <= demand.AllowedXInterval.Maximum) yield return right;
        }
    }

    private static int ColumnIndex(int x, int preferredX, int separation) =>
        separation == 0 ? 0 : (x - preferredX) / separation;

    private static bool PositiveOverlap(AxisInterval left, AxisInterval right) =>
        Math.Min(left.Maximum, right.Maximum) > Math.Max(left.Minimum, right.Minimum);
}
