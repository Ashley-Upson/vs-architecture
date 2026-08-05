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
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class ArchitectureGenerationService : IArchitectureGenerationService
{
    private readonly IArchitectureAnalyser analyser;
    private readonly IArchitectureDiagramPlanner planner;
    private readonly IArchitectureDiagramRenderer<DrawioPage> renderer;
    private readonly IDrawioDocumentComposer composer;

    public ArchitectureGenerationService(
        IArchitectureAnalyser analyser,
        IArchitectureDiagramPlanner planner,
        IArchitectureDiagramRenderer<DrawioPage> renderer,
        IDrawioDocumentComposer composer)
    {
        this.analyser = analyser ?? throw new ArgumentNullException(nameof(analyser));
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
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
        var planningRequest = ArchitecturePlanningRequestFactory.Create(diagram, job, mode);
        var planned = planner.Plan(planningRequest);
        var page = renderer.Render(planned, new ArchitectureRenderRequest(
            planningRequest.Validation.Mode, planningRequest.GenerationSettings.OutputRenderer, true));
        if (!string.IsNullOrWhiteSpace(job.PageNameHint)) page = page with { SuggestedName = job.PageNameHint!.Trim() };
        var repeat = Repeat(page, serializationRepeatCount);
        var semanticNodes = diagram.Projects.SelectMany(project => project.Nodes).ToArray();
        var manifest = new ArchitectureGenerationManifest(
            diagram.Projects.Count,
            semanticNodes.Length + diagram.ExternalNodes.Count,
            diagram.Links.Count,
            planned.Routes.Count,
            planned.Diagnostics.Findings.Count,
            0,
            page.StablePageKey)
        {
            SemanticClassCount = semanticNodes.Count(node => node.Kind == "Class"),
            SemanticInterfaceCount = semanticNodes.Count(node => node.Kind == "Interface"),
            UniqueInterfaceResolutionCount = semanticNodes.Count(node => node.InterfaceResolution == InterfaceResolutionStatus.Unique),
            UnresolvedInterfaceCount = semanticNodes.Count(node => node.InterfaceResolution == InterfaceResolutionStatus.Unresolved),
            MultipleInterfaceResolutionCount = semanticNodes.Count(node => node.InterfaceResolution == InterfaceResolutionStatus.Multiple),
            ProjectedRenderNodeCount = planned.PhysicalNodes.Count,
            ProjectedRenderLinkCount = planned.PhysicalLinks.Count,
            DuplicatedInstanceCount = planned.PhysicalNodes.Count(node => node.ProjectionMode == PhysicalNodeProjectionMode.DuplicateBranch),
            CanonicalSharedNodeCount = planned.PhysicalNodes.Count(node => node.ProjectionMode == PhysicalNodeProjectionMode.Canonical),
            ExceptionAuthorisedDuplicateCount = planned.PhysicalNodes.Count(node => node.DuplicationProvenance is not null),
            MultiParentNodeCount = 0
        };
        var findings = planned.Diagnostics.Findings
            .Select(finding => new ValidationFinding(
                finding.Code,
                finding.SubjectId ?? "v6-planning",
                finding.SubjectId,
                null,
                1,
                finding.Message,
                false))
            .ToArray();
        return Task.FromResult(new TypedArchitectureGenerationResult(
            diagram, page, findings, manifest,
            new ArchitectureEligibilityResult(false, new[] { "V6 projection is available; placement, routing, sizing, geometry and Architecture node/link emission are deferred." }),
            () => new DrawioDiagnosticExportResult(page.GraphModel.ToString(),
                "{\"projectionCompleted\":true,\"logicalPlacementCompleted\":false,\"sizingCompleted\":false,\"absoluteGeometryCompleted\":false,\"routingDeferred\":true,\"sizingDeferred\":true,\"absoluteGeometryDeferred\":true}",
                new Dictionary<string, string>(), 0, 0),
            repeat, planned.Diagnostics.Metrics));
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
