using System;
using System.Linq;
using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public sealed class DrawioArchitectureRenderer : IArchitectureRenderer<DrawioPage>, IArchitectureDiagnosticRenderer
{
    private readonly DeterministicDrawioExporter _exporter;

    public DrawioArchitectureRenderer()
        : this(new DeterministicDrawioExporter())
    {
    }

    public DrawioArchitectureRenderer(DeterministicDrawioExporter exporter) =>
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));

    public DrawioPage Render(
        ArchitectureRenderGraph graph,
        ArchitectureRenderSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        cancellationToken.ThrowIfCancellationRequested();
        return RenderWithDiagnostics(graph, settings, ArchitectureRenderingMode.Production, cancellationToken).Page;
    }

    public ArchitectureRenderResult RenderWithDiagnostics(
        ArchitectureRenderGraph graph,
        ArchitectureRenderSettings settings,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        CancellationToken cancellationToken = default)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        cancellationToken.ThrowIfCancellationRequested();
        var legacySettings = ToLegacySettings(settings);
        return _exporter.GenerateArchitectureProjectRegionResult(graph, legacySettings);
    }

    private static DiagramSettings ToLegacySettings(ArchitectureRenderSettings settings)
    {
        settings ??= new ArchitectureRenderSettings();
        return new DiagramSettings
        {
            Canvas = settings.Canvas,
            Layout = settings.Layout,
            StyleRules = settings.StyleRules,
            Overrides = settings.Overrides,
            ShowProjectContainers = settings.ShowProjectContainers,
            ProjectContainerStyle = settings.ProjectContainerStyle,
            ExternalDependencyStyle = settings.ExternalDependencyStyle,
            Connector = settings.Connector,
            NodeDuplication = settings.NodeDuplication
        };
    }
}
