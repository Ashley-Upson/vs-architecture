using System.Collections.Generic;
using System.Text.Json;

namespace StandardIo.ArchitectureDiagram.Core.Models.Generation;

public sealed record ArchitectureEvidenceInput(
    string InputPath,
    string? SourceRevision,
    string CliVersion,
    JsonElement NormalizedSettings,
    string ScopeMode,
    IReadOnlyList<string> ConfiguredRoots,
    IReadOnlyList<string> InferredRoots);

public sealed record ArchitectureEvidenceTopology(
    ArchitectureGenerationManifest Manifest,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? SemanticNodeToRenderInstances);

public sealed record ArchitectureEvidencePlacement(
    ValidationRectangle PageBounds,
    IReadOnlyList<ArchitectureProjectGeometry> Projects,
    IReadOnlyList<ArchitectureNodeGeometry> Nodes);

public sealed record ArchitectureEvidenceAllocation(
    string Authority,
    string CoordinateScope,
    bool DetailedTelemetryIncluded);

public sealed record ArchitectureEvidenceOwnership(
    int LogicalRouteCount,
    int PhysicalEdgeCellCount,
    string Representation);

public sealed record ArchitectureEvidenceValidation(
    IReadOnlyList<ValidationFinding> LogicalFindings,
    IReadOnlyList<ValidationFinding> PhysicalFindings,
    IReadOnlyList<ArchitectureGeometryFinding> GeometryFindings);

public sealed record ArchitectureEvidenceDeterminism(
    string PageSha256,
    string AnalysisSha256,
    SerializationRepeatResult? SerializationRepeat);

public sealed record ArchitectureGenerationEvidence(
    ArchitectureEvidenceInput Input,
    ArchitectureGenerationManifest SemanticAnalysis,
    ArchitectureEvidenceTopology TopologyProjection,
    ArchitectureEvidencePlacement ProjectPlacement,
    ArchitectureEvidenceAllocation TerminalAllocation,
    ArchitectureEvidenceAllocation HorizontalSlotAllocation,
    ArchitectureEvidenceAllocation VerticalAndReturnColumnAllocation,
    IReadOnlyList<GeneratedRoute> LogicalRoutes,
    ArchitectureEvidenceOwnership PhysicalOwnership,
    ArchitectureEvidenceValidation Validation,
    ArchitectureEvidenceDeterminism Determinism,
    IReadOnlyList<PipelineStageMetric> Performance);
