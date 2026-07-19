namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ChangedIntervalReassignmentResult(
    DeterministicSlotAssignment InitialAssignment,
    DeterministicSlotAssignment FinalAssignment,
    bool InterLayerHeightChanged,
    bool FurtherMovementRequested,
    int PassCount);
