using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CanonicalSharedNodeCorridorPipelineTests
{
    [Fact]
    public void Safe_long_exterior_route_remains_safe_through_corridor_pipeline()
    {
        var nodes = Nodes();
        var selected = new LinkLayout(
            new RenderLink("long_edge", "later_parent", "canonical_target", "internal", 0),
            new Point(1000, 1000),
            new Point(100, 200),
            new[]
            {
                new Point(1000, 1020),
                new Point(1400, 1020),
                new Point(1400, 300),
                new Point(100, 300),
                new Point(100, 220)
            },
            0.5,
            0.5);
        var links = new Dictionary<string, LinkLayout> { [selected.Link.Id] = selected };

        AssertSafe(selected, nodes);

        var observation = CorridorObserver.Observe(nodes, links, 12, 10);
        var mappings = observation.SegmentMappings
            .Where(mapping => mapping.EdgeId == selected.Link.Id)
            .OrderBy(mapping => mapping.SegmentIndex)
            .ToArray();
        Assert.NotEmpty(mappings);
        Assert.All(mappings, mapping => Assert.Equal(
            CompleteSegments(selected)[mapping.SegmentIndex],
            mapping.Segment));

        var allocation = CorridorLaneAllocator.Allocate(observation);
        Assert.Empty(allocation.FailedCorridorIds);
        Assert.All(mappings, mapping => Assert.True(
            allocation.TryGetLane(mapping.CorridorId, selected.Link.Id, out _)));

        var compiled = CorridorLaneGeometryCompiler.Compile(links, observation, allocation)[selected.Link.Id];
        AssertSafe(compiled, nodes);

        var traversal = EdgeTraversalCompiler.Compile(
            new Dictionary<string, LinkLayout> { [compiled.Link.Id] = compiled },
            observation,
            allocation);
        var final = EdgeTraversalCompiler.Apply(
            new Dictionary<string, LinkLayout> { [compiled.Link.Id] = compiled },
            traversal)[compiled.Link.Id];
        AssertSafe(final, nodes);
    }

    private static Dictionary<string, NodeLayout> Nodes()
    {
        var result = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["later_parent"] = Node("later_parent", new Rect(940, 940, 120, 60)),
            ["canonical_target"] = Node("canonical_target", new Rect(40, 140, 120, 60))
        };
        for (var index = 0; index < 6; index++)
        {
            result[$"upper_{index}"] = Node($"upper_{index}", new Rect(220 + index * 150, 400, 100, 80));
            result[$"lower_{index}"] = Node($"lower_{index}", new Rect(220 + index * 150, 760, 100, 80));
        }

        return result;
    }

    private static void AssertSafe(LinkLayout link, IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var obstacles = nodes
            .Where(item => item.Key != link.Link.SourceId && item.Key != link.Link.TargetId)
            .Select(item => item.Value.Rect)
            .ToArray();
        Assert.DoesNotContain(
            CompleteSegments(link),
            segment => obstacles.Any(segment.Intersects));
    }

    private static Segment[] CompleteSegments(LinkLayout link)
    {
        var points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
        return points.Zip(points.Skip(1), (start, end) => new Segment(start, end)).ToArray();
    }

    private static NodeLayout Node(string id, Rect rect) =>
        new(
            new RenderNode(
                id,
                "project",
                id,
                $"Fixture.{id}",
                "Class",
                false,
                string.Empty,
                0,
                Array.Empty<string>(),
                Array.Empty<TypeProperty>(),
                0),
            rect,
            0,
            false);
}
