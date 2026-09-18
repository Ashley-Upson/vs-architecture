// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class ProjectModelTreeCacheTests
{
    [Fact]
    public void Split_WhenProjectWasAlreadySplit_ReusesProcessTrees()
    {
        // Given
        var projectModel = new ProjectModel { Name = "Trees" };
        var broker = new CountingProjectModelSplitterBroker();
        var firstService = new ProjectModelTreeService(broker: broker);
        var secondService = new ProjectModelTreeService(broker: broker);

        // When
        ProjectModel[] first = firstService.Split(projectModel: projectModel);
        ProjectModel[] second = secondService.Split(projectModel: projectModel);

        // Then
        Assert.Equal(expected: 1, actual: broker.SplitCount);
        Assert.Same(expected: first, actual: second);
    }

    private sealed class CountingProjectModelSplitterBroker : IProjectModelSplitterBroker
    {
        public int SplitCount { get; private set; }

        public ProjectModel[] Split(ProjectModel projectModel)
        {
            SplitCount++;
            return [new ProjectModel { Name = projectModel.Name }];
        }
    }
}
