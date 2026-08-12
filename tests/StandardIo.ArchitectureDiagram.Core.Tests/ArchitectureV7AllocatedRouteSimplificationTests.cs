using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7AllocatedRouteSimplificationTests
{
    [Fact]
    public void Collapses_horizontal_and_vertical_reversals_without_changing_endpoints_or_bends()
    {
        var route = Route(
            (0, 0), (10, 0), (20, 0), (15, 0), (30, 0),
            (30, 10), (30, 20), (30, 15), (30, 30));

        var result = Simplify(route);
        var simplified = Assert.Single(result.Scene.Routes);
        var evidence = Assert.Single(result.Evidence);

        Assert.Equal(new[] { (0d, 0d), (30d, 0d), (30d, 30d) }, simplified.Points.Select(point => (point.X, point.Y)));
        Assert.Equal((0d, 0d), (simplified.Points[0].X, simplified.Points[0].Y));
        Assert.Equal((30d, 30d), (simplified.Points[^1].X, simplified.Points[^1].Y));
        Assert.Equal(2, simplified.Segments.Count);
        Assert.Equal(6, evidence.RedundantPointsRemoved);
        Assert.Equal(4, evidence.ReversalTransitions);
        Assert.Equal(4, evidence.OvershootGroups);
        Assert.All(simplified.Points.Zip(simplified.Points.Skip(1), (a, b) => (a, b)), pair =>
            Assert.True(pair.a.X == pair.b.X || pair.a.Y == pair.b.Y));
        Assert.Contains("allocated-route-canonicalisation", simplified.Provenance);
        Assert.Contains("allocation", simplified.Segments[0].AllocationProvenance);
    }

    [Fact]
    public void Same_axis_lane_mismatch_is_a_hard_failure_and_is_not_simplified()
    {
        var route = Route(new[] { (0d, 0d), (10d, 0d), (20d, 0d) }, new[] { "h0", "h1" });
        var result = Simplify(route);

        var evidence = Assert.Single(result.Evidence);
        Assert.False(evidence.Simplified);
        Assert.Equal(1, evidence.SameAxisLaneMismatchCount);
        Assert.Contains(result.Scene.Diagnostics, item => item.Code == "SIMPLIFIER-SAME-AXIS-LANE-MISMATCH" && item.IsHardFailure);
        Assert.Equal(route.Points, Assert.Single(result.Scene.Routes).Points);
    }

    [Fact]
    public void Diagonal_input_is_a_hard_failure_and_is_not_simplified()
    {
        var route = Route((0, 0), (10, 10));
        var result = Simplify(route);

        var evidence = Assert.Single(result.Evidence);
        Assert.False(evidence.Simplified);
        Assert.Equal(1, evidence.DiagonalSegmentCount);
        Assert.Contains(result.Scene.Diagnostics, item => item.Code == "SIMPLIFIER-DIAGONAL" && item.IsHardFailure);
    }

    [Fact]
    public void Duplicate_points_are_removed_and_multiple_genuine_bends_are_preserved()
    {
        var route = Route((0, 0), (0, 0), (10, 0), (10, 10), (20, 10), (20, 20));
        var result = Simplify(route);
        var simplified = Assert.Single(result.Scene.Routes);

        Assert.Equal(new[] { (0d, 0d), (10d, 0d), (10d, 10d), (20d, 10d), (20d, 20d) },
            simplified.Points.Select(point => (point.X, point.Y)));
        Assert.Equal(1, Assert.Single(result.Evidence).RedundantPointsRemoved);
        Assert.Equal(4, simplified.Segments.Count);
    }

    [Fact]
    public void Endpoint_handoff_duplicates_and_returns_collapse_to_one_final_monotonic_run()
    {
        var route = RouteWithResources(new[] {
            (0d, 0d), (10d, 0d), (10d, 0d), (5d, 0d), (5d, 10d) },
            new[] { "handoff:source", "handoff:source", "handoff:destination", "handoff:destination" });

        var result = Simplify(route);
        var simplified = Assert.Single(result.Scene.Routes);

        Assert.Equal(new[] { (0d, 0d), (5d, 0d), (5d, 10d) }, simplified.Points.Select(point => (point.X, point.Y)));
        Assert.Equal(0, CountSameAxisReversals(simplified));
        Assert.Contains("canonical-simplification", simplified.Segments[0].AllocationProvenance);
        Assert.Contains("handoff:source", simplified.Segments[0].AllocationProvenance);
        Assert.Contains("handoff:destination", simplified.Segments[0].AllocationProvenance);
    }

    [Fact]
    public void One_hundred_redundant_same_axis_points_compile_to_the_canonical_route_shape()
    {
        var points = Enumerable.Range(0, 100).Select(index => (X: (double)index, Y: 0d)).ToArray();
        var result = Simplify(Route(points));
        var simplified = Assert.Single(result.Scene.Routes);

        Assert.Equal(new[] { (0d, 0d), (99d, 0d) },
            simplified.Points.Select(point => (point.X, point.Y)));
        Assert.Single(simplified.Segments);
        Assert.Equal(100, Assert.Single(result.Evidence).PointsBefore);
        Assert.Equal(2, Assert.Single(result.Evidence).PointsAfter);
        Assert.True(Assert.Single(result.Evidence).Simplified);
    }

    private static ArchitectureV7PhysicalSceneFreeze Scene(ArchitectureV7PhysicalRoute route) =>
        new(Array.Empty<ArchitectureV7PhysicalTrackDimension>(), Array.Empty<ArchitectureV7PhysicalTrackDimension>(),
            Array.Empty<ArchitectureV7PhysicalSceneNode>(), Array.Empty<ArchitectureV7PhysicalTerminal>(), new[] { route },
            Array.Empty<ArchitectureV7PhysicalSceneDiagnostic>(), "placement", "route", "allocation", "scene");

    private static ArchitectureV7RouteSimplificationResult Simplify(ArchitectureV7PhysicalRoute route) =>
        new ArchitectureV7AllocatedRouteSimplificationStage().Simplify(Scene(route));

    private static ArchitectureV7PhysicalRoute Route(params (double X, double Y)[] points) =>
        Route(points, null);

    private static ArchitectureV7PhysicalRoute Route((double X, double Y)[] points, string[]? laneBySegment)
    {
        var physicalPoints = points.Select((point, index) => new ArchitectureV7PhysicalPoint(point.X, point.Y, "point-" + index)).ToArray();
        var segments = Enumerable.Range(0, Math.Max(0, points.Length - 1)).Select(index =>
        {
            var lane = laneBySegment is not null && index < laneBySegment.Length ? laneBySegment[index] :
                points[index].Y == points[index + 1].Y ? "h0" : "v0";
            var cells = new[] { new ArchitectureV7RouteCell(index, index), new ArchitectureV7RouteCell(index + 1, index + 1) };
            return new ArchitectureV7PhysicalSegment("link", physicalPoints[index], physicalPoints[index + 1],
                new[] { index, index + 1 }, cells, "run-" + index, lane, "allocation-resource-" + index);
        }).ToArray();
        return new ArchitectureV7PhysicalRoute("link", physicalPoints, segments, "source-route");
    }

    private static ArchitectureV7PhysicalRoute RouteWithResources((double X, double Y)[] points, string[] resources)
    {
        var physicalPoints = points.Select((point, index) => new ArchitectureV7PhysicalPoint(point.X, point.Y, "point-" + index)).ToArray();
        var segments = Enumerable.Range(0, points.Length - 1).Select(index =>
            new ArchitectureV7PhysicalSegment("link", physicalPoints[index], physicalPoints[index + 1],
                new[] { index, index + 1 }, new[] { new ArchitectureV7RouteCell(index, index), new ArchitectureV7RouteCell(index + 1, index + 1) },
                resources[index], resources[index], "resource=" + resources[index] + ";lane=" + resources[index])).ToArray();
        return new ArchitectureV7PhysicalRoute("link", physicalPoints, segments, "source-route");
    }

    private static int CountSameAxisReversals(ArchitectureV7PhysicalRoute route) =>
        route.Points.Zip(route.Points.Skip(1), (a, b) => (a, b)).Zip(route.Points.Skip(2), (pair, c) => (pair.a, pair.b, c))
            .Count(item => (item.a.Y == item.b.Y && item.b.Y == item.c.Y && Math.Sign(item.b.X - item.a.X) != Math.Sign(item.c.X - item.b.X)) ||
                (item.a.X == item.b.X && item.b.X == item.c.X && Math.Sign(item.b.Y - item.a.Y) != Math.Sign(item.c.Y - item.b.Y)));
}
