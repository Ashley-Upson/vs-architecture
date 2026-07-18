using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class OrthogonalGeometryTests
{
    [Fact]
    public void AxisInterval_distinguishes_closed_contact_from_positive_length_overlap()
    {
        var left = new AxisInterval(0, 10);
        var touching = new AxisInterval(10, 20);

        Assert.True(left.ClosedIntersects(touching));
        Assert.Equal(0, left.PositiveLengthOverlap(touching));
        Assert.Equal(5, left.PositiveLengthOverlap(new AxisInterval(5, 20)));
    }

    [Fact]
    public void Segment_contains_endpoints_and_intersects_only_rectangle_interior()
    {
        var segment = new Segment(new Point(0, 10), new Point(20, 10));

        Assert.True(segment.ContainsPointClosed(new Point(0, 10)));
        Assert.True(segment.ContainsPointClosed(new Point(20, 10)));
        Assert.True(segment.Intersects(new Rect(5, 5, 10, 10)));
        Assert.False(new Segment(new Point(0, 5), new Point(20, 5)).Intersects(new Rect(5, 5, 10, 10)));
    }

    [Fact]
    public void Polyline_bounds_and_translation_are_deterministic()
    {
        var points = new[] { new Point(-10, 20), new Point(30, 20), new Point(30, 80) };

        Assert.Equal(new Rect(-10, 20, 40, 60), PolylineGeometry.Bounds(points));
        Assert.Equal(
            new[] { new Point(0, 15), new Point(40, 15), new Point(40, 75) },
            PolylineGeometry.Translate(points, 10, -5));
    }
}
