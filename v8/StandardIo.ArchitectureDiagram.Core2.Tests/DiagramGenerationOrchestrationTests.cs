using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class DiagramGenerationOrchestrationTests
{
    [Fact]
    public async Task ShouldBuildAndSplitEachProjectThenRenderAllTreesOnceAsync()
    {
        // Given
        var stack = new Stack();
        using var cancellation = new CancellationTokenSource();
        var request = new DiagramGenerationRequest { ProjectPaths = new[] { "one", "two" } };
        // When
        byte[] result = await stack.GenerateAsync(request, cancellation.Token);
        // Then
        Assert.Same(stack.Bytes, result);
        Assert.Equal(new[] { "build:one", "split:one", "build:two", "split:two", "render" }, stack.Calls);
        Assert.Equal(cancellation.Token, stack.Token);
        Assert.Equal(4, stack.Rendered!.Length);
        for (int i = 0; i < 4; i++) Assert.Same(stack.Trees[i], stack.Rendered[i]);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("split")]
    [InlineData("render")]
    public async Task ShouldPropagateStageFailuresWithoutContinuingAsync(string stage)
    {
        // Given
        var stack = new Stack { Failure = stage };
        // When / Then
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => stack.GenerateAsync(
            new DiagramGenerationRequest { ProjectPaths = new[] { "one" } }, CancellationToken.None));
        Assert.Same(stack.Error, error);
        Assert.Equal(stage == "build" ? 1 : stage == "split" ? 2 : 3, stack.Calls.Count);
    }

    [Fact]
    public async Task ShouldRejectInvalidRequestsAndCancellationBeforeBuildingAsync()
    {
        // Given
        var stack = new Stack();
        // When / Then
        await Assert.ThrowsAsync<ArgumentNullException>(() => stack.GenerateAsync(null!, default));
        await Assert.ThrowsAsync<ArgumentException>(() => stack.GenerateAsync(new(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => stack.GenerateAsync(new() { ProjectPaths = null! }, default));
        await Assert.ThrowsAsync<NotSupportedException>(() => stack.GenerateAsync(new() { DiagramType = (DiagramTypes)99 }, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stack.GenerateAsync(new(), new CancellationToken(true)));
        Assert.Empty(stack.Calls);
    }

    private sealed class Stack : IProjectModelBuilderService, IProjectModelTreeService, IDiagramRenderer
    {
        public List<string> Calls { get; } = new();
        public List<ProjectModel> Trees { get; } = new();
        public ProjectModel[]? Rendered { get; private set; }
        public CancellationToken Token { get; private set; }
        public byte[] Bytes { get; } = new byte[] { 1, 2, 3 };
        public string? Failure { get; init; }
        public InvalidOperationException Error { get; } = new("stage failure");
        public DiagramRendererRule Rule { get; } = new("test", ".test");
        public Task<byte[]> GenerateAsync(DiagramGenerationRequest request, CancellationToken token) => new DiagramGenerationOrchestrationService(this, this).GenerateAsync(request, token, this);
        public Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken)
        {
            Calls.Add("build:" + projectFilePath);
            Token = cancellationToken;
            if (Failure == "build") throw Error;
            return Task.FromResult(new ProjectModel { Path = projectFilePath });
        }
        public ProjectModel[] Split(ProjectModel projectModel)
        {
            Calls.Add("split:" + projectModel.Path);
            if (Failure == "split") throw Error;
            var trees = new[] { new ProjectModel(), new ProjectModel() };
            Trees.AddRange(trees);
            return trees;
        }
        public byte[] Render(ProjectModel[] projectModels)
        {
            Calls.Add("render");
            if (Failure == "render") throw Error;
            Rendered = projectModels;
            return Bytes;
        }
    }
}
