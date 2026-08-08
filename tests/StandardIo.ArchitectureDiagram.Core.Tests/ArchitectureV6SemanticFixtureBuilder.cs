using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

/// <summary>
/// Builds analyser-shaped semantic input for planner integration tests. The builder deliberately
/// stops at ArchitectureDiagram; physical IDs, coordinates and route data are never fixture input.
/// </summary>
internal sealed class ArchitectureV6SemanticFixtureBuilder
{
    private readonly List<ArchitectureProject> projects = new();
    private readonly List<ArchitectureExternalNode> externalNodes = new();
    private readonly List<ArchitectureLink> links = new();
    private readonly Dictionary<string, List<ArchitectureNode>> projectNodes = new(StringComparer.Ordinal);
    private string? selectedProjectId;

    public ArchitectureV6SemanticFixtureBuilder Project(string id, string name)
    {
        if (projectNodes.ContainsKey(id))
            throw new ArgumentException($"Project '{id}' was already added.", nameof(id));

        projectNodes[id] = new List<ArchitectureNode>();
        projects.Add(new ArchitectureProject(id, name, projectNodes[id], id));
        selectedProjectId ??= id;
        return this;
    }

    public ArchitectureV6SemanticFixtureBuilder Select(string projectId)
    {
        EnsureProject(projectId);
        selectedProjectId = projectId;
        return this;
    }

    public ArchitectureV6SemanticFixtureBuilder Node(
        string id,
        string name,
        string projectId,
        string? kind = null,
        IReadOnlyList<string>? interfaces = null)
    {
        EnsureProject(projectId);
        projectNodes[projectId].Add(new ArchitectureNode(
            id,
            projectId,
            name,
            $"{projects.Single(project => project.Id == projectId).Name}.{name}",
            kind ?? "Class",
            id,
            interfaces ?? Array.Empty<string>()));
        return this;
    }

    public ArchitectureV6SemanticFixtureBuilder External(
        string id,
        string name,
        string tag = "interface",
        string assemblyName = "External")
    {
        externalNodes.Add(new ArchitectureExternalNode(
            id,
            name,
            assemblyName,
            id,
            $"{assemblyName}.{name}",
            tag));
        return this;
    }

    public ArchitectureV6SemanticFixtureBuilder Link(string id, string sourceId, string targetId, string kind = "internal")
    {
        links.Add(new ArchitectureLink(id, sourceId, targetId, kind));
        return this;
    }

    public ArchitectureDiagramModel BuildModel() => new(
        projects.Select(project => project with { Nodes = projectNodes[project.Id].ToArray() }).ToArray(),
        externalNodes.ToArray(),
        links.ToArray(),
        null);

    public ArchitecturePlanningRequest BuildRequest(
        IReadOnlyList<ArchitectureV6RoleRule>? roleRules = null,
        NodePlacementPolicy? nodePlacement = null,
        NodeProjectionMode projectionMode = NodeProjectionMode.Canonical)
    {
        var projectId = selectedProjectId ?? throw new InvalidOperationException("A selected project is required.");
        var placement = nodePlacement ?? new NodePlacementPolicy(
            "*OrchestrationService", 120, 60, 20, 40, roleRules);
        if (roleRules is not null)
            placement = placement with { RoleRules = roleRules };

        return new ArchitecturePlanningRequest(
            BuildModel(),
            new ArchitectureSelectionScope("SelectedProjects", new[] { projectId }, Array.Empty<string>()),
            new ArchitectureGenerationSettingsSnapshot("drawio", "[External]", Array.Empty<string>(), Array.Empty<string>()),
            new NodeProjectionPolicy(projectionMode, Array.Empty<string>()),
            new ProjectPlacementPolicy(true, "border"),
            placement,
            new RoutePlanningPolicy(12, 8, "[External]"),
            new GridSizingPolicy(20, 20, 20, 30),
            new ValidationPolicy(ArchitectureValidationMode.Normal),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private void EnsureProject(string projectId)
    {
        if (!projectNodes.ContainsKey(projectId))
            throw new ArgumentException($"Project '{projectId}' has not been added.", nameof(projectId));
    }
}
