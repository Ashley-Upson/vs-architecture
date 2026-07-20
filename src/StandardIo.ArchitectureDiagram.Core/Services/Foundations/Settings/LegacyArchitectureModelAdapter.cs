using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;

public static class LegacyArchitectureModelAdapter
{
    public static ArchitectureDiagramModel ToArchitecture(DiagramModel model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        var selection = model.Metadata?.SemanticSelection;
        return new ArchitectureDiagramModel(
            model.Projects.Select(project => new ArchitectureProject(project.Id, project.Name,
                project.Types.Select(node => new ArchitectureNode(
                    node.Id, node.ProjectId, node.Name, node.FullName, node.Kind, node.UniqueId,
                    node.Interfaces ?? Array.Empty<string>())).ToArray(), project.UniqueId)).ToArray(),
            model.ExternalDependencies.Select(node => new ArchitectureExternalNode(
                node.Id, node.Name, node.AssemblyName, node.UniqueId, node.FullName, node.Tag)).ToArray(),
            model.Edges.Select(link => new ArchitectureLink(link.Id, link.SourceId, link.TargetId, link.Kind)).ToArray(),
            selection is null ? null : new ArchitectureSelectionDiagnostic(
                selection.ScopePolicy,
                selection.Roots.Select(root => new ArchitectureRoot(root.SemanticNodeId,
                    root.MatchedCanonicalValue, root.PatternIndex, root.SourceLine, root.PatternText)).ToArray(),
                selection.SelectedNodeIds, selection.OmittedNodeIds,
                selection.SelectedLinkIds, selection.OmittedLinkIds, selection.UnmatchedPatternIndexes));
    }
}
