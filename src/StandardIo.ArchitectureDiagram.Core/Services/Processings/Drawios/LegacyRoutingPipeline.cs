using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed partial class RenderLayout
{
    internal static class LegacyRoutingPipeline
    {
        public static LegacyRoutingResult Run(
            PlacedGraph initialPlacement,
            DiagramSettings settings,
            ICollection<PipelineStageMetric> timings)
        {
            var placement = initialPlacement;
            var positionedLinks = MeasureStage(timings, "candidate construction and selection", () =>
                PositionLinks(placement.Graph, settings, placement.Nodes));
            var provisionalLinks = positionedLinks.Links;
            var corridors = Observe(placement, provisionalLinks, settings, timings);
            var lanes = MeasureStage(timings, "lane allocation", () => CorridorLaneAllocator.Allocate(corridors));
            var capacityFailureCount = lanes.CapacityRequests?.Count ?? 0;
            var capacityAttempts = new List<RouteRepairAttempt>();

            for (var capacityPass = 0; capacityPass < 2 && lanes.CapacityRequests?.Count > 0; capacityPass++)
            {
                var capacityExpansion = MeasureStage(timings, "capacity requests", () => ExpandLayersForCapacityRequests(
                    placement.Nodes,
                    provisionalLinks,
                    corridors,
                    lanes,
                    settings));
                capacityAttempts.AddRange(capacityExpansion.Attempts);
                if (!capacityExpansion.Changed)
                {
                    break;
                }

                placement = placement.Revise(
                    capacityExpansion.Nodes,
                    PlacementPipeline.PositionProjects(placement.Graph, settings, capacityExpansion.Nodes));
                positionedLinks = MeasureStage(timings, "candidate construction and selection", () =>
                    PositionLinks(placement.Graph, settings, placement.Nodes));
                provisionalLinks = positionedLinks.Links;
                corridors = Observe(placement, provisionalLinks, settings, timings);
                lanes = MeasureStage(timings, "lane allocation", () => CorridorLaneAllocator.Allocate(corridors));
                capacityFailureCount += lanes.CapacityRequests?.Count ?? 0;
            }

            var compiled = CompileAndValidate(
                placement,
                provisionalLinks,
                corridors,
                lanes,
                settings,
                timings,
                new RouteRevision(0));
            var preExpansionValidation = compiled.Validated.Validation;
            var expansion = ExpandLayersForLaneDemand(
                placement.Nodes,
                compiled.Validated.Links,
                compiled.Validated.Validation,
                settings);
            var expansionAttempts = capacityAttempts.Concat(expansion.Attempts).ToArray();
            if (expansion.Changed)
            {
                placement = placement.Revise(
                    expansion.Nodes,
                    PlacementPipeline.PositionProjects(placement.Graph, settings, expansion.Nodes));
                positionedLinks = MeasureStage(timings, "candidate construction and selection", () =>
                    PositionLinks(placement.Graph, settings, placement.Nodes));
                provisionalLinks = positionedLinks.Links;
                corridors = Observe(placement, provisionalLinks, settings, timings);
                lanes = MeasureStage(timings, "lane allocation", () => CorridorLaneAllocator.Allocate(corridors));
                compiled = CompileAndValidate(
                    placement,
                    provisionalLinks,
                    corridors,
                    lanes,
                    settings,
                    timings,
                    compiled.Validated.RouteRevision.Next());
            }

            var links = compiled.Validated.Links;
            var currentValidation = compiled.Validated.Validation;
            var repairBudget = links.Count > 256
                ? new RouteRepairBudget(16, 2, 1, 24)
                : links.Count > 128
                    ? new RouteRepairBudget(32, 4, 2, 128)
                    : new RouteRepairBudget();
            var duplicateExposureMode = settings.NodeDuplication.AllowDuplicateNodes &&
                placement.Graph.PlacementParentByNode.Count == 0 &&
                placement.Graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal));
            var duplicateNeedsRepair = RequiresDuplicateRepair(placement, compiled.Validated, settings);
            var repair = MeasureStage(timings, "repair passes", () => duplicateExposureMode && !duplicateNeedsRepair
                ? RouteRepairCoordinator.CompileOnly(
                    placement.Nodes,
                    links,
                    settings,
                    "SkippedDuplicatedModeNonBlockingAdvisories")
                : RouteRepairCoordinator.Repair(placement.Nodes, links, settings, repairBudget));

            return new LegacyRoutingResult(
                placement,
                repair.Links,
                positionedLinks,
                repair.Corridors,
                repair.Lanes,
                repair.Traversals,
                repair.PostRepairValidation,
                preExpansionValidation,
                expansionAttempts.Concat(repair.Attempts).ToArray(),
                repair.EstimatedWorkUsed,
                repair.WorkBudgetExhausted,
                repair.RunReason,
                repair.RoutesInvalidated,
                repair.RoutePairsRevalidated,
                repair.CorridorRebuildCount,
                capacityFailureCount,
                capacityAttempts.Count(attempt => attempt.Applied));
        }

        internal static bool RequiresDuplicateRepair(
            PlacedGraph placement,
            ValidatedLogicalRoutes routes,
            DiagramSettings settings)
        {
            routes.Normalized.Generated.EnsureCompatible(placement);
            routes.ValidatedCompatibilityCheck();
            return routes.Validation.Violations.Any(violation =>
                violation.Code == TraceabilityViolationCode.NodeCollision ||
                violation.Code == TraceabilityViolationCode.SharedSegment &&
                violation.Magnitude >= settings.Layout.NodeWidth);
        }

        private static CorridorObservation Observe(
            PlacedGraph placement,
            IReadOnlyDictionary<string, LinkLayout> links,
            DiagramSettings settings,
            ICollection<PipelineStageMetric> timings) =>
            MeasureStage(timings, "corridor observation", () => CorridorObserver.Observe(
                placement.Nodes,
                links,
                settings.Layout.ParallelLaneSpacing,
                settings.Layout.LinkPadding));

        private static CompiledRoutePhases CompileAndValidate(
            PlacedGraph placement,
            IReadOnlyDictionary<string, LinkLayout> provisionalLinks,
            CorridorObservation corridors,
            CorridorLaneAllocation lanes,
            DiagramSettings settings,
            ICollection<PipelineStageMetric> timings,
            RouteRevision generatedRevision)
        {
            var links = MeasureStage(timings, "lane geometry compilation", () =>
                CorridorLaneGeometryCompiler.Compile(provisionalLinks, corridors, lanes));
            var traversals = MeasureStage(timings, "traversal compilation", () =>
                EdgeTraversalCompiler.Compile(links, corridors, lanes, placement.Nodes, provisionalLinks));
            links = EdgeTraversalCompiler.Apply(links, traversals);
            var generated = new GeneratedLogicalRoutes(placement, links, generatedRevision);
            generated.EnsureCompatible(placement);
            links = MeasureStage(timings, "normalization", () =>
                LogicalRouteNormalizer.Normalize(placement.Nodes, generated.Links, settings.Layout.LinkPadding));
            var normalized = new NormalizedLogicalRoutes(generated, links, generated.Revision.Next());
            normalized.EnsureCompatible(generated);
            var validation = MeasureStage(timings, "validation", () =>
                TraceabilityValidator.Validate(placement.Nodes, normalized.Links, settings.Layout.ParallelLaneSpacing));
            return new CompiledRoutePhases(
                traversals,
                new ValidatedLogicalRoutes(normalized, validation));
        }
    }

    private sealed record CompiledRoutePhases(
        EdgeTraversalCompilation Traversals,
        ValidatedLogicalRoutes Validated);

    internal sealed record LegacyRoutingResult(
        PlacedGraph Placement,
        IReadOnlyDictionary<string, LinkLayout> Links,
        PositionedLinkLayouts PositionedLinks,
        CorridorObservation Corridors,
        CorridorLaneAllocation Lanes,
        EdgeTraversalCompilation Traversals,
        TraceabilityValidationResult Traceability,
        TraceabilityValidationResult PreRepairTraceability,
        IReadOnlyList<RouteRepairAttempt> RepairAttempts,
        int RepairWorkUsed,
        bool RepairBudgetExhausted,
        string RepairRunReason,
        int RoutesInvalidated,
        long RoutePairsRevalidated,
        int CorridorRebuildCount,
        int CapacityFailureCount,
        int CapacityExpansionCount);
}
