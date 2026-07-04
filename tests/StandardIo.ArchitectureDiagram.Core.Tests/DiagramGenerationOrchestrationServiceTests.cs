using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;
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
        var service = new DiagramGenerationOrchestrationService(analysisProcessingService, renderingProcessingService);

        var result = await service.GenerateAsync(System.Array.Empty<Project>(), settings);

        Assert.Equal("drawio", result);
        Assert.Same(settings, analysisProcessingService.Settings);
        Assert.Same(graph, renderingProcessingService.Graph);
        Assert.Same(settings, renderingProcessingService.Settings);
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
