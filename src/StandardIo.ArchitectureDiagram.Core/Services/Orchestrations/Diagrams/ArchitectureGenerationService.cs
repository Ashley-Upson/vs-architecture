using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class ArchitectureGenerationService : IArchitectureGenerationService
{
    private readonly IArchitectureAnalyser analyser;
    private readonly IArchitectureDiagnosticRenderer renderer;
    private readonly IDrawioDocumentComposer composer;

    public ArchitectureGenerationService(
        IArchitectureAnalyser analyser,
        IArchitectureDiagnosticRenderer renderer,
        IDrawioDocumentComposer composer)
    {
        this.analyser = analyser ?? throw new ArgumentNullException(nameof(analyser));
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
        var rendered = renderer.RenderWithDiagnostics(diagram, job.Rendering, mode, cancellationToken);
        var page = string.IsNullOrWhiteSpace(job.PageNameHint)
            ? rendered.Page
            : rendered.Page with { SuggestedName = job.PageNameHint!.Trim() };
        var repeat = Repeat(page, serializationRepeatCount);
        var manifest = new ArchitectureGenerationManifest(
            diagram.Projects.Count,
            diagram.Projects.Sum(project => project.Nodes.Count) + diagram.ExternalNodes.Count,
            diagram.Links.Count,
            rendered.Routes.Count,
            rendered.LogicalFindings.Count,
            rendered.PhysicalFindings.Count,
            page.StablePageKey);
        return Task.FromResult(new TypedArchitectureGenerationResult(
            diagram, page, rendered.PreRepairFindings, rendered.LogicalFindings,
            rendered.PhysicalFindings, rendered.RepairAttempts, rendered.Routes,
            rendered.Timings, manifest, rendered.Eligibility, () => rendered.Diagnostics,
            repeat, rendered.DevelopmentArtifacts));
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
