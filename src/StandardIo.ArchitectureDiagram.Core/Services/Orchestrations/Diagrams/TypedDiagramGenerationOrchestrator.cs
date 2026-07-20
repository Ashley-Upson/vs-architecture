using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class TypedDiagramGenerationOrchestrator : ITypedDiagramGenerationOrchestrator
{
    private readonly IArchitectureAnalyser _architectureAnalyser;
    private readonly IArchitectureRenderer<DrawioPage> _architectureRenderer;
    private readonly IDataModelAnalyser _dataModelAnalyser;
    private readonly IDataModelRenderer<DrawioPage> _dataModelRenderer;
    private readonly IDrawioDocumentComposer _composer;

    public TypedDiagramGenerationOrchestrator()
        : this(new RoslynDependencyAnalyzer(), new DrawioArchitectureRenderer(),
            new RoslynDataModelAnalyser(), new DrawioDataModelRenderer(), new DrawioDocumentComposer())
    {
    }

    public TypedDiagramGenerationOrchestrator(
        IArchitectureAnalyser architectureAnalyser,
        IArchitectureRenderer<DrawioPage> architectureRenderer,
        IDataModelAnalyser dataModelAnalyser,
        IDataModelRenderer<DrawioPage> dataModelRenderer,
        IDrawioDocumentComposer composer)
    {
        _architectureAnalyser = architectureAnalyser ?? throw new ArgumentNullException(nameof(architectureAnalyser));
        _architectureRenderer = architectureRenderer ?? throw new ArgumentNullException(nameof(architectureRenderer));
        _dataModelAnalyser = dataModelAnalyser ?? throw new ArgumentNullException(nameof(dataModelAnalyser));
        _dataModelRenderer = dataModelRenderer ?? throw new ArgumentNullException(nameof(dataModelRenderer));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
    }

    public async Task<TypedDiagramGenerationResult> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        DiagramGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Grouping != DiagramOutputGrouping.SingleDocument)
            throw new NotSupportedException($"Output grouping {request.Grouping} is not supported.");
        var projects = selectedProjects?.Where(project => project is not null).ToArray() ?? Array.Empty<Project>();
        var pages = new List<DrawioPage>();
        var diagnostics = new List<DiagramJobDiagnostic>();
        foreach (var job in request.Jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await ExecuteAsync(projects, job, cancellationToken).ConfigureAwait(false);
                if (page is not null) pages.Add(WithNameHint(page, job.PageNameHint));
                else diagnostics.Add(new DiagramJobDiagnostic(job.DiagramType, "Analysis", "No eligible content; no page emitted.", false));
            }
            catch (Exception exception) when (request.FailurePolicy == DiagramGenerationFailurePolicy.KeepSuccessfulPages &&
                                              exception is not OperationCanceledException)
            {
                diagnostics.Add(new DiagramJobDiagnostic(job.DiagramType, "AnalysisOrRendering", exception.Message, true));
            }
        }

        var document = _composer.Compose(pages, request.DocumentSettings);
        return new TypedDiagramGenerationResult(document, pages, diagnostics);
    }

    private async Task<DrawioPage?> ExecuteAsync(
        IReadOnlyList<Project> projects,
        DiagramGenerationJob job,
        CancellationToken cancellationToken)
    {
        switch (job)
        {
            case ArchitectureGenerationJob architecture:
                var architectureModel = await _architectureAnalyser.AnalyseAsync(
                    projects, architecture.Analysis, cancellationToken).ConfigureAwait(false);
                return _architectureRenderer.Render(architectureModel, architecture.Rendering, cancellationToken);
            case DataModelGenerationJob dataModel:
                var dataModelModel = await _dataModelAnalyser.AnalyseAsync(
                    projects, dataModel.Analysis, cancellationToken).ConfigureAwait(false);
                if (dataModelModel.Entities.Count == 0 && !dataModel.EmitEmptyPage) return null;
                return _dataModelRenderer.Render(dataModelModel, dataModel.Rendering, cancellationToken);
            default:
                throw new NotSupportedException($"Diagram job type {job.GetType().FullName} is not supported.");
        }
    }

    private static DrawioPage WithNameHint(DrawioPage page, string? hint) =>
        string.IsNullOrWhiteSpace(hint) ? page : page with { SuggestedName = hint!.Trim() };
}
