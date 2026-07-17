using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class TraceabilityValidatorTests
{
    [Fact]
    public void ThrowIfInvalid_prevents_invalid_geometry_from_reaching_serialization()
    {
        var result = new TraceabilityValidationResult(new[]
        {
            new TraceabilityViolation(
                TraceabilityViolationCode.SharedSegment,
                "edge_a",
                "edge_b",
                40,
                "Edges share 40px of route.")
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TraceabilityValidator.ThrowIfInvalid(result));

        Assert.Contains("1 violation", exception.Message);
        Assert.Contains("share 40px", exception.Message);
    }

    [Fact]
    public void Validate_reports_shared_segments_spacing_and_reused_bends()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge_a"] = Link("edge_a", 0, new Point(20, 20), new Point(100, 100), new[]
            {
                new Point(20, 60),
                new Point(100, 60)
            }),
            ["edge_b"] = Link("edge_b", 1, new Point(20, 30), new Point(100, 110), new[]
            {
                new Point(20, 60),
                new Point(100, 60)
            })
        };

        var result = TraceabilityValidator.Validate(
            new Dictionary<string, NodeLayout>(),
            links,
            requiredParallelSpacing: 12);

        Assert.Contains(result.Violations, violation =>
            violation.Code == TraceabilityViolationCode.SharedSegment && violation.Magnitude > 0);
        Assert.Contains(result.Violations, violation =>
            violation.Code == TraceabilityViolationCode.ReusedBend && violation.Magnitude == 2);
    }

    [Fact]
    public void Validate_reports_node_collisions()
    {
        var node = new RenderNode(
            "obstacle",
            null,
            "Obstacle",
            "Obstacle",
            "Class",
            false,
            string.Empty,
            0,
            Array.Empty<string>(),
            Array.Empty<StandardIo.ArchitectureDiagram.Core.Models.TypeProperty>(),
            0);
        var nodes = new Dictionary<string, NodeLayout>
        {
            [node.Id] = new NodeLayout(node, new Rect(40, 40, 40, 40), 0, false)
        };
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge"] = Link("edge", 0, new Point(20, 60), new Point(100, 60), Array.Empty<Point>())
        };

        var result = TraceabilityValidator.Validate(nodes, links, 12);

        Assert.Contains(result.Violations, violation =>
            violation.Code == TraceabilityViolationCode.NodeCollision);
    }

    [Fact]
    public void Validate_accepts_separated_parallel_routes()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge_a"] = Link("edge_a", 0, new Point(0, 20), new Point(100, 20), Array.Empty<Point>()),
            ["edge_b"] = Link("edge_b", 1, new Point(0, 40), new Point(100, 40), Array.Empty<Point>())
        };

        var result = TraceabilityValidator.Validate(
            new Dictionary<string, NodeLayout>(),
            links,
            requiredParallelSpacing: 12);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_reports_same_axis_immediate_reversal()
    {
        var links = new Dictionary<string, LinkLayout>
        {
            ["edge"] = Link("edge", 0, new Point(20, 20), new Point(20, 100), new[]
            {
                new Point(20, 120),
                new Point(20, 80)
            })
        };

        var result = TraceabilityValidator.Validate(
            new Dictionary<string, NodeLayout>(),
            links,
            requiredParallelSpacing: 12);

        Assert.Contains(result.Violations, violation =>
            violation.Code == TraceabilityViolationCode.ImmediateReversal);
    }

    private static LinkLayout Link(
        string id,
        int order,
        Point source,
        Point target,
        IEnumerable<Point> points) =>
        new(
            new RenderLink(id, $"{id}_source", $"{id}_target", "internal", order),
            source,
            target,
            points,
            0.5,
            0.5);
}
