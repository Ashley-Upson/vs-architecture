using System;
using System.Linq;
using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

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
        ArchitectureDiagramModel model,
        ArchitectureRenderSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        cancellationToken.ThrowIfCancellationRequested();
        return RenderWithDiagnostics(model, settings, ArchitectureRenderingMode.Production, cancellationToken).Page;
    }

    public ArchitectureRenderResult RenderWithDiagnostics(
        ArchitectureDiagramModel model,
        ArchitectureRenderSettings settings,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        CancellationToken cancellationToken = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        cancellationToken.ThrowIfCancellationRequested();
        if (mode != ArchitectureRenderingMode.Production)
            throw new NotSupportedException("Development project-region rendering has not yet been migrated to the typed renderer.");
        return _exporter.GenerateArchitectureResult(ToLegacyModel(model), ToLegacySettings(settings));
    }

    private static DiagramModel ToLegacyModel(ArchitectureDiagramModel model)
    {
        var selection = model.Selection;
        var report = selection is null ? null : new SemanticSelectionReport(
            selection.ScopePolicy,
            selection.Roots.Select(root => new RootDiscoveryPatternDefinition(
                root.PatternIndex, root.SourceLine, root.PatternText)).Distinct().OrderBy(item => item.PatternIndex).ToArray(),
            selection.Roots.Select(root => new SemanticRootMatch(
                root.SemanticNodeId, root.MatchedCanonicalValue, root.PatternIndex,
                root.SourceLine, root.PatternText)).ToArray(),
            selection.SelectedNodeIds, selection.OmittedNodeIds,
            selection.SelectedLinkIds, selection.OmittedLinkIds, selection.UnmatchedPatternIndexes);
        return new DiagramModel(
            model.Projects.Select(project => new ProjectContainer(
                project.Id, project.Name,
                project.Nodes.Select(node => new TypeNode(
                    node.Id, node.ProjectId, node.Name, node.FullName, node.Kind,
                    node.UniqueId, node.Interfaces)).ToArray(), project.UniqueId)).ToArray(),
            model.ExternalNodes.Select(node => new ExternalDependencyNode(
                node.Id, node.Name, node.AssemblyName, node.UniqueId, node.FullName, node.Tag)).ToArray(),
            model.Links.Select(link => new DependencyEdge(link.Id, link.SourceId, link.TargetId, link.Kind)).ToArray(),
            new DiagramMetadata(SemanticSelection: report));
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
