using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CommonRailDevelopmentFixtureTests
{
    [Fact]
    public void Real_extracted_component_reduces_hard_findings_without_legacy_repair()
    {
        var result = CommonRailDevelopmentFixture.Build();

        Assert.Equal(1, result.BeforeDefects.SharedSegment);
        Assert.Equal(1, result.BeforeDefects.ParallelSpacing);
        Assert.Equal(1, result.BeforeDefects.ReusedBend);
        Assert.Equal(0, result.AfterDefects.SharedSegment);
        Assert.Equal(0, result.AfterDefects.ParallelSpacing);
        Assert.Equal(0, result.AfterDefects.ReusedBend);
        Assert.Equal(0, result.AfterDefects.NodeCollision);
        Assert.Equal(0, result.AfterDefects.NonOrthogonalSegment);
        Assert.Equal(0, result.AfterDefects.ImmediateReversal);
        Assert.False(result.RouteRepairCoordinatorRan);
        Assert.False(result.SeparateOverlappingCornersRan);
        Assert.False(result.TraversalFallbackRan);
    }

    [Fact]
    public void Real_extracted_component_is_complete_deterministic_and_preserves_dependencies()
    {
        var first = CommonRailDevelopmentFixture.Build();
        var second = CommonRailDevelopmentFixture.Build();

        Assert.Equal(first.BeforeDrawio, second.BeforeDrawio);
        Assert.Equal(first.AfterDrawio, second.AfterDrawio);
        Assert.Equal(3, first.RoutesRegenerated);
        Assert.Equal(3, first.RailsAssigned);
        Assert.Equal(6, first.TurnsAssigned);
        Assert.Equal(3, first.Invalidations.Count);
        Assert.All(first.Invalidations, item => Assert.Equal(RouteInvalidationCause.AssignedRailChanged, item.Cause));
        Assert.Equal(0, first.NodesMoved);
        Assert.Equal(0, first.LayersMoved);
        Assert.Equal(0, first.SpaceAdded);
        Assert.Equal(3, XDocument.Parse(first.AfterDrawio).Descendants("mxCell").Count(item => item.Attribute("edge")?.Value == "1"));
    }
}
