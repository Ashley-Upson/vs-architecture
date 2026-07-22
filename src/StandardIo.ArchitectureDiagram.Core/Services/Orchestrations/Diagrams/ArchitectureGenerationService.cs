using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class ArchitectureGenerationService : IArchitectureGenerationService
{
    private readonly IArchitectureAnalyser analyser;
    private readonly IArchitectureTopologyProjector projector;
    private readonly IArchitectureDiagnosticRenderer renderer;
    private readonly IDrawioDocumentComposer composer;

    public ArchitectureGenerationService(
        IArchitectureAnalyser analyser,
        IArchitectureTopologyProjector projector,
        IArchitectureDiagnosticRenderer renderer,
        IDrawioDocumentComposer composer)
    {
        this.analyser = analyser ?? throw new ArgumentNullException(nameof(analyser));
        this.projector = projector ?? throw new ArgumentNullException(nameof(projector));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
    }

    public ArchitectureGenerationService(
        IArchitectureAnalyser analyser,
        IArchitectureDiagnosticRenderer renderer,
        IDrawioDocumentComposer composer)
        : this(analyser, new ArchitectureTopologyProjector(), renderer, composer)
    {
    }

    public async Task<TypedArchitectureGenerationResult> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        int serializationRepeatCount = 0,
        CancellationToken cancellationToken = default)
    {
        var diagram = await analyser.AnalyseAsync(selectedProjects, job.Analysis, cancellationToken).ConfigureAwait(false);
        return await GenerateAsync(diagram, job, mode, serializationRepeatCount, cancellationToken).ConfigureAwait(false);
    }

    public Task<TypedArchitectureGenerationResult> GenerateAsync(
        ArchitectureDiagramModel diagram,
        ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production,
        int serializationRepeatCount = 0,
        CancellationToken cancellationToken = default)
    {
        var projected = projector.Project(diagram, job.Rendering.NodeDuplication);
        var rendered = renderer.RenderWithDiagnostics(projected, job.Rendering, mode, cancellationToken);
        var page = string.IsNullOrWhiteSpace(job.PageNameHint)
            ? rendered.Page
            : rendered.Page with { SuggestedName = job.PageNameHint!.Trim() };
        var repeat = Repeat(page, serializationRepeatCount);
        var semanticNodes = diagram.Projects.SelectMany(project => project.Nodes).ToArray();
        var instanceCounts = projected.Nodes.GroupBy(node => node.SemanticNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var incomingParents = projected.Links.GroupBy(link => link.TargetSemanticId, StringComparer.Ordinal)
            .Count(group => group.Select(link => link.SourceSemanticId).Distinct(StringComparer.Ordinal).Skip(1).Any());
        var manifest = new ArchitectureGenerationManifest(
            diagram.Projects.Count,
            semanticNodes.Length + diagram.ExternalNodes.Count,
            diagram.Links.Count,
            rendered.Routes.Count,
            rendered.LogicalFindings.Count,
            rendered.PhysicalFindings.Count,
            page.StablePageKey)
        {
            SemanticClassCount = semanticNodes.Count(node => node.Kind == "Class"),
            SemanticInterfaceCount = semanticNodes.Count(node => node.Kind == "Interface"),
            UniqueInterfaceResolutionCount = semanticNodes.Count(node => node.InterfaceResolution == InterfaceResolutionStatus.Unique),
            UnresolvedInterfaceCount = semanticNodes.Count(node => node.InterfaceResolution == InterfaceResolutionStatus.Unresolved),
            MultipleInterfaceResolutionCount = semanticNodes.Count(node => node.InterfaceResolution == InterfaceResolutionStatus.Multiple),
            ProjectedRenderNodeCount = projected.Nodes.Count,
            ProjectedRenderLinkCount = projected.Links.Count,
            DuplicatedInstanceCount = projected.Nodes.Count(node => node.Occurrence == ArchitectureRenderNodeOccurrence.Duplicated),
            CanonicalSharedNodeCount = instanceCounts.Count(pair => pair.Value == 1 &&
                projected.Links.Count(link => link.TargetSemanticId == pair.Key) > 1),
            ExceptionAuthorisedDuplicateCount = projected.Nodes.Count(node =>
                node.DuplicationReason == ArchitectureDuplicationReason.ExceptionPattern),
            MultiParentNodeCount = incomingParents
        };
        return Task.FromResult(new TypedArchitectureGenerationResult(
            diagram, page, rendered.PreRepairFindings, rendered.LogicalFindings,
            rendered.PhysicalFindings, rendered.RepairAttempts, rendered.Routes,
            rendered.Timings, manifest, rendered.Eligibility, () => rendered.Diagnostics,
            repeat, rendered.DevelopmentArtifacts, projected));
    }

    private SerializationRepeatResult? Repeat(DrawioPage page, int repeatCount)
    {
        if (repeatCount <= 0) return null;
        var hashes = new List<string>();
        for (var index = 0; index <= repeatCount; index++)
        {
            var content = composer.Compose(new[] { page }, new DrawioDocumentSettings()).Content;
            using var sha = SHA256.Create();
            hashes.Add(string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(content)).Select(value => value.ToString("x2"))));
        }
        return new SerializationRepeatResult(repeatCount, hashes.Distinct(StringComparer.Ordinal).Count() == 1, hashes);
    }
}
