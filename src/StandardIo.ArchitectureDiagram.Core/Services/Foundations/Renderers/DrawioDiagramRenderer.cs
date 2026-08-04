using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

/// <summary>
/// Adapts the middleman diagram model into the typed Architecture render graph.
/// Architecture generation then uses the same replacement renderer as the typed path.
/// </summary>
public sealed class DrawioDiagramRenderer : IDiagramRenderer
{
    private readonly ArchitectureTopologyProjector _projector = new();
    private readonly ReplacementArchitectureRenderer _renderer = new();

    public string RendererId => DiagramRendererIds.Drawio;

    public string DisplayName => "Draw.io diagram";

    public string FileExtension => ".drawio";

    public string FileFilter => "Draw.io diagram (*.drawio)|*.drawio|XML file (*.xml)|*.xml|All files (*.*)|*.*";

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        if (diagram is null) throw new System.ArgumentNullException(nameof(diagram));
        settings ??= DiagramSettings.CreateDefault();
        var architecture = LegacyArchitectureModelAdapter.ToArchitecture(diagram);
        var graph = _projector.Project(architecture, settings.NodeDuplication);
        var page = _renderer.Render(graph, settings).Page;
        return new DrawioDocumentComposer().Compose(new[] { page }, new DrawioDocumentSettings()).Content;
    }
}
