using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public enum NodeProjectionMode
{
    Canonical,
    DuplicateBranches
}

public enum ArchitectureValidationMode
{
    Normal,
    Strict,
    Diagnostic
}

public sealed record ArchitectureSelectionScope(
    string ScopePolicy,
    IReadOnlyList<string> SelectedProjectIds,
    IReadOnlyList<string> RootSemanticNodeIds);

public sealed record NodeProjectionPolicy(
    NodeProjectionMode Mode,
    IReadOnlyList<string> DuplicationExceptionPatterns);

public sealed record ProjectPlacementPolicy(bool ShowProjectContainers, string ProjectContainerStyle);

public sealed record NodePlacementPolicy(
    string BaselinePattern,
    int MinimumNodeWidth,
    int MinimumNodeHeight,
    int HorizontalSpacing,
    int VerticalSpacing,
    IReadOnlyList<ArchitectureV6RoleRule>? RoleRules = null,
    ArchitectureV6SpacingPolicy? Spacing = null);

public sealed record ArchitectureV6RoleRule(string Name, string Pattern, int Order);

public sealed record ArchitectureV6SpacingPolicy(
    int Sibling,
    int SiblingGroup,
    int OwnershipBoundary,
    int RoleBand,
    int LogicalLayer,
    int External,
    int Broker,
    int ProjectBoundary)
{
    public static ArchitectureV6SpacingPolicy From(int normal) => new(
        normal, normal * 2, normal * 2, normal * 2, normal, normal * 2, normal, normal * 3);
}

public sealed record RoutePlanningPolicy(
    int MinimumParallelSpacing,
    int MinimumPortSpacing,
    string ExternalDependencyTag);

public sealed record GridSizingPolicy(
    int CellWidth,
    int CellHeight,
    int ContainerPadding,
    int ProjectHeaderHeight);

public sealed record ValidationPolicy(ArchitectureValidationMode Mode);

public sealed record ArchitectureGenerationSettingsSnapshot(
    string OutputRenderer,
    string ExternalDependencyTag,
    IReadOnlyList<string> ExcludedNamespaces,
    IReadOnlyList<string> ExcludedNames);

public sealed record ArchitectureV6ConnectorStyle(string StrokeColor, int StrokeWidth, bool Rounded);

public sealed record ArchitectureV6StyleRule(
    string Match,
    string FillColor,
    string StrokeColor,
    string FontColor,
    string Shape,
    bool Shadow,
    string? ExtraStyle);

public sealed record ArchitectureV6StyleOverride(string FullName, ArchitectureV6StyleRule Style);

public sealed record ArchitecturePlanningRequest(
    Architectures.ArchitectureDiagram SemanticModel,
    ArchitectureSelectionScope SelectedScope,
    ArchitectureGenerationSettingsSnapshot GenerationSettings,
    NodeProjectionPolicy NodeProjection,
    ProjectPlacementPolicy ProjectPlacement,
    NodePlacementPolicy NodePlacement,
    RoutePlanningPolicy RoutePlanning,
    GridSizingPolicy GridSizing,
    ValidationPolicy Validation,
    IReadOnlyList<string> StyleRules,
    IReadOnlyList<string> StyleOverrides,
    IReadOnlyList<ArchitectureV6StyleRule>? StylePolicies = null,
    IReadOnlyList<ArchitectureV6StyleOverride>? StyleOverridesWithValues = null,
    ArchitectureV6StyleRule? ProjectContainerStyle = null,
    ArchitectureV6StyleRule? ExternalDependencyStyle = null,
    ArchitectureV6ConnectorStyle? ConnectorStyle = null);
