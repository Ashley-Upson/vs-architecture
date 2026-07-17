using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class DeterministicDrawioExporter : IDeterministicDrawioExporter
{
    public string Export(DiagramModel diagram, DiagramSettings settings)
    {
        var prepared = Prepare(diagram, settings);
        TraceabilityValidator.ThrowIfInvalid(prepared.Layout.Traceability, prepared.IsEnforced);
        return new DiagramFileBuilder(prepared.Settings).Build(prepared.Layout, prepared.Ownership);
    }

    public DrawioGenerationResult GenerateResult(DiagramModel diagram, DiagramSettings settings)
    {
        var prepared = Prepare(diagram, settings);
        var findings = prepared.Layout.Traceability.Violations
            .Select(violation => ToFinding(violation, prepared.IsEnforced(violation)))
            .ToArray();
        var document = new DiagramFileBuilder(prepared.Settings).Build(prepared.Layout, prepared.Ownership);
        return new DrawioGenerationResult(
            document,
            findings,
            findings,
            Array.Empty<RouteRepairAttempt>(),
            prepared.Layout.Links.Values
                .OrderBy(link => link.Link.Id, StringComparer.Ordinal)
                .Select(link => new GeneratedRoute(
                    link.Link.Id,
                    new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint })
                        .Select(point => new ValidationPoint(point.X, point.Y))
                        .ToArray()))
                .ToArray(),
            serializationSucceeded: true,
            strictValidationPassed: findings.All(finding => !finding.IsStrictlyEnforced));
    }

    public DrawioDiagnosticExportResult ExportDiagnostic(DiagramModel diagram, DiagramSettings settings)
    {
        var prepared = Prepare(diagram, settings);
        var enforced = prepared.Layout.Traceability.Violations.Where(prepared.IsEnforced).ToArray();
        var content = new DiagramFileBuilder(prepared.Settings).Build(prepared.Layout, prepared.Ownership);
        var reportJson = DrawioDiagnosticReportBuilder.BuildJson(
            prepared.Layout,
            prepared.Ownership,
            enforced,
            prepared.Settings.Layout.ParallelLaneSpacing);
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
        var graph = RenderGraph.From(diagram, settings);
        var layout = RenderLayout.Build(graph, settings);
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

            if (!successfulCorridorsByEdge.TryGetValue(violation.EdgeId, out var edgeCorridors))
            {
                return false;
            }

            return violation.OtherEdgeId is null ||
                successfulCorridorsByEdge.TryGetValue(violation.OtherEdgeId, out var otherCorridors) &&
                edgeCorridors.Overlaps(otherCorridors);
        }

        var ownership = CoordinateOwnershipCompiler.Compile(
            layout.Nodes,
            layout.Projects,
            layout.Links,
            settings.ShowProjectContainers);
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

        return new PreparedExport(settings, layout, ownership, IsEnforced);
    }

    private sealed record PreparedExport(
        DiagramSettings Settings,
        RenderLayout Layout,
        CoordinateOwnershipCompilation Ownership,
        Func<TraceabilityViolation, bool> IsEnforced);

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
