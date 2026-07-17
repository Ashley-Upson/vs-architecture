using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class DeterministicDrawioExporter : IDeterministicDrawioExporter
{
    public string Export(DiagramModel diagram, DiagramSettings settings)
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
        TraceabilityValidator.ThrowIfInvalid(layout.Traceability, violation =>
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
        });
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

        return new DiagramFileBuilder(settings).Build(layout, ownership);
    }
}
