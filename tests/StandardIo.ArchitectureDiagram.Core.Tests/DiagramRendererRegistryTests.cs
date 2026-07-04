using System;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Processings;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramRendererRegistryTests
{
    [Theory]
    [InlineData("drawio", "drawio")]
    [InlineData("DRAWIO", "drawio")]
    [InlineData("json", "json")]
    [InlineData("", "drawio")]
    [InlineData(null, "drawio")]
    [InlineData("missing", "drawio")]
    public void Resolve_returns_requested_renderer_or_drawio_fallback(string? rendererId, string expectedRendererId)
    {
        var registry = new DiagramRendererRegistry();

        var renderer = registry.Resolve(rendererId);

        Assert.Equal(expectedRendererId, renderer.RendererId);
    }

    [Fact]
    public void Registry_rejects_empty_renderer_set()
    {
        Assert.Throws<ArgumentException>(() => new DiagramRendererRegistry(Array.Empty<IDiagramRenderer>()));
    }

    [Fact]
    public void Processing_service_uses_selected_renderer()
    {
        var registry = new DiagramRendererRegistry(new IDiagramRenderer[]
        {
            new StubRenderer("drawio", "drawio-output"),
            new StubRenderer("json", "json-output")
        });
        var service = new DiagramRenderingProcessingService(registry);
        var settings = DiagramSettings.CreateDefault();
        settings.OutputRenderer = "json";

        var output = service.Render(EmptyDiagram(), settings);

        Assert.Equal("json-output", output);
    }

    private static DiagramModel EmptyDiagram()
    {
        return new DiagramModel(
            Array.Empty<ProjectContainer>(),
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>(),
            new DiagramMetadata());
    }

    private sealed class StubRenderer : IDiagramRenderer
    {
        private readonly string _output;

        public StubRenderer(string rendererId, string output)
        {
            RendererId = rendererId;
            _output = output;
        }

        public string RendererId { get; }

        public string DisplayName => RendererId;

        public string FileExtension => ".txt";

        public string FileFilter => "Text file (*.txt)|*.txt";

        public string Render(DiagramModel diagram, DiagramSettings settings) => _output;
    }
}
