using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.Architectures;

public enum ArchitectureRenderNodeOccurrence
{
    Canonical,
    Duplicated
}

public enum ArchitectureDuplicationReason
{
    None,
    GlobalPolicy,
    ExceptionPattern
}

public sealed record ArchitectureRenderProject(string Id, string Name, int Order);

public sealed record ArchitectureRenderNode(
    string Id,
    string SemanticNodeId,
    string? ProjectId,
    string DisplayText,
    string SemanticTypeIdentity,
    string NodeKind,
    bool IsExternal,
    string ExternalTag,
    InterfaceResolutionStatus InterfaceResolution,
    string? InterfaceIdentity,
    string? ImplementationIdentity,
    int ImplementationCount,
    ArchitectureRenderNodeOccurrence Occurrence,
    ArchitectureDuplicationReason DuplicationReason,
    string? PlacementParentRenderId,
    int Order);

public sealed record ArchitectureRenderLink(
    string Id,
    string SemanticLinkId,
    string SourceRenderInstanceId,
    string TargetRenderInstanceId,
    string SourceSemanticId,
    string TargetSemanticId,
    string Kind,
    int Order);

public sealed record ArchitectureRenderGraph(
    IReadOnlyList<ArchitectureRenderProject> Projects,
    IReadOnlyList<ArchitectureRenderNode> Nodes,
    IReadOnlyList<ArchitectureRenderLink> Links,
    IReadOnlyList<string> TraversalRootSemanticIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RenderInstancesBySemanticNodeId);
