using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7RouteCorridorProjectionTests
{
    private const ArchitectureV7CellCapability General = ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;

    [Fact]
    public void Endpoint_node_cells_are_attachments_and_are_not_corridor_usage_cells()
    {
        var placement = Placement(3, 3);
        var route = Route("link", "source", "target", (0, 0), (0, 1), (0, 2));
        var result = Project(placement, route);

        var usage = Assert.Single(result.Usages);
        Assert.Equal("corridor:H:0:0-2", usage.CorridorId);
        Assert.Equal(new[] { new ArchitectureV7RouteCell(0, 1) }, usage.TraversalCells);
        Assert.DoesNotContain(new ArchitectureV7RouteCell(0, 0), usage.TraversalCells);
        Assert.DoesNotContain(new ArchitectureV7RouteCell(0, 2), usage.TraversalCells);
    }

    [Fact]
    public void Projection_preserves_frozen_route_topology_and_projects_each_maximal_run()
    {
        var placement = Placement(3, 3);
        var route = Route("link", "source", "target", (0, 0), (1, 0), (2, 0), (2, 1), (2, 2));
        var before = route.Cells.ToArray();
        var result = Project(placement, route);

        Assert.Equal(before, route.Cells);
        Assert.Equal(2, result.Usages.Count);
        Assert.Equal(ArchitectureV7RunOrientation.Vertical, result.Usages[0].Orientation);
        Assert.Equal(ArchitectureV7RunOrientation.Horizontal, result.Usages[1].Orientation);
        Assert.Empty(result.UnprojectedRuns);
    }

    [Fact]
    public void Projection_is_deterministic_when_route_enumeration_is_shuffled()
    {
        var placement = Placement(3, 3);
        var first = new[]
        {
            Route("b", "s2", "t2", (0, 0), (0, 1), (0, 2)),
            Route("a", "s1", "t1", (2, 0), (2, 1), (2, 2))
        };
        var second = first.AsEnumerable().Reverse().ToArray();

        var left = Project(placement, first);
        var right = Project(placement, second);

        Assert.Equal(left.Fingerprint, right.Fingerprint);
        Assert.Equal(left.Usages.Select(usage => usage.UsageId), right.Usages.Select(usage => usage.UsageId));
    }

    [Fact]
    public void Missing_capability_corridor_is_reported_without_mutating_the_route()
    {
        var placement = Placement(1, 3, (_, column) => column == 1 ? ArchitectureV7CellCapability.Blocked : General);
        var route = Route("link", "source", "target", (0, 0), (0, 1), (0, 2));

        var result = Project(placement, route);

        Assert.Empty(result.Usages);
        Assert.Single(result.UnprojectedRuns);
        Assert.Equal(3, route.Cells.Count);
    }

    private static ArchitectureV7RouteCorridorProjectionFreeze Project(ArchitectureV7PlacementFreeze placement, params ArchitectureV7LogicalRoute[] routes)
    {
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(routes, Array.Empty<ArchitectureV7RouteDiagnostic>(),
            placement.PlacementFingerprint, "projection", "routes");
        var corridors = new ArchitectureV7CapabilityCorridorDiscoveryStage().Discover(placement);
        return new ArchitectureV7RouteCorridorProjectionStage().Project(placement, routeFreeze, corridors);
    }

    private static ArchitectureV7PlacementFreeze Placement(int rows, int columns, Func<int, int, ArchitectureV7CellCapability>? capability = null)
    {
        var cells = Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, columns)
            .Select(column => new ArchitectureV7LogicalCell(row, column, capability?.Invoke(row, column) ?? General))).ToArray();
        return new ArchitectureV7PlacementFreeze(Array.Empty<ArchitectureV7FrozenNodePlacement>(), Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(rows, columns, cells), Array.Empty<ArchitectureV7ProjectTransform>(),
            "projection", "ownership", "sizing", "reservation", "placement");
    }

    private static ArchitectureV7LogicalRoute Route(string id, string source, string target, params (int Row, int Column)[] cells) =>
        new(id, id, source, target, cells.Select(cell => new ArchitectureV7RouteCell(cell.Row, cell.Column)).ToArray(), true,
            Array.Empty<ArchitectureV7RouteDiagnostic>(), "test");
}
