using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Models.Generation;

public enum DiagramType
{
    Architecture,
    DataModel
}

public enum DiagramGenerationFailurePolicy
{
    FailAll,
    KeepSuccessfulPages
}

public enum DiagramOutputGrouping
{
    SingleDocument
}

public abstract record DiagramGenerationJob(DiagramType DiagramType, string? PageNameHint);

public sealed record ArchitectureGenerationJob(
    ArchitectureAnalysisSettings Analysis,
    ArchitectureRenderSettings Rendering,
    string? PageNameHint = null)
    : DiagramGenerationJob(DiagramType.Architecture, PageNameHint);

public sealed record DataModelGenerationJob(
    DataModelAnalysisSettings Analysis,
    DataModelRenderSettings Rendering,
    bool EmitEmptyPage = false,
    string? PageNameHint = null)
    : DiagramGenerationJob(DiagramType.DataModel, PageNameHint);

public sealed record DiagramGenerationRequest(
    IReadOnlyList<DiagramGenerationJob> Jobs,
    DrawioDocumentSettings DocumentSettings,
    DiagramOutputGrouping Grouping = DiagramOutputGrouping.SingleDocument,
    DiagramGenerationFailurePolicy FailurePolicy = DiagramGenerationFailurePolicy.FailAll);

public sealed record DiagramJobDiagnostic(
    DiagramType DiagramType,
    string Stage,
    string Message,
    bool IsFailure);

public sealed record TypedDiagramGenerationResult(
    DrawioDocument Document,
    IReadOnlyList<DrawioPage> Pages,
    IReadOnlyList<DiagramJobDiagnostic> Diagnostics);
