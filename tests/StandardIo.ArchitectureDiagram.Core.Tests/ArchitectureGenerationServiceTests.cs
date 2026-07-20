using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureGenerationServiceTests
{
    [Fact]
    public async Task Generate_analyses_and_renders_once_and_repeats_only_serialization()
    {
        var analyser = new CountingAnalyser();
        var renderer = new CountingRenderer();
        var service = new ArchitectureGenerationService(analyser, renderer, new DrawioDocumentComposer());

        var result = await service.GenerateAsync([], Job(), serializationRepeatCount: 3);

        Assert.Equal(1, analyser.Count);
        Assert.Equal(1, renderer.Count);
        Assert.Equal(3, result.SerializationRepeat!.RequestedRepeats);
        Assert.True(result.SerializationRepeat.IsDeterministic);
        Assert.Equal(4, result.SerializationRepeat.DocumentHashes.Count);
        Assert.Equal(2, result.Manifest.SemanticNodeCount);
        Assert.Equal(1, result.Manifest.SemanticLinkCount);
    }

    [Fact]
    public async Task Generate_keeps_expensive_diagnostic_export_lazy()
    {
        var renderer = new CountingRenderer();
        var service = new ArchitectureGenerationService(new CountingAnalyser(), renderer, new DrawioDocumentComposer());

        var result = await service.GenerateAsync([], Job());

        Assert.Equal(0, renderer.DiagnosticCount);
        _ = result.Diagnostics;
        _ = result.Diagnostics;
        Assert.Equal(1, renderer.DiagnosticCount);
    }

    private static ArchitectureGenerationJob Job() =>
        new(new ArchitectureAnalysisSettings(), new ArchitectureRenderSettings());

    private sealed class CountingAnalyser : IArchitectureAnalyser
    {
        public int Count { get; private set; }
        public Task<ArchitectureDiagramModel> AnalyseAsync(IEnumerable<Project> selectedProjects, ArchitectureAnalysisSettings settings, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.FromResult(new ArchitectureDiagramModel(
                [new ArchitectureProject("project", "Project", [
                    new ArchitectureNode("a", "project", "A", "Fixture.A", "Class", "", []),
                    new ArchitectureNode("b", "project", "B", "Fixture.B", "Class", "", [])], "")],
                [], [new ArchitectureLink("edge", "a", "b", "internal")], null));
        }
    }

    private sealed class CountingRenderer : IArchitectureDiagnosticRenderer
    {
        public int Count { get; private set; }
        public int DiagnosticCount { get; private set; }

        public ArchitectureRenderResult RenderWithDiagnostics(ArchitectureDiagramModel model, ArchitectureRenderSettings settings, ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, CancellationToken cancellationToken = default)
        {
            Count++;
            var page = new DrawioPage("Architecture", "architecture",
                new XElement("mxGraphModel", new XElement("root", new XElement("mxCell", new XAttribute("id", "0")))), []);
            return new ArchitectureRenderResult(page, [], [], [], [],
                [new GeneratedRoute("edge", [new ValidationPoint(0, 0), new ValidationPoint(0, 10)])], [],
                new ArchitectureEligibilityResult(true, []), () =>
                {
                    DiagnosticCount++;
                    return new DrawioDiagnosticExportResult("", "{}", new Dictionary<string, string>(), 0, 0);
                });
        }
    }
}
