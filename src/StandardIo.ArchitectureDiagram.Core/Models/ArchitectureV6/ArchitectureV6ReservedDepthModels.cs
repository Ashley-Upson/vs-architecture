using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public sealed record ArchitectureV6ReservedDepthConstraint(
    string ReservationName,
    string PhysicalNodeId,
    int NaturalDepth,
    int RequiredNodeRow,
    bool IsExternal,
    string Provenance);

public sealed class ArchitectureV6ReservedDepthRequirement
{
    public ArchitectureV6ReservedDepthRequirement(
        string reservationName,
        int matchCount,
        int requiredNodeRow,
        IReadOnlyList<ArchitectureV6ReservedDepthConstraint> constraints)
    {
        ReservationName = reservationName ?? throw new ArgumentNullException(nameof(reservationName));
        MatchCount = matchCount;
        RequiredNodeRow = requiredNodeRow;
        Constraints = Array.AsReadOnly((constraints ?? throw new ArgumentNullException(nameof(constraints))).ToArray());
    }

    public string ReservationName { get; }
    public int MatchCount { get; }
    public int RequiredNodeRow { get; }
    public IReadOnlyList<ArchitectureV6ReservedDepthConstraint> Constraints { get; }
}

public sealed record ArchitectureV6FrozenReservation(
    string Name,
    string Pattern,
    int Order,
    int MatchCount,
    int NodeRow,
    bool IsExternal);

public sealed class ArchitectureV6ReservedDepthTable
{
    public ArchitectureV6ReservedDepthTable(IEnumerable<ArchitectureV6FrozenReservation> reservations)
    {
        var values = (reservations ?? throw new ArgumentNullException(nameof(reservations))).ToArray();
        if (values.Length == 0) throw new ArgumentException("At least one reservation is required.", nameof(reservations));
        if (values.Any(item => item.NodeRow < 1 || item.NodeRow % 2 == 0))
            throw new ArgumentException("Reserved node rows must be positive odd numbers.", nameof(reservations));
        if (values.Zip(values.Skip(1), (left, right) => right.NodeRow <= left.NodeRow).Any(item => item))
            throw new ArgumentException("Reserved node rows must be strictly increasing.", nameof(reservations));
        if (values.Count(item => item.IsExternal) != 1 || !values[values.Length - 1].IsExternal)
            throw new ArgumentException("External must be the final shared reservation.", nameof(reservations));

        Reservations = Array.AsReadOnly(values);
        byName = values.ToDictionary(item => item.Name, StringComparer.Ordinal);
    }

    private readonly IReadOnlyDictionary<string, ArchitectureV6FrozenReservation> byName;
    public IReadOnlyList<ArchitectureV6FrozenReservation> Reservations { get; }
    public ArchitectureV6FrozenReservation External => Reservations[Reservations.Count - 1];

    public ArchitectureV6FrozenReservation ForRole(string role) =>
        byName.TryGetValue(role, out var reservation)
            ? reservation
            : throw new KeyNotFoundException($"No frozen reservation exists for role '{role}'.");
}
