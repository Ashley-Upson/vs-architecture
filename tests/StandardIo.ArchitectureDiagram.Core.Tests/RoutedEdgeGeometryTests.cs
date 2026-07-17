using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class RoutedEdgeGeometryTests
{
    [Fact]
    public void Constructor_copies_points_and_precomputes_segments()
    {
        var points = new[]
        {
            new Point(10, 20),
            new Point(10, 40),
            new Point(50, 40)
        };
        var geometry = new RoutedEdgeGeometry(points);

        points[1] = new Point(999, 999);

        Assert.Equal(new Point(10, 40), geometry.Points[1]);
        Assert.Equal(2, geometry.Segments.Count);
        Assert.Equal(new Segment(new Point(10, 20), new Point(10, 40)), geometry.Segments[0]);
        Assert.Equal(new Segment(new Point(10, 40), new Point(50, 40)), geometry.Segments[1]);
    }
}
