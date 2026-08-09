using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public enum ArchitectureV7ProjectionMode
{
    Canonical,
    ConfiguredDuplicateBranches
}

public sealed record ArchitectureV7ProjectionPolicy(
    ArchitectureV7ProjectionMode Mode,
    IReadOnlyList<string> DuplicationPatterns);

public sealed record ArchitectureV7DuplicationProvenance(
    string SemanticNodeId,
    string SemanticLinkId,
    string BranchSourcePhysicalNodeId,
    int BranchOrdinal,
    string Reason);

public sealed record ArchitectureV7PhysicalNode(
    string PhysicalNodeId,
    string SemanticNodeId,
    string? ProjectId,
    bool IsExternal,
    bool IsStandalone,
    string Name,
    string FullName,
    string Kind,
    ArchitectureV7ProjectionMode ProjectionMode,
    ArchitectureV7DuplicationProvenance? DuplicationProvenance);

public sealed record ArchitectureV7PhysicalLink(
    string PhysicalLinkId,
    string SemanticLinkId,
    string SourcePhysicalNodeId,
    string DestinationPhysicalNodeId,
    string? SourceProjectId,
    string? DestinationProjectId,
    string Kind);

public sealed record ArchitectureV7ProjectionDiagnostic(string Code, string Message, string? SubjectId = null);

public sealed class ArchitectureV7PhysicalProjectionResult
{
    public ArchitectureV7PhysicalProjectionResult(
        IReadOnlyList<ArchitectureV7PhysicalNode> physicalNodes,
        IReadOnlyList<ArchitectureV7PhysicalLink> physicalLinks,
        IReadOnlyDictionary<string, IReadOnlyList<string>> semanticNodeToPhysicalNodeIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> semanticLinkToPhysicalLinkIds,
        IReadOnlyList<string> unaccountedSemanticNodeIds,
        IReadOnlyList<string> unaccountedSemanticLinkIds,
        IReadOnlyList<ArchitectureV7ProjectionDiagnostic> diagnostics,
        string freezeFingerprint)
    {
        PhysicalNodes = ReadOnly(physicalNodes);
        PhysicalLinks = ReadOnly(physicalLinks);
        SemanticNodeToPhysicalNodeIds = ReadOnlyMap(semanticNodeToPhysicalNodeIds);
        SemanticLinkToPhysicalLinkIds = ReadOnlyMap(semanticLinkToPhysicalLinkIds);
        UnaccountedSemanticNodeIds = ReadOnly(unaccountedSemanticNodeIds);
        UnaccountedSemanticLinkIds = ReadOnly(unaccountedSemanticLinkIds);
        Diagnostics = ReadOnly(diagnostics);
        FreezeFingerprint = freezeFingerprint ?? throw new ArgumentNullException(nameof(freezeFingerprint));
    }

    public IReadOnlyList<ArchitectureV7PhysicalNode> PhysicalNodes { get; }
    public IReadOnlyList<ArchitectureV7PhysicalLink> PhysicalLinks { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SemanticNodeToPhysicalNodeIds { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SemanticLinkToPhysicalLinkIds { get; }
    public IReadOnlyList<string> UnaccountedSemanticNodeIds { get; }
    public IReadOnlyList<string> UnaccountedSemanticLinkIds { get; }
    public IReadOnlyList<ArchitectureV7ProjectionDiagnostic> Diagnostics { get; }
    public string FreezeFingerprint { get; }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) => Array.AsReadOnly((values ?? Array.Empty<T>()).ToArray());

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadOnlyMap(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        new Dictionary<string, IReadOnlyList<string>>((values ?? new Dictionary<string, IReadOnlyList<string>>())
            .ToDictionary(item => item.Key, item => ReadOnly(item.Value), StringComparer.Ordinal), StringComparer.Ordinal);
}

public sealed record ArchitectureV7PositionalOwnershipDecision(
    string PhysicalNodeId,
    string SemanticNodeId,
    string? PositionalParentPhysicalNodeId,
    IReadOnlyList<string> IncomingPhysicalParentIds,
    IReadOnlyList<string> AdditionalSemanticParentPhysicalNodeIds);

public sealed class ArchitectureV7PositionalOwnershipResult
{
    public ArchitectureV7PositionalOwnershipResult(
        ArchitectureV7PhysicalProjectionResult projection,
        IReadOnlyList<ArchitectureV7PositionalOwnershipDecision> decisions,
        string projectionFreezeFingerprint,
        string freezeFingerprint)
    {
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        Decisions = Array.AsReadOnly((decisions ?? Array.Empty<ArchitectureV7PositionalOwnershipDecision>()).ToArray());
        ProjectionFreezeFingerprint = projectionFreezeFingerprint ?? throw new ArgumentNullException(nameof(projectionFreezeFingerprint));
        FreezeFingerprint = freezeFingerprint ?? throw new ArgumentNullException(nameof(freezeFingerprint));
    }

    public ArchitectureV7PhysicalProjectionResult Projection { get; }
    public IReadOnlyList<ArchitectureV7PositionalOwnershipDecision> Decisions { get; }
    public string ProjectionFreezeFingerprint { get; }
    public string FreezeFingerprint { get; }
}
