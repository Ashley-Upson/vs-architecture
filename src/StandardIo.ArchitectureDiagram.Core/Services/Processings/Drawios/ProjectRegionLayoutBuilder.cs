using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

/// <summary>
/// Canonical typed Architecture layout authority after topology projection.
/// </summary>
internal static class ProjectRegionLayoutBuilder
{
    internal static ProjectRegionLayout Build(RenderGraph graph, DiagramSettings settings)
    {
        var timings = new List<PipelineStageMetric>();
        var placed = MeasureStage(timings, "project-region positional placement", () =>
            ProjectRegionPlacement.Place(graph, settings, new LayoutRevision(0)));
        var activePlacement = MeasureStage(timings, "project-region layer-band placement", () =>
            graph.Projects.Count > 1 ? placed : ProjectLayerBandPlacement.Align(placed, settings));
        var immutableBandPlacement = activePlacement;
        var accumulatedExpansions = new Dictionary<ProjectLayerExpansionIdentity, int>();
        CanonicalTopologySelection topology = null!;
        IReadOnlyDictionary<string, LinkLayout> terminalLayouts = null!;
        ProjectSlotCompilation slotCompilation = null!;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            topology = MeasureStage(timings, "project-region canonical topology selection", () =>
                CanonicalTopologyFamilySelector.Select(graph, activePlacement.Nodes, activePlacement.Revision));
            terminalLayouts = MeasureStage(timings, "project-region terminal allocation", () =>
                ProjectTerminalAllocator.Allocate(graph, activePlacement.Nodes, settings));
            var projectLabels = MeasureStage(timings, "project-region project-label measurement", () =>
                ProjectLabelGeometryMeasurer.Measure(
                    activePlacement.Projects, settings.Layout.ProjectHeaderHeight, settings.Layout.LinkPadding));
            slotCompilation = ProjectInterLayerSlotCompiler.Compile(
                topology.Plans, activePlacement.Nodes, terminalLayouts, projectLabels, activePlacement.Revision,
                settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
            timings.AddRange(slotCompilation.Timings);
            if (slotCompilation.RequiredLayerExpansion.Count == 0) break;
            foreach (var expansion in slotCompilation.RequiredLayerExpansion)
                accumulatedExpansions[expansion.Key] =
                    (accumulatedExpansions.TryGetValue(expansion.Key, out var existing) ? existing : 0) +
                    expansion.Value;
            activePlacement = MeasureStage(timings, "project-region InterLayer expansion", () =>
                ProjectLayerBandPlacement.Expand(immutableBandPlacement, settings, accumulatedExpansions));
            if (iteration == 7)
                throw new InvalidOperationException("Project InterLayer expansion did not converge: " +
                    string.Join(",", slotCompilation.RequiredLayerExpansion
                        .OrderBy(item => item.Key.ProjectId, StringComparer.Ordinal).ThenBy(item => item.Key.LowerDepth)
                        .Select(item => $"project-{item.Key.ProjectId}:depth-{item.Key.LowerDepth}:{item.Value}:" +
                            $"upper={activePlacement.Nodes.Values.Where(node => node.Node.ProjectId == item.Key.ProjectId && node.Depth == item.Key.LowerDepth - 1).Select(node => node.Rect.Bottom).DefaultIfEmpty(-1).Max()}:" +
                            $"lower={activePlacement.Nodes.Values.Where(node => node.Node.ProjectId == item.Key.ProjectId && node.Depth == item.Key.LowerDepth).Select(node => node.Rect.Y).DefaultIfEmpty(-1).Min()}")));
        }
        slotCompilation = slotCompilation with { ExpandedInterLayerCount = accumulatedExpansions.Count };
        var links = MeasureStage(timings, "project-region canonical normalization", () =>
            LogicalRouteNormalizer.Normalize(activePlacement.Nodes, slotCompilation.Links, settings.Layout.LinkPadding));
        var validation = MeasureStage(timings, "project-region logical validation", () =>
            TraceabilityValidator.Validate(activePlacement.Nodes, links, settings.Layout.ParallelLaneSpacing));
        return new ProjectRegionLayout(
            graph, activePlacement.Nodes, activePlacement.Projects, links, validation,
            timings, activePlacement.Revision, topology.Plans, slotCompilation);
    }

    private static T MeasureStage<T>(ICollection<PipelineStageMetric> timings, string stage, Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        T result;
        using (PerformanceAudit.Measure(stage))
        {
            result = action();
        }
        timer.Stop();
        timings.Add(new PipelineStageMetric(stage, timer.ElapsedMilliseconds,
            timings.Count(item => item.Stage == stage) + 1));
        return result;
    }
}
