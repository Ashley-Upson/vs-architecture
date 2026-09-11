using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramGenerationOrchestrationServiceTests
{
    [Fact]
    public async Task GenerateAsync_analyzes_projects_then_exports_drawio()
    {
        var settings = DiagramSettings.CreateDefault();
        var graph = new DiagramModel(
            new[] { new ProjectContainer("project_a", "App", System.Array.Empty<TypeNode>()) },
            System.Array.Empty<ExternalDependencyNode>(),
            System.Array.Empty<DependencyEdge>(),
            new DiagramMetadata());
        var analysisProcessingService = new FakeAnalysisProcessingService(graph);
        var renderingProcessingService = new FakeRenderingProcessingService("drawio");
        var typed = new FakeTypedOrchestrator("drawio");
        var service = new DiagramGenerationOrchestrationService(analysisProcessingService, renderingProcessingService, typed);

        var result = await service.GenerateAsync(System.Array.Empty<Project>(), settings);

        Assert.Equal("drawio", result);
        Assert.Null(analysisProcessingService.Settings);
        Assert.Null(renderingProcessingService.Graph);
        Assert.NotNull(typed.Request);
        Assert.Collection(typed.Request!.Jobs,
            job => Assert.IsType<ArchitectureGenerationJob>(job),
            job => Assert.IsType<DataModelGenerationJob>(job));
    }

    private sealed class FakeTypedOrchestrator : ITypedDiagramGenerationOrchestrator
    {
        private readonly string _content;
        public FakeTypedOrchestrator(string content) => _content = content;
        public DiagramGenerationRequest? Request { get; private set; }

        public Task<TypedDiagramGenerationResult> GenerateAsync(
            IEnumerable<Project> selectedProjects,
            DiagramGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new TypedDiagramGenerationResult(
                new DrawioDocument(_content, [], []), [], []));
        }
    }

    private sealed class FakeAnalysisProcessingService : IDiagramAnalysisProcessingService
    {
        private readonly DiagramModel _graph;

        public FakeAnalysisProcessingService(DiagramModel graph)
        {
            _graph = graph;
        }

        public DiagramSettings? Settings { get; private set; }

        public Task<DiagramModel> AnalyzeAsync(
            IEnumerable<Project> selectedProjects,
            DiagramSettings settings,
            CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.FromResult(_graph);
        }
    }

    private sealed class FakeRenderingProcessingService : IDiagramRenderingProcessingService
    {
        private readonly string _drawio;

        public FakeRenderingProcessingService(string drawio)
        {
            _drawio = drawio;
        }

        public DiagramModel? Graph { get; private set; }

        public DiagramSettings? Settings { get; private set; }

        public string Render(DiagramModel graph, DiagramSettings settings)
        {
            Graph = graph;
            Settings = settings;
            return _drawio;
        }
    }
}
