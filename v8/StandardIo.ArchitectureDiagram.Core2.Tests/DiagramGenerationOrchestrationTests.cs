// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class DiagramGenerationOrchestrationTests
{
    private static readonly string[] singleProjectPaths =
    {
        "one"
    };
    private static readonly string[] expectedStageOrder = new[]
    {
        "build:one",
        "split:one",
        "build:two",
        "split:two",
        "render"
    };
    [Fact]
    public async Task ShouldBypassSplittingWhenNoDuplicatesIsRequestedAsync()
    {
        // Given
        var stack = new Stack { Failure = "split" };
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        var request = parser.Parse(command: new[] { "Architecture", "one.csproj", "two.csproj", "--noduplicates", "-o", "output" });
        // When
        await stack.GenerateAsync(request: request, token: CancellationToken.None);
        // Then
        Assert.Equal(expected: new[] { "build:" + request.ProjectPaths[0], "build:" + request.ProjectPaths[1], "render" }, actual: stack.Calls);
        Assert.Equal(expected: request.ProjectPaths, actual: stack.Rendered!.Select(selector: project => project.Path));
    }

    [Fact]
    public async Task ShouldBuildAndSplitEachProjectThenRenderAllTreesOnceAsync()
    {
        // Given
        var stack = new Stack();
        using var cancellation = new CancellationTokenSource();

        var request = new DiagramRenderRequest
        {
            ProjectPaths = new[]
            {
                "one",
                "two"
            }
        };
        // When

        byte[] result = await stack.GenerateAsync(request: request, token: cancellation.Token);
        // Then
        Assert.Same(expected: stack.Bytes, actual: result);
        Assert.Equal(expected: expectedStageOrder, actual: stack.Calls);
        Assert.Equal(expected: cancellation.Token, actual: stack.Token);
        Assert.Equal(expected: 4, actual: stack.Rendered!.Length);

        for (int i = 0; i < 4; i++)
        {
            Assert.Same(expected: stack.Trees[i], actual: stack.Rendered[i]);
        }
    }

    [Theory]
    [InlineData("build")]
    [InlineData("split")]
    [InlineData("render")]
    public async Task ShouldPropagateStageFailuresWithoutContinuingAsync(string stage)
    {
        // Given
        var stack = new Stack
        {
            Failure = stage
        };
        // When / Then

        var error = await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => stack.GenerateAsync(request: new DiagramRenderRequest { ProjectPaths = singleProjectPaths }, token: CancellationToken.None));
        // Then: verify the resulting contract.
        Assert.Same(expected: stack.Error, actual: error);
        Assert.Equal(expected: stage == "build" ? 1 : stage == "split" ? 2 : 3, actual: stack.Calls.Count);
    }

    [Fact]
    public async Task ShouldRejectInvalidRequestsAndCancellationBeforeBuildingAsync()
    {
        // Given
        var stack = new Stack();
        // When / Then
        await Assert.ThrowsAsync<ArgumentNullException>(testCode: () => stack.GenerateAsync(request: null !, token: default));
        // Then: verify the resulting contract.
        await Assert.ThrowsAsync<ArgumentException>(testCode: () => stack.GenerateAsync(request: new(), token: default));
        await Assert.ThrowsAsync<ArgumentException>(testCode: () => stack.GenerateAsync(request: new() { ProjectPaths = null ! }, token: default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(testCode: () => stack.GenerateAsync(request: new(), token: new CancellationToken(true)));
        Assert.Empty(collection: stack.Calls);
    }

    private sealed class Stack : IProjectModelBuilderService, IProjectModelTreeService, IDiagramRenderer
    {
        public List<string> Calls { get; } = new();
        public List<ProjectModel> Trees { get; } = new();
        public ProjectModel[]? Rendered { get; private set; }
        public CancellationToken Token { get; private set; }
        public byte[] Bytes { get; } = new byte[]
        {
            1,
            2,
            3
        };
        public string? Failure { get; init; }
        public InvalidOperationException Error { get; } = new("stage failure");

        public Task<byte[]> GenerateAsync(DiagramRenderRequest request, CancellationToken token) =>
            new DiagramGenerationOrchestrationService(this, this, this).GenerateAsync(request: request, cancellationToken: token);

        public Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken)
        {
            Calls.Add(item: "build:" + projectFilePath);
            Token = cancellationToken;

            if (Failure == "build")
            {
                throw Error;
            }

            return Task.FromResult(result: new ProjectModel { Path = projectFilePath });
        }

        public ProjectModel[] Split(ProjectModel projectModel)
        {
            Calls.Add(item: "split:" + projectModel.Path);

            if (Failure == "split")
            {
                throw Error;
            }

            var trees = new[]
            {
                new ProjectModel(),
                new ProjectModel()
            };

            Trees.AddRange(collection: trees);
            return trees;
        }

        public byte[] Render(RenderModel renderModel)
        {
            Calls.Add(item: "render");

            if (Failure == "render")
            {
                throw Error;
            }

            Rendered = renderModel.ProjectModels;
            return Bytes;
        }
    }
}