using System;
using System.Linq;
using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

/// <summary>
/// Canonical production renderer for typed Architecture generation. CLI and VSIX Architecture jobs
/// resolve this renderer through <see cref="IArchitectureDiagnosticRenderer"/>.
/// </summary>
public sealed class DrawioArchitectureRenderer : IArchitectureRenderer<DrawioPage>, IArchitectureDiagnosticRenderer
{
    private readonly ReplacementArchitectureRenderer _replacementRenderer;

    public DrawioArchitectureRenderer()
        : this(new ReplacementArchitectureRenderer())
    {
    }

    public DrawioArchitectureRenderer(ReplacementArchitectureRenderer replacementRenderer) =>
        _replacementRenderer = replacementRenderer ?? throw new ArgumentNullException(nameof(replacementRenderer));

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
        return _replacementRenderer.Render(graph, ToLegacySettings(settings));
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
