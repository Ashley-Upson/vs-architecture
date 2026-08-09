using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7ReservationReconciliationStage
{
    public ArchitectureV7ReservationReconciliationResult Reconcile(
        ArchitectureV7ReservationInspectionResult inspection)
    {
        if (inspection is null) throw new ArgumentNullException(nameof(inspection));

        var active = inspection.Requirements
            .Where(requirement => !requirement.ReservationName.Equals("External", StringComparison.OrdinalIgnoreCase) && requirement.MatchCount > 0)
            .OrderBy(requirement => requirement.Order)
            .ThenBy(requirement => requirement.ReservationName, StringComparer.Ordinal)
            .ToList();
        var reservations = new List<ArchitectureV7FrozenReservation>();
        var nextRow = 1;
        foreach (var requirement in active)
        {
            var row = Math.Max(nextRow, OddAtOrAbove(requirement.RequiredNodeRow));
            reservations.Add(new ArchitectureV7FrozenReservation(requirement.ReservationName, requirement.Pattern,
                requirement.Order, requirement.MatchCount, row, false));
            nextRow = checked(row + 2);
        }

        var external = inspection.Requirements.First(requirement => requirement.ReservationName.Equals("External", StringComparison.OrdinalIgnoreCase));
        var externalRow = Math.Max(nextRow, OddAtOrAbove(external.RequiredNodeRow));
        reservations.Add(new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue,
            external.MatchCount, externalRow, true));
        var fingerprintText = inspection.FreezeFingerprint + "#" + string.Join("|", reservations.Select(item =>
            item.Name + ":" + item.Pattern + ":" + item.Order + ":" + item.MatchCount + ":" + item.NodeRow));
        using var sha = SHA256.Create();
        var fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintText))).Replace("-", string.Empty);
        return new ArchitectureV7ReservationReconciliationResult(inspection,
            new ArchitectureV7FrozenReservationTable(reservations, fingerprint));
    }

    private static int OddAtOrAbove(int row) => row % 2 == 0 ? checked(row + 1) : row;
}
