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
    private const int MaximumExpansionReconciliationIterations = 12;

    internal static ProjectRegionLayout Build(RenderGraph graph, DiagramSettings settings)
    {
        var timings = new List<PipelineStageMetric>();
        var placed = MeasureStage(timings, "project-region positional placement", () =>
            ProjectRegionPlacement.Place(graph, settings, new LayoutRevision(0)));
        var semanticLayers = MeasureStage(timings, "project-region configured semantic layers", () =>
            ConfiguredSemanticLayerPlacement.Apply(placed, settings));
        var activePlacement = MeasureStage(timings, "project-region layer-band placement", () =>
            semanticLayers.Enabled
                ? ProjectLayerBandPlacement.AlignProjects(semanticLayers.Placement, settings)
                : graph.Projects.Count > 1
                    ? placed
                    : ProjectLayerBandPlacement.Align(placed, settings));
        AssertHorizontalGeometry(placed.Nodes, activePlacement.Nodes);
        var immutableBandPlacement = activePlacement;
        var baseBandExtents = ProjectLayerExpansionReconciler.BaseBandExtents(
            immutableBandPlacement.Nodes, settings.Layout.LinkPadding,
            settings.Layout.ParallelLaneSpacing, graph.Links.Count);
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> currentExpansions =
            new Dictionary<ProjectLayerExpansionIdentity, int>();
        var expansionStates = new List<IReadOnlyDictionary<ProjectLayerExpansionIdentity, int>>
        {
            currentExpansions
        };
        var seenExpansionHashes = new HashSet<string>(StringComparer.Ordinal)
        {
            ProjectLayerExpansionReconciler.Hash(currentExpansions)
        };
        var expansionIterations = new List<ProjectLayerExpansionIteration>();
        var cycleResolutionUsed = false;
        var cycleResolutionPending = false;
        CanonicalTopologySelection topology = null!;
        IReadOnlyDictionary<string, LinkLayout> terminalLayouts = null!;
        ProjectSlotCompilation slotCompilation = null!;
        for (var iteration = 0; iteration < MaximumExpansionReconciliationIterations; iteration++)
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
            var actualGaps = ProjectLayerExpansionReconciler.ActualGaps(baseBandExtents, currentExpansions);
            var fits = ProjectLayerExpansionReconciler.Fits(actualGaps, slotCompilation.RequiredExtentByBand);
            var desiredExpansions = ProjectLayerExpansionReconciler.DesiredExpansions(
                baseBandExtents, slotCompilation.RequiredExtentByBand);
            var changedBands = ChangedBands(currentExpansions, desiredExpansions);
            var changeKind = ChangeKind(currentExpansions, desiredExpansions, changedBands);
            expansionIterations.Add(new ProjectLayerExpansionIteration(
                iteration + 1,
                ProjectLayerExpansionReconciler.Hash(currentExpansions),
                actualGaps,
                slotCompilation.RequiredExtentByBand,
                changedBands,
                changeKind,
                cycleResolutionUsed));

            if (cycleResolutionPending && fits) break;
            if (fits && ProjectLayerExpansionReconciler.Same(currentExpansions, desiredExpansions)) break;

            if (!ProjectLayerExpansionReconciler.TryAddState(seenExpansionHashes, desiredExpansions))
            {
                cycleResolutionUsed = true;
                cycleResolutionPending = true;
                desiredExpansions = ProjectLayerExpansionReconciler.SafeCycleMap(
                    expansionStates, slotCompilation.RequiredExtentByBand, baseBandExtents);
                if (fits && ProjectLayerExpansionReconciler.Same(currentExpansions, desiredExpansions)) break;
            }

            currentExpansions = desiredExpansions;
            expansionStates.Add(currentExpansions);
            activePlacement = MeasureStage(timings, "project-region InterLayer expansion", () =>
                ProjectLayerBandPlacement.Expand(immutableBandPlacement, settings, currentExpansions));
            if (iteration == MaximumExpansionReconciliationIterations - 1)
                throw new InvalidOperationException("Project InterLayer extent reconciliation did not converge: " +
                    string.Join(",", expansionIterations.Select(state =>
                        $"iteration-{state.Iteration}:{state.ExpansionMapHash}:{state.ChangeKind}")));
        }
        var finalGaps = ProjectLayerExpansionReconciler.ActualGaps(baseBandExtents, currentExpansions);
        if (!ProjectLayerExpansionReconciler.Fits(finalGaps, slotCompilation.RequiredExtentByBand))
            throw new InvalidOperationException("Project InterLayer extent reconciliation accepted an invalid placement.");
        slotCompilation = slotCompilation with
        {
            ExpandedInterLayerCount = currentExpansions.Count(item => item.Value > 0),
            ExpansionIterations = expansionIterations,
            ExpansionCycleResolutionUsed = cycleResolutionUsed
        };
        var links = MeasureStage(timings, "project-region canonical normalization", () =>
            LogicalRouteNormalizer.Normalize(activePlacement.Nodes, slotCompilation.Links, settings.Layout.LinkPadding));
        var validation = MeasureStage(timings, "project-region logical validation", () =>
            TraceabilityValidator.Validate(activePlacement.Nodes, links, settings.Layout.ParallelLaneSpacing));
        return new ProjectRegionLayout(
            graph, activePlacement.Nodes, activePlacement.Projects, links, validation,
            timings, activePlacement.Revision, topology.Plans, slotCompilation, semanticLayers);
    }

    private static IReadOnlyList<ProjectLayerExpansionIdentity> ChangedBands(
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> current,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> desired) =>
        current.Keys.Union(desired.Keys).Where(key =>
                (current.TryGetValue(key, out var left) ? left : 0) !=
                (desired.TryGetValue(key, out var right) ? right : 0))
            .OrderBy(key => key.ProjectId, StringComparer.Ordinal).ThenBy(key => key.LowerDepth).ToArray();

    private static string ChangeKind(
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> current,
        IReadOnlyDictionary<ProjectLayerExpansionIdentity, int> desired,
        IReadOnlyList<ProjectLayerExpansionIdentity> changed)
    {
        if (changed.Count == 0) return "Unchanged";
        var grew = changed.Any(key =>
            (desired.TryGetValue(key, out var right) ? right : 0) >
            (current.TryGetValue(key, out var left) ? left : 0));
        var shrank = changed.Any(key =>
            (desired.TryGetValue(key, out var right) ? right : 0) <
            (current.TryGetValue(key, out var left) ? left : 0));
        return grew && shrank ? "Mixed" : grew ? "Grew" : "Shrank";
    }

    private static void AssertHorizontalGeometry(
        IReadOnlyDictionary<string, NodeLayout> baseline,
        IReadOnlyDictionary<string, NodeLayout> current)
    {
        var changed = current.Values.Where(node =>
                !baseline.TryGetValue(node.Node.Id, out var original) ||
                original.Rect.X != node.Rect.X || original.Rect.Width != node.Rect.Width)
            .OrderBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
        if (changed.Length == 0) return;
        throw new InvalidOperationException(
            "Configured semantic layer placement changed horizontal node geometry: " +
            string.Join(",", changed.Select(node => node.Node.Id)));
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
