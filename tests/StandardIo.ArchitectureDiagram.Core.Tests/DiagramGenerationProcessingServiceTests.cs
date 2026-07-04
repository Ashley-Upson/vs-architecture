using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Services.Processings;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramGenerationProcessingServiceTests
{
    [Fact]
    public async Task GenerateAsync_analyzes_projects_then_exports_drawio()
    {
        var settings = DiagramSettings.CreateDefault();
        var graph = new ArchitectureGraph(
            new[] { new ProjectContainer("project_a", "App", System.Array.Empty<TypeNode>()) },
            System.Array.Empty<ExternalDependencyNode>(),
            System.Array.Empty<DependencyEdge>());
        var analysisProcessingService = new FakeAnalysisProcessingService(graph);
        var renderingProcessingService = new FakeRenderingProcessingService("drawio");
        var service = new DiagramGenerationProcessingService(analysisProcessingService, renderingProcessingService);

        var result = await service.GenerateAsync(System.Array.Empty<Project>(), settings);

        Assert.Equal("drawio", result);
        Assert.Same(settings, analysisProcessingService.Settings);
        Assert.Same(graph, renderingProcessingService.Graph);
        Assert.Same(settings, renderingProcessingService.Settings);
    }

    private sealed class FakeAnalysisProcessingService : IDiagramAnalysisProcessingService
    {
        private readonly ArchitectureGraph _graph;

        public FakeAnalysisProcessingService(ArchitectureGraph graph)
        {
            _graph = graph;
        }

        public DiagramSettings? Settings { get; private set; }

        public Task<ArchitectureGraph> AnalyzeAsync(
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

        public ArchitectureGraph? Graph { get; private set; }

        public DiagramSettings? Settings { get; private set; }

        public string Render(ArchitectureGraph graph, DiagramSettings settings)
        {
            Graph = graph;
            Settings = settings;
            return _drawio;
        }
    }
}
