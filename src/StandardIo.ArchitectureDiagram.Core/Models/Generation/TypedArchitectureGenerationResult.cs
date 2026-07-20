using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
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
    string PageStableKey);

public sealed record ArchitectureEligibilityResult(bool Eligible, IReadOnlyList<string> Reasons);

public sealed record SerializationRepeatResult(int RequestedRepeats, bool IsDeterministic, IReadOnlyList<string> DocumentHashes);

public sealed record ArchitectureDevelopmentArtifacts(
    string InvariantJson,
    IReadOnlyDictionary<string, string> NamedJsonArtifacts);

public sealed class ArchitectureRenderResult
{
    private readonly Lazy<DrawioDiagnosticExportResult> diagnostics;

    public ArchitectureRenderResult(
        DrawioPage page,
        IReadOnlyList<ValidationFinding> preRepairFindings,
        IReadOnlyList<ValidationFinding> logicalFindings,
        IReadOnlyList<ValidationFinding> physicalFindings,
        IReadOnlyList<RouteRepairAttempt> repairAttempts,
        IReadOnlyList<GeneratedRoute> routes,
        IReadOnlyList<PipelineStageMetric> timings,
        ArchitectureEligibilityResult eligibility,
        Func<DrawioDiagnosticExportResult> diagnosticFactory,
        ArchitectureDevelopmentArtifacts? developmentArtifacts = null)
    {
        Page = page;
        PreRepairFindings = preRepairFindings;
        LogicalFindings = logicalFindings;
        PhysicalFindings = physicalFindings;
        RepairAttempts = repairAttempts;
        Routes = routes;
        Timings = timings;
        Eligibility = eligibility;
        diagnostics = new Lazy<DrawioDiagnosticExportResult>(diagnosticFactory, true);
        DevelopmentArtifacts = developmentArtifacts;
    }

    public DrawioPage Page { get; }
    public IReadOnlyList<ValidationFinding> PreRepairFindings { get; }
    public IReadOnlyList<ValidationFinding> LogicalFindings { get; }
    public IReadOnlyList<ValidationFinding> PhysicalFindings { get; }
    public IReadOnlyList<RouteRepairAttempt> RepairAttempts { get; }
    public IReadOnlyList<GeneratedRoute> Routes { get; }
    public IReadOnlyList<PipelineStageMetric> Timings { get; }
    public ArchitectureEligibilityResult Eligibility { get; }
    public ArchitectureDevelopmentArtifacts? DevelopmentArtifacts { get; }
    public DrawioDiagnosticExportResult Diagnostics => diagnostics.Value;
}

public sealed class TypedArchitectureGenerationResult
{
    private readonly Lazy<DrawioDiagnosticExportResult> diagnostics;

    public TypedArchitectureGenerationResult(
        ArchitectureDiagramModel diagram,
        DrawioPage page,
        IReadOnlyList<ValidationFinding> preRepairFindings,
        IReadOnlyList<ValidationFinding> logicalFindings,
        IReadOnlyList<ValidationFinding> physicalFindings,
        IReadOnlyList<RouteRepairAttempt> repairAttempts,
        IReadOnlyList<GeneratedRoute> routes,
        IReadOnlyList<PipelineStageMetric> timings,
        ArchitectureGenerationManifest manifest,
        ArchitectureEligibilityResult eligibility,
        Func<DrawioDiagnosticExportResult> diagnosticFactory,
        SerializationRepeatResult? serializationRepeat,
        ArchitectureDevelopmentArtifacts? developmentArtifacts)
    {
        Diagram = diagram;
        Page = page;
        PreRepairFindings = preRepairFindings;
        LogicalFindings = logicalFindings;
        PhysicalFindings = physicalFindings;
        RepairAttempts = repairAttempts;
        Routes = routes;
        Timings = timings;
        Manifest = manifest;
        Eligibility = eligibility;
        diagnostics = new Lazy<DrawioDiagnosticExportResult>(diagnosticFactory, true);
        SerializationRepeat = serializationRepeat;
        DevelopmentArtifacts = developmentArtifacts;
    }

    public ArchitectureDiagramModel Diagram { get; }
    public DrawioPage Page { get; }
    public IReadOnlyList<ValidationFinding> PreRepairFindings { get; }
    public IReadOnlyList<ValidationFinding> LogicalFindings { get; }
    public IReadOnlyList<ValidationFinding> PhysicalFindings { get; }
    public IReadOnlyList<RouteRepairAttempt> RepairAttempts { get; }
    public IReadOnlyList<GeneratedRoute> Routes { get; }
    public IReadOnlyList<PipelineStageMetric> Timings { get; }
    public ArchitectureGenerationManifest Manifest { get; }
    public ArchitectureEligibilityResult Eligibility { get; }
    public DrawioDiagnosticExportResult Diagnostics => diagnostics.Value;
    public SerializationRepeatResult? SerializationRepeat { get; }
    public ArchitectureDevelopmentArtifacts? DevelopmentArtifacts { get; }
    public bool StrictValidationPassed => LogicalFindings.Concat(PhysicalFindings).All(finding => !finding.IsStrictlyEnforced);
}
