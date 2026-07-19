using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectLabelGeometryMeasurerTests
{
    [Fact]
    public void Measures_only_rendered_text_and_clearance_not_the_complete_title_bar()
    {
        var project = new ProjectLayout(
            new RenderProject("project", "Short", 0), new Rect(-200, -80, 800, 400));

        var result = ProjectLabelGeometryMeasurer.Measure(
            new Dictionary<string, ProjectLayout> { ["project"] = project }, 34, 10)["project"];

        Assert.Equal(project.Rect, result.ProjectBounds);
        Assert.Equal(40, result.ProjectLabelTextBounds.Width);
        Assert.Equal(new Rect(180, -80, 40, 34), result.ProjectLabelTextBounds);
        Assert.Equal(new Rect(170, -90, 60, 54), result.ProjectLabelObstacleBounds);
        Assert.True(new Segment(new Point(-150, -70), new Point(100, -70)).IsHorizontal);
        Assert.False(new Segment(new Point(-150, -70), new Point(100, -70))
            .Intersects(result.ProjectLabelObstacleBounds));
        Assert.True(new Segment(new Point(180, -100), new Point(180, 0))
            .Intersects(result.ProjectLabelObstacleBounds));
    }

    [Fact]
    public void Long_label_deterministically_reduces_unused_header_space()
    {
        var projects = new Dictionary<string, ProjectLayout>
        {
            ["project"] = new(new RenderProject("project", new string('W', 40), 0), new Rect(0, 0, 500, 300))
        };

        var first = ProjectLabelGeometryMeasurer.Measure(projects, 34, 8)["project"];
        var second = ProjectLabelGeometryMeasurer.Measure(projects, 34, 8)["project"];

        Assert.Equal(first, second);
        Assert.Equal(320, first.ProjectLabelTextBounds.Width);
        Assert.Equal(new Rect(82, -8, 336, 50), first.ProjectLabelObstacleBounds);
    }
}
