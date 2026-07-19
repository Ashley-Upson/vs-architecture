using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record DifferenceAlternativeChoice(
    string ConflictId,
    string AlternativeId,
    MovementScopeIdentity MovingScope,
    string MovingStructureId,
    string OpposingStructureId,
    HorizontalMovementDirection Direction,
    int Weight,
    int Movement,
    int MovedNodeCount,
    int WidthExpansion,
    GenerationConstraint Constraint);

internal sealed record PositiveDifferenceCycle(
    IReadOnlyList<string> ScopeIds,
    IReadOnlyList<DifferenceAlternativeChoice> Edges,
    int TotalWeight);

internal sealed record DifferenceAlternativeSelection(
    IReadOnlyList<DifferenceAlternativeChoice> Available,
    IReadOnlyList<DifferenceAlternativeChoice> Selected,
    IReadOnlyList<PositiveDifferenceCycle> PositiveCycles,
    int StatesEvaluated,
    int StatesRejected,
    int CompleteSolutions,
    bool IsSatisfiable);
