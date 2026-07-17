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
        var annotated = DrawioDiagnosticReportBuilder.Annotate(content, enforced, "All enforced findings");
        var focused = DrawioDiagnosticReportBuilder.FocusedOutputs(content, enforced);
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
}
