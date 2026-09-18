// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class ProjectModelBuilderCacheTests
{
    [Fact]
    public async Task BuildAsync_WhenProjectWasAlreadyRequested_ReusesProcessModelAsync()
    {
        // Given
        string projectPath = Path.Combine(
            path1: Path.GetTempPath(),
            path2: $"architecture-cache-{Guid.NewGuid():N}.csproj");

        var broker = new CountingProjectModelBroker();
        var firstService = new ProjectModelBuilderService(broker: broker);
        var secondService = new ProjectModelBuilderService(broker: broker);

        // When
        ProjectModel[] models = await Task.WhenAll(
            firstService.BuildAsync(
                projectFilePath: projectPath,
                cancellationToken: CancellationToken.None),
            secondService.BuildAsync(
                projectFilePath: projectPath,
                cancellationToken: CancellationToken.None));

        // Then
        Assert.Equal(expected: 1, actual: broker.BuildCount);
        Assert.Same(expected: models[0], actual: models[1]);
    }

    private sealed class CountingProjectModelBroker : IProjectModelBroker
    {
        private int buildCount;

        public int BuildCount => buildCount;

        public async Task<ProjectModel> BuildAsync(
            string projectFilePath,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(location: ref buildCount);
            await Task.Yield();

            return new ProjectModel
            {
                Name = Path.GetFileNameWithoutExtension(path: projectFilePath),
                Path = projectFilePath,
                Types = [],
                Dependencies = []
            };
        }
    }
}
