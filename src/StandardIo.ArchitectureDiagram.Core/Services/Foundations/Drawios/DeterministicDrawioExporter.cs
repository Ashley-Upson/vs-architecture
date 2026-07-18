using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class DeterministicDrawioExporter : IDeterministicDrawioExporter
{
    public DrawioGenerationResult GenerateResult(
        DiagramModel diagram,
        DiagramSettings settings,
        IReadOnlyList<PipelineStageMetric>? upstreamTimings = null)
    {
        var prepared = Prepare(diagram, settings);
        if (upstreamTimings is not null)
        {
            prepared.StageTimings.InsertRange(0, upstreamTimings);
        }
        var findings = prepared.Layout.Traceability.Violations
            .Select(violation => ToFinding(violation, prepared.IsEnforced(violation)))
            .ToArray();
        var preRepairFindings = prepared.Layout.PreRepairTraceability.Violations
            .Select(violation => ToFinding(violation, enforced: false))
            .ToArray();
        var document = Measure(prepared.StageTimings, "serialization", () =>
            new DiagramFileBuilder(prepared.Settings).Build(prepared.Layout, prepared.Ownership));
        return new DrawioGenerationResult(
            document,
            preRepairFindings,
            findings,
            prepared.Layout.RepairAttempts,
            prepared.Layout.Links.Values
                .OrderBy(link => link.Link.Id, StringComparer.Ordinal)
                .Select(link => new GeneratedRoute(
                    link.Link.Id,
                    new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint })
                        .Select(point => new ValidationPoint(point.X, point.Y))
                        .ToArray()))
                .ToArray(),
            serializationSucceeded: true,
            strictValidationPassed: findings.All(finding => !finding.IsStrictlyEnforced),
            () => BuildDiagnostic(prepared, document),
            stageTimings: AllTimings(prepared));
    }

    public DrawioDiagnosticExportResult ExportDiagnostic(DrawioGenerationResult generationResult)
    {
        if (generationResult is null) throw new ArgumentNullException(nameof(generationResult));
        return generationResult.Diagnostics;
    }

    public void ValidateStrict(DrawioGenerationResult generationResult)
    {
        if (generationResult is null) throw new ArgumentNullException(nameof(generationResult));
        var enforced = generationResult.ValidationFindings
            .Where(finding => finding.IsStrictlyEnforced)
            .ToArray();
        if (enforced.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Strict geometry verification failed with {enforced.Length} finding(s)." + Environment.NewLine +
            string.Join(Environment.NewLine, enforced.Take(10).Select(finding => finding.Description)));
    }

    private static DrawioDiagnosticExportResult BuildDiagnostic(PreparedExport prepared, string content)
    {
        var enforced = prepared.Layout.Traceability.Violations.Where(prepared.IsEnforced).ToArray();
        var reportJson = DrawioDiagnosticReportBuilder.BuildJson(
            prepared.Layout,
            prepared.Ownership,
            enforced,
            prepared.Settings.Layout.ParallelLaneSpacing,
            AllTimings(prepared),
            diagnosticReuse: true);
        var annotated = DrawioDiagnosticReportBuilder.Annotate(content, enforced, "All enforced findings", prepared.Layout);
        var focused = DrawioDiagnosticReportBuilder.FocusedOutputs(content, enforced, prepared.Layout);
        return new DrawioDiagnosticExportResult(
            annotated,
            reportJson,
            focused,
            enforced.Length,
            enforced.Select(violation => violation.EdgeId).Distinct(StringComparer.Ordinal).Count());
    }

    private static PreparedExport Prepare(DiagramModel diagram, DiagramSettings settings)
    {
        if (diagram is null)
        {
            throw new ArgumentNullException(nameof(diagram));
        }

        settings ??= DiagramSettings.CreateDefault();
        var timings = new List<PipelineStageMetric>();
        var graph = Measure(timings, "render graph construction", () => RenderGraph.From(diagram, settings));
        var layout = Measure(timings, "layout and routing", () => RenderLayout.Build(graph, settings));
        var successfulCorridorsByEdge = layout.Corridors.SegmentMappings
            .Where(mapping => !layout.Lanes.FailedCorridorIds.Contains(mapping.CorridorId))
            .GroupBy(mapping => mapping.EdgeId)
            .ToDictionary(
                group => group.Key,
                group => new HashSet<string>(group.Select(mapping => mapping.CorridorId)));
        bool IsEnforced(TraceabilityViolation violation)
        {
            // Traversal compilation can contain collinear terminal-access redundancy;
            // ownership serialization removes that redundancy before XML emission.
            if (violation.Code == TraceabilityViolationCode.ImmediateReversal)
            {
                return false;
            }

            if (violation.Code == TraceabilityViolationCode.PerpendicularCrossing)
            {
                return false;
            }

            if (!successfulCorridorsByEdge.TryGetValue(violation.EdgeId, out var edgeCorridors))
            {
                return false;
            }

            return violation.OtherEdgeId is null ||
                successfulCorridorsByEdge.TryGetValue(violation.OtherEdgeId, out var otherCorridors) &&
                edgeCorridors.Overlaps(otherCorridors);
        }

        var ownership = Measure(timings, "ownership", () => CoordinateOwnershipCompiler.Compile(
            layout.Nodes,
            layout.Projects,
            layout.Links,
            settings.ShowProjectContainers));
        if (settings.ShowProjectContainers)
        {
            var projects = ProjectOwnershipBoundsCompiler.Compile(
                layout.Projects,
                layout.Nodes,
                ownership,
                settings.Layout.ContainerPadding,
                settings.Layout.ProjectHeaderHeight);
            layout = layout.WithProjects(projects);
            ownership = CoordinateOwnershipCompiler.Rebase(ownership, projects);
        }

        return new PreparedExport(settings, layout, ownership, IsEnforced, timings);
    }

    private static T Measure<T>(ICollection<PipelineStageMetric> timings, string stage, Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        var result = action();
        timer.Stop();
        timings.Add(new PipelineStageMetric(stage, timer.ElapsedMilliseconds,
            timings.Count(item => item.Stage == stage) + 1));
        return result;
    }

    private static PipelineStageMetric[] AllTimings(PreparedExport prepared) =>
        prepared.StageTimings.Concat(prepared.Layout.StageTimings).ToArray();

    private sealed record PreparedExport(
        DiagramSettings Settings,
        RenderLayout Layout,
        CoordinateOwnershipCompilation Ownership,
        Func<TraceabilityViolation, bool> IsEnforced,
        List<PipelineStageMetric> StageTimings);

    private static ValidationFinding ToFinding(TraceabilityViolation violation, bool enforced) =>
        new(
            DrawioDiagnosticReportBuilder.CategoryName(violation),
            violation.EdgeId,
            violation.OtherEdgeId,
            violation.OtherNodeId,
            violation.Magnitude,
            violation.Description,
            (violation.Locations ?? Array.Empty<Point>())
                .Select(point => new ValidationPoint(point.X, point.Y))
                .ToArray(),
            (violation.OffendingSegments ?? Array.Empty<Segment>())
                .Select(segment => new ValidationSegment(
                    new ValidationPoint(segment.Start.X, segment.Start.Y),
                    new ValidationPoint(segment.End.X, segment.End.Y)))
                .ToArray(),
            violation.RequiredSpacing,
            violation.ActualSpacing,
            violation.ParallelOverlapLength,
            enforced);
}
