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

public sealed class ProjectModelOrchestrationTests
{
    [Fact]
    public async Task ShouldResolveThenCoordinateTheTwoModelStacksAsync()
    {
        var fixture = new Pipeline();
        using var cancellation = new CancellationTokenSource();
        fixture.Token = cancellation.Token;

        ProjectModel result = await fixture.CreateService().GenerateProjectModelAsync("supplied-path", cancellation.Token);

        Assert.Equal(new[] { "resolve", "types", "dependencies" }, fixture.Calls);
        Assert.Equal("resolved", result.Name);
        Assert.Equal("resolved.csproj", result.Path);
        Assert.Same(fixture.Project, result);
        Assert.Same(fixture.Types, result.Types);
        Assert.Same(fixture.Dependencies, result.Dependencies);
    }

    [Theory]
    [InlineData("resolve", 1)]
    [InlineData("dependencies", 3)]
    [InlineData("types", 2)]
    public async Task ShouldStopAndPropagateFailuresFromEachStageAsync(string failureStage, int expectedCalls)
    {
        var fixture = new Pipeline { FailureStage = failureStage };

        Exception failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.CreateService().GenerateProjectModelAsync("supplied-path"));

        Assert.Same(fixture.Failure, failure);
        Assert.Equal(expectedCalls, fixture.Calls.Count);
    }

    private sealed class Pipeline : IProjectProcessingService, IProjectTypesProcessingService, IProjectDependenciesProcessingService
    {
        public List<string> Calls { get; } = new();
        public CancellationToken Token { get; set; }
        public string? FailureStage { get; set; }
        public Exception Failure { get; } = new InvalidOperationException("stage failed");
        public DefinedType[] Types { get; } = new[] { new DefinedType { Name = "Expected" } };
        public ProjectModel? Project { get; private set; }
        public Dependency[] Dependencies { get; } = new[] { new Dependency { FromType = "Expected", ToType = "Target" } };

        public ProjectModelOrchestrationService CreateService() => new(this, this, this);

        public string ResolveProjectFilePath(string suppliedPath)
        {
            Assert.Equal("supplied-path", suppliedPath);
            Record("resolve");
            return "resolved.csproj";
        }

        public Task PopulateTypesAsync(ProjectModel project, CancellationToken cancellationToken)
        {
            Assert.Equal("resolved.csproj", project.Path);
            Assert.Equal(Token, cancellationToken);
            Assert.Empty(project.Types!);
            Assert.Empty(project.Dependencies!);
            Project = project;
            Record("types");
            project.Types = Types;
            return Task.CompletedTask;
        }

        public Task PopulateDependenciesAsync(ProjectModel project, CancellationToken cancellationToken)
        {
            Assert.Same(Project, project);
            Assert.Same(Types, project.Types);
            Assert.Equal(Token, cancellationToken);
            Record("dependencies");
            project.Dependencies = Dependencies;
            return Task.CompletedTask;
        }

        private void Record(string stage)
        {
            Calls.Add(stage);
            if (FailureStage == stage) throw Failure;
        }
    }
}
