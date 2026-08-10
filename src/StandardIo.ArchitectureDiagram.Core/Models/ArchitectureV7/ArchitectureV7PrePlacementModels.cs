using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7PrePlacementConfiguration(
    int ConfiguredBaseCellWidth,
    int ConfiguredMinimumWidth,
    int LabelCharacterWidth,
    int LabelHorizontalMargin,
    int TerminalPortSpacing,
    int TerminalInset,
    IReadOnlyList<ArchitectureV7ReservedRoleRule> ReservedLayerTypePatterns);

public sealed record ArchitectureV7ReservedRoleRule(string Name, string Pattern, int Order);

/// <summary>Named conversions for the shared V7 reserved node-row coordinate domain.</summary>
public static class ArchitectureV7ReservationCoordinates
{
    public const int FirstReservedNodeRow = 1;
    public const int ProjectCommonNodeRowOffset = 2;

    public static int ReservedNodeRowFromSemanticDepth(int semanticDepth)
    {
        if (semanticDepth < 0) throw new ArgumentOutOfRangeException(nameof(semanticDepth));
        return checked(semanticDepth * 2 + FirstReservedNodeRow);
    }

    public static int TreeLayerFromReservedNodeRow(int reservedNodeRow)
    {
        if (reservedNodeRow < FirstReservedNodeRow || reservedNodeRow % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(reservedNodeRow), "A reserved node row must be a positive odd logical row.");
        return (reservedNodeRow - FirstReservedNodeRow) / 2;
    }

    public static int FinalCommonNodeRowFromReservedNodeRow(int reservedNodeRow)
    {
        _ = TreeLayerFromReservedNodeRow(reservedNodeRow);
        return checked(reservedNodeRow + ProjectCommonNodeRowOffset);
    }

    public static int NextReservedNodeRow(int reservedNodeRow)
    {
        _ = TreeLayerFromReservedNodeRow(reservedNodeRow);
        return checked(reservedNodeRow + 2);
    }
}

public sealed record ArchitectureV7NodeSpanRequirement(
    string PhysicalNodeId,
    int LogicalSpan,
    int VisibleLabelRequirement,
    int IncomingTerminalRequirement,
    int OutgoingTerminalRequirement,
    int ConfiguredMinimumRequirement,
    int RequiredWidth,
    int ConfiguredBaseCellWidth,
    string Provenance);

public sealed class ArchitectureV7NodeSpanSizingResult
{
    public ArchitectureV7NodeSpanSizingResult(
        ArchitectureV7PositionalOwnershipResult ownership,
        IReadOnlyList<ArchitectureV7NodeSpanRequirement> requirements,
        string freezeFingerprint)
    {
        Ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        Requirements = Array.AsReadOnly((requirements ?? Array.Empty<ArchitectureV7NodeSpanRequirement>()).OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal).ToArray());
        FreezeFingerprint = freezeFingerprint ?? throw new ArgumentNullException(nameof(freezeFingerprint));
    }

    public ArchitectureV7PositionalOwnershipResult Ownership { get; }
    public IReadOnlyList<ArchitectureV7NodeSpanRequirement> Requirements { get; }
    public string FreezeFingerprint { get; }
}

public sealed record ArchitectureV7ReservedDepthConstraint(
    string ReservationName,
    string PhysicalNodeId,
    int NaturalDepth,
    int RequiredNodeRow,
    bool IsExternal,
    string Provenance);

public sealed record ArchitectureV7ReservedDepthRequirement(
    string ReservationName,
    string Pattern,
    int Order,
    int MatchCount,
    int RequiredNodeRow,
    IReadOnlyList<ArchitectureV7ReservedDepthConstraint> Constraints);

public sealed class ArchitectureV7ReservationInspectionResult
{
    public ArchitectureV7ReservationInspectionResult(
        ArchitectureV7PositionalOwnershipResult ownership,
        IReadOnlyDictionary<string, int> naturalDepthByPhysicalNodeId,
        IReadOnlyList<ArchitectureV7ReservedDepthRequirement> requirements,
        IReadOnlyList<string> diagnostics,
        string freezeFingerprint)
    {
        Ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        NaturalDepthByPhysicalNodeId = new Dictionary<string, int>((naturalDepthByPhysicalNodeId ?? new Dictionary<string, int>())
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        Requirements = Array.AsReadOnly((requirements ?? Array.Empty<ArchitectureV7ReservedDepthRequirement>()).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<string>()).ToArray());
        FreezeFingerprint = freezeFingerprint ?? throw new ArgumentNullException(nameof(freezeFingerprint));
    }

    public ArchitectureV7PositionalOwnershipResult Ownership { get; }
    public IReadOnlyDictionary<string, int> NaturalDepthByPhysicalNodeId { get; }
    public IReadOnlyList<ArchitectureV7ReservedDepthRequirement> Requirements { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public string FreezeFingerprint { get; }
}

public sealed record ArchitectureV7FrozenReservation(
    string Name,
    string Pattern,
    int Order,
    int MatchCount,
    int NodeRow,
    bool IsExternal);

public sealed class ArchitectureV7FrozenReservationTable
{
    public ArchitectureV7FrozenReservationTable(IReadOnlyList<ArchitectureV7FrozenReservation> reservations, string fingerprint)
    {
        Reservations = Array.AsReadOnly((reservations ?? Array.Empty<ArchitectureV7FrozenReservation>()).ToArray());
        Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        if (Reservations.Count == 0 || !Reservations[Reservations.Count - 1].IsExternal)
            throw new ArgumentException("A frozen V7 reservation table must end with External.", nameof(reservations));
    }

    public IReadOnlyList<ArchitectureV7FrozenReservation> Reservations { get; }
    public ArchitectureV7FrozenReservation External => Reservations[Reservations.Count - 1];
    public string Fingerprint { get; }
}

public sealed class ArchitectureV7ReservationReconciliationResult
{
    public ArchitectureV7ReservationReconciliationResult(
        ArchitectureV7ReservationInspectionResult inspection,
        ArchitectureV7FrozenReservationTable table)
    {
        Inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        Table = table ?? throw new ArgumentNullException(nameof(table));
    }

    public ArchitectureV7ReservationInspectionResult Inspection { get; }
    public ArchitectureV7FrozenReservationTable Table { get; }
}
