// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectModelOrchestrationTests
{
    private static readonly string[] expectedStageOrder = new[]
    {
        "resolve",
        "types",
        "dependencies"
    };
    [Fact]
    public async Task ShouldResolveThenCoordinateTheTwoModelStacksAsync()
    {
        // Given: the fixture and inputs below.
        var fixture = new Pipeline();
        using var cancellation = new CancellationTokenSource();
        fixture.Token = cancellation.Token;
        // When: exercise the operation under test.

        ProjectModel result = await fixture.CreateService()
            .GenerateProjectModelAsync(projectFilePath: "supplied-path", cancellationToken: cancellation.Token);
        // Then: verify the resulting contract.

        Assert.Equal(expected: expectedStageOrder, actual: fixture.Calls);
        Assert.Equal(expected: "resolved", actual: result.Name);
        Assert.Equal(expected: "resolved.csproj", actual: result.Path);
        Assert.Same(expected: fixture.Project, actual: result);
        Assert.Same(expected: fixture.Types, actual: result.Types);
        Assert.Same(expected: fixture.Dependencies, actual: result.Dependencies);
    }

    [Theory]
    [InlineData("resolve", 1)]
    [InlineData("dependencies", 3)]
    [InlineData("types", 2)]
    public async Task ShouldStopAndPropagateFailuresFromEachStageAsync(string failureStage, int expectedCalls)
    {
        // Given: the fixture and inputs below.
        var fixture = new Pipeline
        {
            FailureStage = failureStage
        };
        // When: exercise the operation under test.

        Exception failure = await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => fixture.CreateService()
            .GenerateProjectModelAsync(projectFilePath: "supplied-path"));
        // Then: verify the resulting contract.

        Assert.Same(expected: fixture.Failure, actual: failure);
        Assert.Equal(expected: expectedCalls, actual: fixture.Calls.Count);
    }

    private sealed class Pipeline : IProjectProcessingService, IProjectTypesProcessingService, IProjectDependenciesProcessingService
    {
        public List<string> Calls { get; } = new();
        public CancellationToken Token { get; set; }
        public string? FailureStage { get; set; }
        public Exception Failure { get; } = new InvalidOperationException("stage failed");
        public DefinedType[] Types { get; } = new[]
        {
            new DefinedType
            {
                Name = "Expected"
            }
        };
        public ProjectModel? Project { get; private set; }
        public TypeRelationship[] Dependencies { get; } = new[]
        {
            new TypeRelationship
            {
                FromType = "Expected",
                ToType = "Target"
            }
        };

        public ProjectModelOrchestrationService CreateService() =>
            new(this, this, this);

        public string ResolveProjectFilePath(string suppliedPath)
        {
            Assert.Equal(expected: "supplied-path", actual: suppliedPath);
            Record(stage: "resolve");
            return "resolved.csproj";
        }

        public Task PopulateTypesAsync(ProjectModel project, CancellationToken cancellationToken)
        {
            Assert.Equal(expected: "resolved.csproj", actual: project.Path);
            Assert.Equal(expected: Token, actual: cancellationToken);
            Assert.Empty(collection: project.Types!);
            Assert.Empty(collection: project.Dependencies!);
            Project = project;
            Record(stage: "types");
            project.Types = Types;
            return Task.CompletedTask;
        }

        public Task PopulateDependenciesAsync(ProjectModel project, CancellationToken cancellationToken)
        {
            Assert.Same(expected: Project, actual: project);
            Assert.Same(expected: Types, actual: project.Types);
            Assert.Equal(expected: Token, actual: cancellationToken);
            Record(stage: "dependencies");
            project.Dependencies = Dependencies;
            return Task.CompletedTask;
        }

        private void Record(string stage)
        {
            Calls.Add(item: stage);

            if (FailureStage == stage)
            {
                throw Failure;
            }
        }
    }
}