// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
using Xunit;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class KeyedGenerationTests
{
    [Fact]
    public void ShouldKeepCommandOrchestrationAboveItsParsingRenderingAndExportProcessingServices()
    {
        // Given
        var type = typeof(StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands.DiagramRenderOrchestrationService);
        // When
        var dependencies = Assert.Single(collection: type.GetConstructors())
            .GetParameters()
            .Select(selector: parameter => parameter.ParameterType.Name)
            .ToArray();
        // Then
        Assert.Equal(
            expected: new[]
            {
                "ICommandParserProcessingService",
                "IDiagramRequestProcessingService",
                "IRawJsonExportProcessingService",
            },
            actual: dependencies);
    }

    [Fact]
    public void ShouldHaveFactoryCreateTheGenerationOrchestration()
    {
        // Given
        var method = Assert.Single(collection: typeof(IDiagramRendererFactory).GetMethods());
        // When
        Type result = method.ReturnType;
        // Then
        Assert.Equal(expected: typeof(IDiagramGenerationOrchestrationService), actual: result);
        Assert.Equal(expected: typeof(string), actual: Assert.Single(collection: method.GetParameters()).ParameterType);
    }

    [Theory]
    [InlineData(typeof(DiagramRequestProcessingService), typeof(IDiagramRequestService))]
    [InlineData(typeof(DiagramRequestBroker), typeof(IDiagramRendererFactory))]
    public void ShouldKeepEachRequestLayerOnItsSingleDependency(Type serviceType, Type dependencyType)
    {
        // Given
        var constructor = Assert.Single(collection: serviceType.GetConstructors());
        // When
        var dependency = Assert.Single(collection: constructor.GetParameters());
        // Then
        Assert.Equal(expected: dependencyType, actual: dependency.ParameterType);
    }

    [Theory]
    [InlineData("DrawIO_Architecture")]
    [InlineData("Html_Architecture")]
    [InlineData("DrawIO_DataModel")]
    [InlineData("Html_DataModel")]
    public void ShouldRegisterEachFormatAndDiagramTypeRenderer(string key)
    {
        // Given
        using var provider = new ServiceCollection()
            .AddArchitectureDiagram()
            .BuildServiceProvider();
        // When
        var graph = provider.GetKeyedService<IDiagramRenderer>(serviceKey: key);
        // Then
        Assert.NotNull(@object: graph);
    }

    [Fact]
    public void ShouldKeepRenderersOutOfGenerationMethodParameters()
    {
        // Given
        Type contract = typeof(IDiagramGenerationOrchestrationService);
        // When
        var parameters = contract.GetMethod(name: "GenerateAsync")!
            .GetParameters();
        // Then
        Assert.DoesNotContain(collection: parameters, filter: parameter => parameter.ParameterType == typeof(IDiagramRenderer));
    }

    [Theory]
    [InlineData(" HTML ", " architecture ", "Html_Architecture")]
    [InlineData("drawio", "ARCHITECTURE", "DrawIO_Architecture")]
    public async Task ShouldBuildGraphWithTheKeyedRendererAndSharedServicesAsync(string format, string diagramType, string key)
    {
        // Given
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        var fixture = new GenerationFixture();
        services.AddSingleton<IProjectModelBuilderService>(implementationInstance: fixture);
        services.AddSingleton<IProjectModelTreeService>(implementationInstance: fixture);
        services.AddKeyedSingleton<IDiagramRenderer>(serviceKey: key, implementationInstance: fixture);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDiagramRendererFactory>();
        var graph = factory.CreateDiagramGenerationOrchestrationService(namedKey: key);
        // When
        byte[] bytes = await graph.GenerateAsync(request: new DiagramRenderRequest { ProjectPaths = new[] { "fixture.csproj" }, Format = Enum.Parse<DiagramFormats>(value: format, ignoreCase: true), DiagramType = Enum.Parse<DiagramTypes>(value: diagramType, ignoreCase: true) }, cancellationToken: CancellationToken.None);
        // Then
        Assert.Equal(expected: new byte[] { 42 }, actual: bytes);
        Assert.Equal(expected: "fixture.csproj", actual: fixture.Path);
        Assert.Same(expected: fixture.Project, actual: fixture.SplitInput);
        Assert.Equal(expected: fixture.Trees, actual: fixture.RenderInput);
    }

    [Fact]
    public void ShouldKeepTheRequestBrokerAsTheOnlyOperationalDependency()
    {
        // Given
        var constructor = Assert.Single(collection: typeof(DiagramRequestService).GetConstructors());
        // When
        var parameter = Assert.Single(collection: constructor.GetParameters());
        // Then
        Assert.Equal(expected: typeof(IDiagramRequestBroker), actual: parameter.ParameterType);
    }

    [Fact]
    public async Task ShouldDelegateThroughTheRequestBrokerAndPreserveCancellationAsync()
    {
        // Given
        var fixture = new GenerationFixture();
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        services.AddSingleton<IProjectModelBuilderService>(implementationInstance: fixture);
        services.AddSingleton<IProjectModelTreeService>(implementationInstance: fixture);
        services.AddKeyedSingleton<IDiagramRenderer>(serviceKey: "Html_Architecture", implementationInstance: fixture);
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IDiagramRequestProcessingService>();
        var request = new DiagramRenderRequest { ProjectPaths = new[] { "fixture.csproj" }, Format = DiagramFormats.Html, OutputPath = "test.html" };
        // When
        byte[] bytes = await service.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: CancellationToken.None);
        // Then
        Assert.Equal(expected: new byte[] { 42 }, actual: bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(testCode: () => service.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: new CancellationToken(true)));
    }

    [Fact]
    public async Task ShouldGenerateUsingTheRegisteredDataRendererAsync()
    {
        // Given
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        var fixture = new GenerationFixture();
        services.AddSingleton<IProjectModelBuilderService>(implementationInstance: fixture);
        services.AddSingleton<IProjectModelTreeService>(implementationInstance: fixture);
        services.AddKeyedSingleton<IDiagramRenderer>(serviceKey: "Html_DataModel", implementationInstance: fixture);
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IDiagramRequestService>();
        var request = new DiagramRenderRequest { Format = DiagramFormats.Html, DiagramType = DiagramTypes.DataModel, ProjectPaths = new[] { "fixture.csproj" } };
        // When
        byte[] bytes = await service.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: CancellationToken.None);
        // Then
        Assert.Equal(expected: new byte[] { 42 }, actual: bytes);
        Assert.Equal(expected: fixture.Trees, actual: fixture.RenderInput);
    }

    private sealed class GenerationFixture : IProjectModelBuilderService, IProjectModelTreeService, IDiagramRenderer
    {
        public ProjectModel Project { get; } = new();
        public ProjectModel[] Trees { get; } = new[] { new ProjectModel() };
        public string? Path { get; private set; }
        public ProjectModel? SplitInput { get; private set; }
        public ProjectModel[]? RenderInput { get; private set; }

        public Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken)
        {
            this.Path = projectFilePath;
            return Task.FromResult(result: this.Project);
        }

        public ProjectModel[] Split(ProjectModel projectModel)
        {
            this.SplitInput = projectModel;
            return this.Trees;
        }

        public byte[] Render(RenderModel renderModel)
        {
            this.RenderInput = renderModel.ProjectModels;
            return new byte[] { 42 };
        }
    }
}
