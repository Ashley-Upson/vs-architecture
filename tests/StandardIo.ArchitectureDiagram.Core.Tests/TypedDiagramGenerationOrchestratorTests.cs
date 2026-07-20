using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class TypedDiagramGenerationOrchestratorTests
{
    [Fact]
    public async Task Generate_executes_independent_typed_jobs_in_request_order()
    {
        var calls = new List<string>();
        var service = Service(calls, dataEntities: 1);
        var request = new DiagramGenerationRequest(
            [
                new DataModelGenerationJob(new DataModelAnalysisSettings(), new DataModelRenderSettings(), PageNameHint: "Tables"),
                new ArchitectureGenerationJob(new ArchitectureAnalysisSettings(), new ArchitectureRenderSettings(), "System")
            ], new DrawioDocumentSettings());

        var result = await service.GenerateAsync([], request);

        Assert.Equal(new[] { "data-analysis", "data-render", "architecture-analysis", "architecture-render" }, calls);
        Assert.Equal(new[] { "Tables", "System" }, result.Document.PageNames);
    }

    [Fact]
    public async Task Generate_omits_empty_data_model_without_failing_architecture()
    {
        var service = Service(new List<string>(), dataEntities: 0);
        var request = new DiagramGenerationRequest(
            [
                new ArchitectureGenerationJob(new ArchitectureAnalysisSettings(), new ArchitectureRenderSettings()),
                new DataModelGenerationJob(new DataModelAnalysisSettings(), new DataModelRenderSettings())
            ], new DrawioDocumentSettings());

        var result = await service.GenerateAsync([], request);

        Assert.Single(result.Pages);
        Assert.Equal("Architecture", result.Pages[0].SuggestedName);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.DiagramType == DiagramType.DataModel && !diagnostic.IsFailure);
    }

    private static TypedDiagramGenerationOrchestrator Service(List<string> calls, int dataEntities) => new(
        new FakeArchitectureGenerationService(calls),
        new FakeDataModelAnalyser(calls, dataEntities), new FakeDataModelRenderer(calls),
        new DrawioDocumentComposer());

    private static DrawioPage Page(string name, string key) => new(name, key,
        new System.Xml.Linq.XElement("mxGraphModel", new System.Xml.Linq.XElement("root",
            new System.Xml.Linq.XElement("mxCell", new System.Xml.Linq.XAttribute("id", "0")))), []);

    private sealed class FakeArchitectureGenerationService(List<string> calls) : IArchitectureGenerationService
    {
        public Task<TypedArchitectureGenerationResult> GenerateAsync(IEnumerable<Project> selectedProjects, ArchitectureGenerationJob job, ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, int serializationRepeatCount = 0, CancellationToken cancellationToken = default)
        {
            calls.Add("architecture-analysis");
            calls.Add("architecture-render");
            return Result();
        }

        public Task<TypedArchitectureGenerationResult> GenerateAsync(ArchitectureDiagramModel diagram, ArchitectureGenerationJob job, ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, int serializationRepeatCount = 0, CancellationToken cancellationToken = default) => Result();

        private static Task<TypedArchitectureGenerationResult> Result()
        {
            var page = Page("Architecture", "architecture");
            return Task.FromResult(new TypedArchitectureGenerationResult(
                new ArchitectureDiagramModel([], [], [], null), page, [], [], [], [], [], [],
                new ArchitectureGenerationManifest(0, 0, 0, 0, 0, 0, "architecture"),
                new ArchitectureEligibilityResult(true, []),
                () => new DrawioDiagnosticExportResult("", "{}", new Dictionary<string, string>(), 0, 0),
                null, null));
        }
    }

    private sealed class FakeDataModelAnalyser(List<string> calls, int entityCount) : IDataModelAnalyser
    {
        public Task<DataModelDiagram> AnalyseAsync(IEnumerable<Project> selectedProjects, DataModelAnalysisSettings settings, CancellationToken cancellationToken = default)
        {
            calls.Add("data-analysis");
            var entities = Enumerable.Range(0, entityCount).Select(index => new DataModelEntity(
                "entity-" + index, "project", "Project", "Entity", "Fixture.Entity", "Fixture", "Class", "Public",
                false, false, false, null, "Fixture", "Fixture.Entity", [])).ToArray();
            return Task.FromResult(new DataModelDiagram(entities, [], []));
        }
    }

    private sealed class FakeDataModelRenderer(List<string> calls) : IDataModelRenderer<DrawioPage>
    {
        public DrawioPage Render(DataModelDiagram model, DataModelRenderSettings settings, CancellationToken cancellationToken = default)
        {
            calls.Add("data-render");
            return Page("Data Model", "data-model");
        }
    }
}
