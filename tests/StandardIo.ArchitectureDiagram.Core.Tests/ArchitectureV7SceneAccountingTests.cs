using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7SceneAccountingTests
{
    [Fact]
    public void Incomplete_scene_keeps_failed_relationship_identity_and_is_not_complete()
    {
        var compiled = new ArchitectureV7PhysicalRoute("link-compiled",
            new[] { new ArchitectureV7PhysicalPoint(0, 0, "test"), new ArchitectureV7PhysicalPoint(0, 10, "test") },
            new[] { new ArchitectureV7PhysicalSegment("link-compiled",
                new ArchitectureV7PhysicalPoint(0, 0, "test"), new ArchitectureV7PhysicalPoint(0, 10, "test"),
                new[] { 0, 1 }, new[] { new ArchitectureV7RouteCell(0, 0), new ArchitectureV7RouteCell(1, 0) },
                "run-compiled", "lane-compiled", "test") }, "test");
        var scene = new ArchitectureV7PhysicalSceneFreeze(
            new List<ArchitectureV7PhysicalTrackDimension>(), new List<ArchitectureV7PhysicalTrackDimension>(),
            new List<ArchitectureV7PhysicalSceneNode>(), new List<ArchitectureV7PhysicalTerminal>(),
            new[] { compiled }, new[] { new ArchitectureV7PhysicalSceneDiagnostic("ROUTE-COMPILATION-FAILED", "test", true, "link-failed") },
            "placement", "routes", "allocation", "scene", new[] { "link-compiled", "link-failed" });

        Assert.Equal(new[] { "link-compiled" }, scene.CompiledPhysicalLinkIds);
        Assert.Equal(new[] { "link-failed" }, scene.FailedPhysicalLinkIds);
        Assert.True(scene.RelationshipAccountingInvariant);
        Assert.False(scene.IsComplete);
        Assert.Equal(2, scene.CompiledPhysicalLinkIds.Count + scene.FailedPhysicalLinkIds.Count);
    }
}
