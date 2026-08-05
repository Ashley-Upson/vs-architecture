using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Models.Generation;

public enum ArchitectureRenderingMode
{
    Production,
    DevelopmentProjectRegion
}

public sealed record ArchitectureGenerationManifest(
    int ProjectCount,
    int SemanticNodeCount,
    int SemanticLinkCount,
    int RenderedRouteCount,
    int LogicalFindingCount,
    int PhysicalFindingCount,
    string PageStableKey)
{
    public int SemanticClassCount { get; init; }
    public int SemanticInterfaceCount { get; init; }
    public int UniqueInterfaceResolutionCount { get; init; }
    public int UnresolvedInterfaceCount { get; init; }
    public int MultipleInterfaceResolutionCount { get; init; }
    public int ProjectedRenderNodeCount { get; init; }
    public int ProjectedRenderLinkCount { get; init; }
    public int DuplicatedInstanceCount { get; init; }
    public int CanonicalSharedNodeCount { get; init; }
    public int ExceptionAuthorisedDuplicateCount { get; init; }
    public int MultiParentNodeCount { get; init; }
}

public sealed record ArchitectureEligibilityResult(bool Eligible, IReadOnlyList<string> Reasons);

public sealed record SerializationRepeatResult(
    int RequestedRepeats,
    bool IsDeterministic,
    IReadOnlyList<string> DocumentHashes);

public sealed record ValidationFinding(
    string Category,
    string LogicalRouteId,
    string? OtherRouteId,
    string? OtherNodeId,
    int Magnitude,
    string Description,
    bool IsStrictlyEnforced);

public sealed class TypedArchitectureGenerationResult
{
    private readonly Lazy<DrawioDiagnosticExportResult> diagnostics;

    public TypedArchitectureGenerationResult(
        ArchitectureDiagramModel diagram,
        DrawioPage page,
        IReadOnlyList<ValidationFinding> findings,
        ArchitectureGenerationManifest manifest,
        ArchitectureEligibilityResult eligibility,
        Func<DrawioDiagnosticExportResult> diagnosticFactory,
        SerializationRepeatResult? serializationRepeat,
        ArchitecturePlanningMetrics? planningMetrics = null)
    {
        Diagram = diagram ?? throw new ArgumentNullException(nameof(diagram));
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Findings = findings ?? throw new ArgumentNullException(nameof(findings));
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        diagnostics = new Lazy<DrawioDiagnosticExportResult>(diagnosticFactory ?? throw new ArgumentNullException(nameof(diagnosticFactory)), true);
        SerializationRepeat = serializationRepeat;
        PlanningMetrics = planningMetrics;
    }

    public ArchitectureDiagramModel Diagram { get; }
    public DrawioPage Page { get; }
    public IReadOnlyList<ValidationFinding> Findings { get; }
    public ArchitectureGenerationManifest Manifest { get; }
    public ArchitectureEligibilityResult Eligibility { get; }
    public DrawioDiagnosticExportResult Diagnostics => diagnostics.Value;
    public SerializationRepeatResult? SerializationRepeat { get; }
    public ArchitecturePlanningMetrics? PlanningMetrics { get; }
    public bool StrictValidationPassed => Eligibility.Eligible;
    public bool SceneProduced => Page is not null;
    public bool SemanticallyComplete => Manifest.ProjectedRenderNodeCount >= Manifest.SemanticNodeCount &&
                                        Manifest.ProjectedRenderLinkCount >= Manifest.SemanticLinkCount;
    public bool SerializationSucceeded => Page.GraphModel is not null;
    public bool StrictlyValid => StrictValidationPassed;
}

public sealed class DrawioDiagnosticExportResult
{
    public DrawioDiagnosticExportResult(
        string content,
        string reportJson,
        IReadOnlyDictionary<string, string> focusedOutputs,
        int enforcedFindingCount,
        int uniqueRejectedRouteCount)
    {
        Content = content;
        ReportJson = reportJson;
        FocusedOutputs = focusedOutputs;
        EnforcedFindingCount = enforcedFindingCount;
        UniqueRejectedRouteCount = uniqueRejectedRouteCount;
    }

    public string Content { get; }
    public string ReportJson { get; }
    public IReadOnlyDictionary<string, string> FocusedOutputs { get; }
    public int EnforcedFindingCount { get; }
    public int UniqueRejectedRouteCount { get; }
}
