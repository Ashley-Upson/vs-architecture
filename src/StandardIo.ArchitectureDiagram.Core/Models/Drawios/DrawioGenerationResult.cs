using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record ValidationPoint(int X, int Y);
public sealed record ValidationSegment(ValidationPoint Start, ValidationPoint End);
public sealed record GeneratedRoute(string LogicalRouteId, IReadOnlyList<ValidationPoint> Points);

public sealed record ValidationFinding(
    string Category,
    string LogicalRouteId,
    string? OtherRouteId,
    string? OtherNodeId,
    int Magnitude,
    string Description,
    IReadOnlyList<ValidationPoint> Locations,
    IReadOnlyList<ValidationSegment> OffendingSegments,
    int? RequiredSpacing,
    int? ActualSpacing,
    int? ParallelOverlapLength,
    bool IsStrictlyEnforced);

public sealed record RouteRepairAttempt(
    string LogicalRouteId,
    string FindingCategory,
    bool Applied,
    string Decision,
    IReadOnlyList<ValidationPoint> BeforePoints,
    IReadOnlyList<ValidationPoint> AfterPoints,
    bool WorkBudgetExhausted = false);

public sealed class DrawioGenerationResult
{
    public DrawioGenerationResult(
        string document,
        IReadOnlyList<ValidationFinding> preRepairFindings,
        IReadOnlyList<ValidationFinding> validationFindings,
        IReadOnlyList<RouteRepairAttempt> repairAttempts,
        IReadOnlyList<GeneratedRoute> routes,
        bool serializationSucceeded,
        bool strictValidationPassed)
    {
        Document = document;
        PreRepairFindings = preRepairFindings;
        ValidationFindings = validationFindings;
        RepairAttempts = repairAttempts;
        Routes = routes;
        SerializationSucceeded = serializationSucceeded;
        StrictValidationPassed = strictValidationPassed;
    }

    public string Document { get; }
    public IReadOnlyList<ValidationFinding> PreRepairFindings { get; }
    public IReadOnlyList<ValidationFinding> ValidationFindings { get; }
    public IReadOnlyList<RouteRepairAttempt> RepairAttempts { get; }
    public IReadOnlyList<GeneratedRoute> Routes { get; }
    public bool SerializationSucceeded { get; }
    public bool StrictValidationPassed { get; }
}
