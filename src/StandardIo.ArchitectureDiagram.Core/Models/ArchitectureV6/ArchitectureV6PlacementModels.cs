using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public sealed record ArchitectureV6FrozenNodePlacement(
    string PhysicalNodeId,
    string? PositionalOwnerId,
    PlanningGridId GridId,
    PlanningGridRowId RowId,
    PlanningGridColumnId CentreColumnId,
    int ColumnSpan,
    bool IsExternal,
    bool IsStandalone);

public sealed class ArchitectureV6PlacementFreeze
{
    public ArchitectureV6PlacementFreeze(
        IReadOnlyList<ArchitectureV6FrozenNodePlacement> nodes,
        IReadOnlyList<ProjectRoutingGrid> projects,
        DiagramRoutingGrid diagramGrid,
        string fingerprint)
    {
        Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
        Projects = Array.AsReadOnly((projects ?? throw new ArgumentNullException(nameof(projects))).Select(FreezeProject).ToArray());
        DiagramGrid = FreezeDiagramGrid(diagramGrid ?? throw new ArgumentNullException(nameof(diagramGrid)));
        Fingerprint = string.IsNullOrWhiteSpace(fingerprint) ? throw new ArgumentException("A placement fingerprint is required.", nameof(fingerprint)) : fingerprint;
        IsFrozen = true;
    }

    public IReadOnlyList<ArchitectureV6FrozenNodePlacement> Nodes { get; }
    public IReadOnlyList<ProjectRoutingGrid> Projects { get; }
    public DiagramRoutingGrid DiagramGrid { get; }
    public string Fingerprint { get; }
    public bool IsFrozen { get; }

    private static ProjectRoutingGrid FreezeProject(ProjectRoutingGrid project) => project with
    {
        Grid = FreezeGrid(project.Grid),
        SubtreeReservations = Array.AsReadOnly(project.SubtreeReservations.ToArray()),
        PhysicalNodeIds = Array.AsReadOnly(project.OwnedPhysicalNodeIds.ToArray()),
        ExternalNodeIds = Array.AsReadOnly(project.OwnedExternalNodeIds.ToArray()),
        EndpointReservations = Array.AsReadOnly(project.OwnedEndpointReservations.ToArray())
    };

    private static DiagramRoutingGrid FreezeDiagramGrid(DiagramRoutingGrid diagram) => diagram with
    {
        Grid = FreezeGrid(diagram.Grid),
        ProjectFootprints = Array.AsReadOnly(diagram.ProjectFootprints.ToArray()),
        Transitions = Array.AsReadOnly(diagram.Transitions.ToArray())
    };

    private static PlanningGrid FreezeGrid(PlanningGrid grid) => grid with
    {
        Rows = Array.AsReadOnly(grid.Rows.ToArray()),
        Columns = Array.AsReadOnly(grid.Columns.ToArray()),
        Cells = new ReadOnlyDictionary<PlanningGridCellId, PlanningGridCell>(
            grid.Cells.ToDictionary(item => item.Key, item => item.Value))
    };
}
