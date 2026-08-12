using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class ArchitectureV7ProductionGenerationService : IArchitectureGenerationService
{
    private readonly IArchitectureAnalyser analyser;
    private readonly IDrawioDocumentComposer composer;

    public ArchitectureV7ProductionGenerationService(IArchitectureAnalyser analyser, IDrawioDocumentComposer composer)
    { this.analyser = analyser ?? throw new ArgumentNullException(nameof(analyser)); this.composer = composer ?? throw new ArgumentNullException(nameof(composer)); }

    public async Task<TypedArchitectureGenerationResult> GenerateAsync(IEnumerable<Project> selectedProjects, ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, int serializationRepeatCount = 0, CancellationToken cancellationToken = default)
    {
        var diagram = await analyser.AnalyseAsync(selectedProjects, job.Analysis, cancellationToken).ConfigureAwait(false);
        return await GenerateAsync(diagram, job, mode, serializationRepeatCount, cancellationToken).ConfigureAwait(false);
    }

    public Task<TypedArchitectureGenerationResult> GenerateAsync(ArchitectureDiagramModel diagram, ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, int serializationRepeatCount = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stageTimings = new Dictionary<string, long>(StringComparer.Ordinal);
        T Measure<T>(string name, Func<T> action)
        {
            var stopwatch = Stopwatch.StartNew();
            try { return action(); }
            finally { stageTimings[name] = stopwatch.ElapsedMilliseconds; }
        }
        var pre = Configuration(job.Rendering.Layout);
        var semanticEvidence = SemanticEvidence(diagram);
        var analyserInputEvidence = AnalyserInputEvidence(job);
        var projection = Measure("projection", () => new ArchitectureV7PhysicalProjectionStage().Project(diagram, new ArchitectureV7ProjectionPolicy(
            job.Rendering.NodeDuplication.AllowDuplicateNodes ? ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches : ArchitectureV7ProjectionMode.Canonical,
            (job.Rendering.NodeDuplication.DuplicationExceptionPatterns ?? new List<string>()).Concat(job.Rendering.Layout.DuplicateHighNoiseNodePatterns ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray())));
        var ownership = Measure("ownership", () => new ArchitectureV7PositionalOwnershipStage().Resolve(projection));
        var sizing = Measure("sizing", () => new ArchitectureV7PreRoutingNodeSpanSizer().Size(ownership, pre));
        var reservation = Measure("reservation", () => new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, pre)));
        var ordinarySchedule = Measure("ordinary-layer-scheduling", () => new ArchitectureV7OrdinaryLayerSchedulingStage().Schedule(ownership, reservation));
        var trees = Measure("recursive-placement", () => new ArchitectureV7RecursiveTreeGridStage().Build(sizing, ordinarySchedule));
        var placement = Measure("project-composition", () => new ArchitectureV7ProjectCompositionStage().Compose(trees, pre));
        var corridorDiscovery = Measure("corridor-discovery", () => new ArchitectureV7CapabilityCorridorDiscoveryStage().Discover(placement));
        var routes = Measure("logical-routing", () => new ArchitectureV7LogicalRelationshipRoutingStage().Route(placement, projection));
        var corridorProjection = Measure("route-corridor-projection", () => new ArchitectureV7RouteCorridorProjectionStage().Project(placement, routes, corridorDiscovery));
        var allocation = Measure("collective-allocation", () => new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routes,
            new ArchitectureV7AllocationConfiguration(job.Rendering.Layout.ParallelLaneSpacing, job.Rendering.Layout.EdgePortSpacing, job.Rendering.Layout.LinkNodeWidthPadding, job.Rendering.Layout.BaseCellWidth,
                job.Rendering.Layout.LinkPadding)));
        var sceneConfiguration = new ArchitectureV7PhysicalSceneConfiguration(job.Rendering.Layout.BaseCellWidth, job.Rendering.Layout.RoutingRowMinimum,
            job.Rendering.Layout.BoundaryRowMinimum, job.Rendering.Layout.NodeWidth, job.Rendering.Layout.NodeHeight, job.Rendering.Layout.ProjectHeaderHeight,
            job.Rendering.Layout.LabelCharacterWidth, job.Rendering.Layout.LinkNodeWidthPadding, job.Rendering.Layout.LinkPadding,
            job.Rendering.Layout.VerticalNodeClearance, job.Rendering.Layout.ParallelLaneSpacing, job.Rendering.Layout.EdgePortSpacing, job.Rendering.Layout.LinkNodeWidthPadding);
        var unsimplifiedScene = Measure("physical-sizing-scene-compilation", () => new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routes, allocation, sceneConfiguration,
            sizing.Requirements.ToDictionary(item => item.PhysicalNodeId, item => item.RequiredWidth, StringComparer.Ordinal)));
        var simplification = Measure("allocated-route-simplification", () => new ArchitectureV7AllocatedRouteSimplificationStage().Simplify(unsimplifiedScene));
        var scene = simplification.Scene;
        var scheduledReservation = new ArchitectureV7ReservationReconciliationResult(reservation.Inspection, ordinarySchedule.Reservations);
        var acceptance = Measure("acceptance-validation", () => new ArchitectureV7FinalAcceptanceValidationStage().Validate(projection, ownership, sizing, scheduledReservation, placement, routes, allocation, scene, sceneConfiguration));
        var evidenceStage = new ArchitectureV7RoutingEvidenceStage();
        var routingEvidence = evidenceStage.Analyze(placement, routes);
        var placementEvidence = evidenceStage.Placement(placement).Select(item => new
        {
            item.PhysicalNodeId,
            PositionalParentId = ownership.Decisions.FirstOrDefault(x => x.PhysicalNodeId == item.PhysicalNodeId)?.PositionalParentPhysicalNodeId,
            item.TreeRootId,
            item.DetachedUnitId,
            item.ProjectId,
            item.TreeLocalCentre,
            item.TreeLocalSpan,
            item.TreeLocalBounds,
            item.ProjectCentre,
            item.ProjectSpan,
            item.ProjectBounds,
            item.CommonGridCentre,
            item.CommonGridSpan,
            item.CommonGridBounds,
            item.AtomicTopLevelUnitId,
            item.ImmediateSiblingUnits,
            item.CompositionOffset,
            item.Provenance
        }).ToArray();
        var overlapNodes = new[] { "physical:type_3142721cf6d670be", "physical:type_79f523abe24dbcf2", "physical:type_837c11765e8a3c8a" };
        var externalNodes = new[] { "physical:external_384594ffb4b54910", "physical:external_6a5ebf9229bdd229:duplicate:4", "physical:external_1d9865dc23352050", "physical:external_6a5ebf9229bdd229:duplicate:10", "physical:external_d6f27d4352f01a68" };
        var representative = new
        {
            DirectChild = routingEvidence.Where(x => x.Scenario == "direct-child").ToArray(),
            Downward = routingEvidence.Where(x => x.Scenario.Contains("downward", StringComparison.Ordinal) && !overlapNodes.Contains(x.SourcePhysicalNodeId, StringComparer.Ordinal) && !overlapNodes.Contains(x.TargetPhysicalNodeId, StringComparer.Ordinal)).Take(3).ToArray(),
            Upward = routingEvidence.Where(x => x.Scenario.Contains("upward", StringComparison.Ordinal) && !overlapNodes.Contains(x.SourcePhysicalNodeId, StringComparer.Ordinal) && !overlapNodes.Contains(x.TargetPhysicalNodeId, StringComparer.Ordinal)).Take(3).ToArray(),
            OverlapNodes = placementEvidence.Where(x => overlapNodes.Contains(x.PhysicalNodeId, StringComparer.Ordinal)).ToArray(),
            ExternalSeparation = placementEvidence.Where(x => externalNodes.Contains(x.PhysicalNodeId, StringComparer.Ordinal)).ToArray()
        };
        var allocationSceneEvidence = new
        {
            Summary = new
            {
                AllocationDiagnostics = allocation.Diagnostics.Count,
                AllocationDiagnosticCodes = allocation.Diagnostics.GroupBy(x => x.Code, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal),
                SceneDiagnostics = scene.Diagnostics.Count,
                SceneDiagnosticCodes = scene.Diagnostics.GroupBy(x => x.Code, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal),
                RunCount = allocation.Runs.Count,
                LaneCount = allocation.Lanes.Count,
                TerminalCount = allocation.Terminals.Count,
                ApproachCount = allocation.Approaches.Count,
                HandoffCount = allocation.Handoffs.Count,
                BendCount = allocation.Bends.Count,
                CrossingCount = allocation.Crossings.Count,
                PhysicalRouteCount = scene.Routes.Count
            },
            AllocationSamples = allocation.Diagnostics.Take(128).Select(diagnostic => new
            {
                Diagnostic = diagnostic,
                RelatedLinks = (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Concat(diagnostic.PhysicalLinkId is null ? Array.Empty<string>() : new[] { diagnostic.PhysicalLinkId }).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                RelatedRuns = (diagnostic.ConflictingRunIds ?? Array.Empty<string>()).Concat(diagnostic.RunId is null ? Array.Empty<string>() : new[] { diagnostic.RunId }).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Routes = routes.Routes.Where(route => (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(route.PhysicalLinkId, StringComparer.Ordinal) || route.PhysicalLinkId == diagnostic.PhysicalLinkId).Select(route => new { route.PhysicalLinkId, route.SourcePhysicalNodeId, route.DestinationPhysicalNodeId, route.Cells, route.IsComplete }).ToArray(),
                Runs = allocation.Runs.Where(run => (diagnostic.ConflictingRunIds ?? Array.Empty<string>()).Contains(run.RunId, StringComparer.Ordinal) || run.RunId == diagnostic.RunId).Select(run => new { run.RunId, run.PhysicalLinkId, run.Orientation, run.Cells, run.StartRouteIndex, run.EndRouteIndex }).ToArray(),
                LaneAssignments = allocation.RunAssignments.Where(item => (diagnostic.ConflictingRunIds ?? Array.Empty<string>()).Contains(item.RunId, StringComparer.Ordinal) || item.RunId == diagnostic.RunId).ToArray(),
                Terminals = allocation.Terminals.Where(item => item.PhysicalLinkId == diagnostic.PhysicalLinkId || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(item.PhysicalLinkId, StringComparer.Ordinal)).ToArray(),
                Approaches = allocation.Approaches.Where(item => item.PhysicalLinkId == diagnostic.PhysicalLinkId || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(item.PhysicalLinkId, StringComparer.Ordinal)).ToArray(),
                Handoffs = allocation.Handoffs.Where(item => item.PhysicalLinkId == diagnostic.PhysicalLinkId || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(item.PhysicalLinkId, StringComparer.Ordinal)).ToArray(),
                Bends = allocation.Bends.Where(item => item.PhysicalLinkId == diagnostic.PhysicalLinkId || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(item.PhysicalLinkId, StringComparer.Ordinal)).ToArray(),
                Crossings = allocation.Crossings.Where(item => item.HorizontalPhysicalLinkId == diagnostic.PhysicalLinkId || item.VerticalPhysicalLinkId == diagnostic.PhysicalLinkId || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(item.HorizontalPhysicalLinkId, StringComparer.Ordinal) || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(item.VerticalPhysicalLinkId, StringComparer.Ordinal)).ToArray(),
                PhysicalRoutes = scene.Routes.Where(route => route.PhysicalLinkId == diagnostic.PhysicalLinkId || (diagnostic.ConflictingPhysicalLinkIds ?? Array.Empty<string>()).Contains(route.PhysicalLinkId, StringComparer.Ordinal)).Select(route => new { route.PhysicalLinkId, route.Points, route.Segments }).ToArray()
            }).ToArray(),
            SceneSamples = scene.Diagnostics.Take(128).ToArray()
        };
        var endpointEvidence = allocation.Terminals.GroupBy(item => (item.PhysicalNodeId, item.EndpointKind))
            .Select(group =>
            {
                var node = scene.Nodes.FirstOrDefault(item => item.PhysicalNodeId == group.Key.PhysicalNodeId);
                var projectionNode = projection.PhysicalNodes.FirstOrDefault(item => item.PhysicalNodeId == group.Key.PhysicalNodeId);
                var terminals = group.Select(item =>
                {
                    var physicalTerminal = scene.Terminals.FirstOrDefault(candidate => candidate.PhysicalLinkId == item.PhysicalLinkId && candidate.EndpointKind == item.EndpointKind);
                    var route = scene.Routes.FirstOrDefault(candidate => candidate.PhysicalLinkId == item.PhysicalLinkId);
                    var drop = route is null || route.Points.Count < 2 ? null : (ArchitectureV7PhysicalPoint?)(item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture ? route.Points[1] : route.Points[route.Points.Count - 2]);
                    return new { item.PhysicalLinkId, item.Direction, item.SlotOrdinal, Terminal = physicalTerminal?.Position, Drop = drop };
                }).OrderBy(item => item.SlotOrdinal).ToArray();
                var horizontal = node is null || terminals.Length == 0 || terminals.All(item => item.Terminal is not null && (item.Terminal.Y == node.Bounds.Top || item.Terminal.Y == node.Bounds.Bottom));
                var axis = terminals.Where(item => item.Terminal is not null).Select(item => horizontal ? item.Terminal!.X : item.Terminal!.Y).ToArray();
                var orderedAxis = axis.OrderBy(value => value).ToArray();
                return new
                {
                    group.Key.PhysicalNodeId,
                    NodeName = projectionNode?.Name,
                    group.Key.EndpointKind,
                    NodeBounds = node?.Bounds,
                    LogicalSpan = placement.Nodes.FirstOrDefault(item => item.PhysicalNodeId == group.Key.PhysicalNodeId)?.LogicalSpan,
                    TerminalCount = terminals.Length,
                    DirectionalGroups = group.GroupBy(item => item.Direction.ToString()).ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal),
                    TerminalOccupiedSpan = axis.Length == 0 ? 0 : axis.Max() - axis.Min(),
                    MinimumTerminalSpacing = orderedAxis.Length < 2 ? 0 : orderedAxis.Zip(orderedAxis.Skip(1), (a, b) => b - a).Min(),
                    TerminalDropMismatches = terminals.Count(item => item.Terminal is not null && item.Drop is not null && (horizontal ? Math.Abs(item.Terminal.X - item.Drop.X) > .001 : Math.Abs(item.Terminal.Y - item.Drop.Y) > .001)),
                    Terminals = terminals
                };
            }).OrderByDescending(item => item.TerminalCount).ThenBy(item => item.NodeName, StringComparer.Ordinal).ToArray();
        var strict = mode == ArchitectureRenderingMode.StrictValidation;
        var findings = acceptance.Findings.Select(finding => new ValidationFinding(finding.Code, finding.SubjectId ?? finding.Stage, finding.SubjectId, null, 1, finding.Message, true)).ToArray();
        DrawioPage page;
        page = Measure("rendering", () => strict && !acceptance.IsStrictEligible
            ? RejectedPage()
            : new ArchitectureV7MechanicalDrawioRenderer().Render(diagram, projection, placement, scene, job.Rendering));
        var rendererFindings = Measure("renderer-fidelity", () => ValidateRendererFidelity(page, projection, scene));
        page = page with { Diagnostics = page.Diagnostics.Concat(rendererFindings).ToArray() };
        if (!string.IsNullOrWhiteSpace(job.PageNameHint)) page = page with { SuggestedName = job.PageNameHint!.Trim() };
        var rendererFidelity = new
        {
            Findings = rendererFindings,
            IsValid = rendererFindings.Length == 0,
            NodeCount = scene.Nodes.Count,
            EmittedArchitectureNodeCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("vertex") == "1" && ((string?)x.Attribute("id"))?.StartsWith("v7_node_", StringComparison.Ordinal) == true),
            EmittedContainerCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("vertex") == "1" && ((string?)x.Attribute("id"))?.StartsWith("v7_project_", StringComparison.Ordinal) == true),
            EmittedVertexCellCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("vertex") == "1"),
            RouteCount = scene.Routes.Count,
            EmittedRouteCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("edge") == "1"),
            PhysicalPointCount = scene.Routes.Sum(route => route.Points.Count),
            PhysicalSegmentCount = scene.Routes.Sum(route => route.Segments.Count),
            PhysicalDiagonalSegmentCount = scene.Routes.SelectMany(route => route.Points.Zip(route.Points.Skip(1), (a, b) => (a, b)))
                .Count(pair => pair.a.X != pair.b.X && pair.a.Y != pair.b.Y),
            RendererWaypointCount = RendererWaypointRuns(page).Sum(run => run.Count),
            RendererWaypointDiagonalCount = RendererWaypointRuns(page).Sum(run => run.Zip(run.Skip(1), (a, b) => (a, b))
                .Count(pair => pair.a.X != pair.b.X && pair.a.Y != pair.b.Y)),
            RendererEndpointDiagonalCount = RendererEndpointDiagonalCount(page, scene),
            ConfiguredBackground = job.Rendering.Canvas.BackgroundColor,
            EmittedBackground = (string?)page.GraphModel.Attribute("background"),
            EmittedPage = (string?)page.GraphModel.Attribute("page")
        };
        var acceptanceSummary = new
        {
            acceptance.Counts,
            acceptance.HardFailureCount,
            acceptance.IsStrictEligible,
            acceptance.IsNormalEligible,
            acceptance.ProjectionFingerprint,
            acceptance.OwnershipFingerprint,
            acceptance.SizingFingerprint,
            acceptance.ReservationFingerprint,
            acceptance.PlacementFingerprint,
            acceptance.RouteFingerprint,
            acceptance.AllocationFingerprint,
            acceptance.SceneFingerprint,
            acceptance.Metrics,
            FindingCodes = acceptance.Findings.Select(x => x.Code).ToArray()
        };
        var reportJson = JsonSerializer.Serialize(new
        {
            pipeline = "V7",
            input = new
            {
                targetProjectPath = job.InputPath ?? "<unspecified>",
                selectedProjectInput = job.ProjectSelectionInput ?? "<unspecified>",
                selectedProjects = diagram.Projects.Select(project => project.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                settingsPath = job.SettingsSourcePath ?? "<unspecified>",
                settingsSha256 = job.SettingsSourceHash ?? "<unspecified>",
                renderer = job.Rendering.OutputRenderer
            },
            rendererStyleEvidence = new
            {
                projectContainer = job.Rendering.ProjectContainerStyle,
                externalDependency = job.Rendering.ExternalDependencyStyle,
                connector = job.Rendering.Connector,
                resolvedNodeStyleSamples = projection.PhysicalNodes.OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal).Take(5)
                    .Select(node => new { node.PhysicalNodeId, node.Name, style = ArchitectureV7MechanicalDrawioRenderer.StyleEvidence(node, job.Rendering) }).ToArray()
            },
            acceptance = acceptanceSummary,
            rendererFidelity,
            stages = new
            {
                semantic = semanticEvidence,
                analyserInput = analyserInputEvidence,
                    projection = new { PhysicalNodeCount = projection.PhysicalNodes.Count, PhysicalLinkCount = projection.PhysicalLinks.Count, Fingerprint = projection.FreezeFingerprint },
                ownership = new { DecisionCount = ownership.Decisions.Count, Fingerprint = ownership.FreezeFingerprint },
                sizing = new { RequirementCount = sizing.Requirements.Count, Fingerprint = sizing.FreezeFingerprint },
                reservation = new { ReservationCount = scheduledReservation.Table.Reservations.Count, Fingerprint = scheduledReservation.Table.Fingerprint },
                ordinaryLayerSchedule = new { Fingerprint = ordinarySchedule.Fingerprint, Diagnostics = ordinarySchedule.Diagnostics, AssignedNodeCount = ordinarySchedule.LayerByPhysicalNodeId.Count },
                placement = new { ProjectCount = placement.Projects.Count, NodeCount = placement.Nodes.Count, Fingerprint = placement.PlacementFingerprint },
                corridorDiscovery = new
                {
                    HorizontalCount = corridorDiscovery.Horizontal.Count,
                    VerticalCount = corridorDiscovery.Vertical.Count,
                    HorizontalLengths = new { Minimum = corridorDiscovery.Horizontal.Count == 0 ? 0 : corridorDiscovery.Horizontal.Min(item => item.CellCount), Maximum = corridorDiscovery.Horizontal.Count == 0 ? 0 : corridorDiscovery.Horizontal.Max(item => item.CellCount), Average = corridorDiscovery.Horizontal.Count == 0 ? 0 : corridorDiscovery.Horizontal.Average(item => item.CellCount) },
                    VerticalLengths = new { Minimum = corridorDiscovery.Vertical.Count == 0 ? 0 : corridorDiscovery.Vertical.Min(item => item.CellCount), Maximum = corridorDiscovery.Vertical.Count == 0 ? 0 : corridorDiscovery.Vertical.Max(item => item.CellCount), Average = corridorDiscovery.Vertical.Count == 0 ? 0 : corridorDiscovery.Vertical.Average(item => item.CellCount) },
                    VerticalNodePassthroughCount = corridorDiscovery.Vertical.Count(item => item.CapabilityClasses.Any(capability => capability.HasFlag(ArchitectureV7CellCapability.NodeAllowed))),
                    StraightOnlyCount = corridorDiscovery.All.Count(item => item.CapabilityClasses.Any(capability => capability.HasFlag(ArchitectureV7CellCapability.StraightPassthroughOnly))),
                    Fingerprint = corridorDiscovery.Fingerprint
                },
                routing = new
                {
                    RouteCount = routes.Routes.Count,
                    CompleteRouteCount = routes.Routes.Count(x => x.IsComplete),
                    MaxContinuationCandidatesEvaluated = routes.Routes.Count == 0 ? 0 : routes.Routes.Max(x => x.OperationMetrics.ContinuationCandidatesEvaluated),
                    MaxUpwardEscapeCandidatesEvaluated = routes.Routes.Count == 0 ? 0 : routes.Routes.Max(x => x.OperationMetrics.UpwardEscapeCandidatesEvaluated),
                    TotalContinuationCandidatesEvaluated = routes.Routes.Sum(x => x.OperationMetrics.ContinuationCandidatesEvaluated),
                    TotalUpwardEscapeCandidatesEvaluated = routes.Routes.Sum(x => x.OperationMetrics.UpwardEscapeCandidatesEvaluated),
                    Fingerprint = routes.RouteFingerprint
                },
                corridorProjection = new
                {
                    UsageCount = corridorProjection.Usages.Count,
                    UnprojectedRunCount = corridorProjection.UnprojectedRuns.Count,
                    UsageIds = corridorProjection.Usages.Select(item => item.UsageId).ToArray(),
                    Fingerprint = corridorProjection.Fingerprint
                },
                stageTimings,
                allocation = new { AssignmentCount = allocation.RunAssignments.Count, Fingerprint = allocation.AllocationFingerprint },
                simplification = new
                {
                    RouteCount = simplification.Evidence.Count,
                    PointsBefore = simplification.Evidence.Sum(item => item.PointsBefore),
                    PointsAfter = simplification.Evidence.Sum(item => item.PointsAfter),
                    RedundantPointsRemoved = simplification.Evidence.Sum(item => item.RedundantPointsRemoved),
                    ReversalTransitions = simplification.Evidence.Sum(item => item.ReversalTransitions),
                     OvershootGroups = simplification.Evidence.Sum(item => item.OvershootGroups),
                     SameAxisLaneMismatches = simplification.Evidence.Sum(item => item.SameAxisLaneMismatchCount),
                     DiagonalSegments = simplification.Evidence.Sum(item => item.DiagonalSegmentCount),
                     Evidence = simplification.Evidence
                },
                scene = new { NodeCount = scene.Nodes.Count, RouteCount = scene.Routes.Count, DiagnosticCount = scene.Diagnostics.Count, scene.PhysicalSceneFingerprint },
            }
        }, new JsonSerializerOptions { WriteIndented = true });
        var manifest = new ArchitectureGenerationManifest(diagram.Projects.Count, projection.PhysicalNodes.Count, diagram.Links.Count, routes.Routes.Count,
            acceptance.Findings.Count, acceptance.Findings.Count, page.StablePageKey)
        {
            ProjectedRenderNodeCount = projection.PhysicalNodes.Count, ProjectedRenderLinkCount = projection.PhysicalLinks.Count,
            DuplicatedInstanceCount = projection.PhysicalNodes.Count(x => x.ProjectionMode == ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches),
            CanonicalSharedNodeCount = projection.PhysicalNodes.Count(x => x.ProjectionMode == ArchitectureV7ProjectionMode.Canonical),
            ExceptionAuthorisedDuplicateCount = projection.PhysicalNodes.Count(x => x.DuplicationProvenance is not null)
        };
        var eligibility = new ArchitectureEligibilityResult(!strict || acceptance.IsStrictEligible, acceptance.Findings.Select(x => x.Code + ": " + x.Message).ToArray());
        return Task.FromResult(new TypedArchitectureGenerationResult(diagram, page, findings, manifest, eligibility,
            () => new DrawioDiagnosticExportResult(page.GraphModel.ToString(), reportJson,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["final-v7-acceptance-report.json"] = JsonSerializer.Serialize(acceptanceSummary, new JsonSerializerOptions { WriteIndented = true }),
                    ["renderer-fidelity-report.json"] = JsonSerializer.Serialize(rendererFidelity, new JsonSerializerOptions { WriteIndented = true })
                    , ["v7-routing-placement-evidence.json"] = JsonSerializer.Serialize(new { routingEvidence, placementEvidence, representative, allocationSceneEvidence }, new JsonSerializerOptions { WriteIndented = true })
                    , ["v7-endpoint-region-evidence.json"] = JsonSerializer.Serialize(endpointEvidence, new JsonSerializerOptions { WriteIndented = true })
                    , ["v7-route-corridor-usage-evidence.json"] = JsonSerializer.Serialize(corridorProjection, new JsonSerializerOptions { WriteIndented = true })
                    , ["v7-route-simplification-evidence.json"] = JsonSerializer.Serialize(new
                    {
                        simplification.Evidence,
                        sceneDiagnostics = scene.Diagnostics,
                        routes = scene.Routes.Select(route => new
                        {
                            route.PhysicalLinkId,
                            Points = route.Points.Select(point => new { point.X, point.Y, point.Provenance }).ToArray(),
                            Segments = route.Segments.Select(segment => new { segment.RunId, segment.LaneId, segment.AllocationProvenance }).ToArray()
                        }).ToArray()
                    }, new JsonSerializerOptions { WriteIndented = true })
                }, acceptance.HardFailureCount + rendererFindings.Length, routes.Routes.Count(x => !x.IsComplete)),
            serializationRepeatCount > 0 ? new SerializationRepeatResult(serializationRepeatCount, true, Array.Empty<string>()) : null));
    }

    private static object SemanticEvidence(ArchitectureDiagramModel diagram)
    {
        var nodes = ArchitectureV7SemanticIdentity.SortedNodeKeys(diagram).ToArray();
        var relationships = ArchitectureV7SemanticIdentity.SortedRelationshipKeys(diagram).ToArray();
        var payload = string.Join("\n", nodes) + "\n--links--\n" + string.Join("\n", relationships);
        using var sha = SHA256.Create();
        var fingerprint = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        return new
        {
            NodeCount = nodes.Length,
            RelationshipCount = relationships.Length,
            PopulationFingerprint = fingerprint,
            StableNodeKeys = nodes,
            StableRelationshipKeys = relationships
        };

    }

    private static object AnalyserInputEvidence(ArchitectureGenerationJob job)
    {
        var inputPath = job.InputPath;
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath) ||
            !string.Equals(Path.GetExtension(inputPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return new { Resolved = false, TargetProjectPath = inputPath ?? "<unspecified>" };
        }

        var projectPath = Path.GetFullPath(inputPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourceFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildPath(path))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var projectReferences = XDocument.Load(projectPath).Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
            .Select(element => (string?)element.Attribute("Include"))
            .Where(path => !string.IsNullOrWhiteSpace(path) && !path!.Contains("$(", StringComparison.Ordinal))
            .Select(path => Path.GetFullPath(Path.Combine(projectDirectory, path!)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicatePatterns = (job.Rendering.NodeDuplication.DuplicationExceptionPatterns ?? new List<string>())
            .Concat(job.Rendering.Layout.DuplicateHighNoiseNodePatterns ?? new List<string>())
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var manifest = new
        {
            TargetProjectPath = projectPath,
            ProjectName = Path.GetFileNameWithoutExtension(projectPath),
            Configuration = "Debug",
            SourceFiles = sourceFiles,
            ProjectReferences = projectReferences,
            ExplicitSourceExclusions = Array.Empty<string>(),
            AnalyserOptions = new
            {
                ExcludedNames = job.Analysis.ExcludedNames.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                ExcludedNamespaces = job.Analysis.ExcludedNamespaces.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                job.Analysis.RootDiscoveryPatternsText,
                job.Analysis.ExternalDependencyTag
            },
            DuplicateMode = job.Rendering.NodeDuplication.AllowDuplicateNodes ? "configured-duplicate" : "canonical",
            DuplicationExceptionPatterns = duplicatePatterns,
            Cache = "none-observed"
        };
        var canonical = JsonSerializer.Serialize(manifest);
        using var sha = SHA256.Create();
        var fingerprint = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        return new
        {
            Resolved = true,
            AnalyserInputFingerprint = fingerprint,
            SourceFileCount = sourceFiles.Length,
            ProjectReferenceCount = projectReferences.Length,
            manifest
        };

        static bool IsBuildPath(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static DiagramDiagnostic[] ValidateRendererFidelity(DrawioPage page, ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PhysicalSceneFreeze scene)
    {
        var cells = page.GraphModel.Descendants("mxCell").ToArray();
        var emittedNodes = cells.Where(x => (string?)x.Attribute("vertex") == "1").ToArray();
        var emittedArchitectureNodes = emittedNodes.Where(x => ((string?)x.Attribute("id"))?.StartsWith("v7_node_", StringComparison.Ordinal) == true).ToArray();
        var emittedEdges = cells.Where(x => (string?)x.Attribute("edge") == "1").ToArray();
        var cellsById = cells.Where(x => x.Attribute("id") is not null).ToDictionary(x => (string)x.Attribute("id")!, StringComparer.Ordinal);
        var findings = new List<DiagramDiagnostic>();
        if (emittedArchitectureNodes.Length != scene.Nodes.Count) findings.Add(new DiagramDiagnostic("V7RendererNodeCount", $"Renderer emitted {emittedArchitectureNodes.Length} architecture nodes; scene contains {scene.Nodes.Count}."));
        if (emittedEdges.Length != scene.Routes.Count) findings.Add(new DiagramDiagnostic("V7RendererRouteCount", $"Renderer emitted {emittedEdges.Length} edges; scene contains {scene.Routes.Count}."));
        foreach (var node in scene.Nodes)
        {
            var cell = cells.FirstOrDefault(x => (string?)x.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("node", node.PhysicalNodeId));
            var bounds = cell is null ? null : AbsoluteBounds(cell, cellsById);
            if (bounds is null || !Equal(node.Bounds.Left, bounds.Left) || !Equal(node.Bounds.Top, bounds.Top) || !Equal(node.Bounds.Right - node.Bounds.Left, bounds.Right - bounds.Left) || !Equal(node.Bounds.Bottom - node.Bounds.Top, bounds.Bottom - bounds.Top))
                findings.Add(new DiagramDiagnostic("V7RendererNodeBounds", $"Renderer node geometry does not match frozen bounds for {node.PhysicalNodeId}.", node.PhysicalNodeId));
        }
        foreach (var route in scene.Routes)
        {
            var cell = cells.FirstOrDefault(x => (string?)x.Attribute("physicalLinkId") == route.PhysicalLinkId);
            var points = cell?.Element("mxGeometry")?.Element("Array")?.Elements("mxPoint").ToArray() ?? Array.Empty<XElement>();
            var expected = route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).ToArray();
            if (cell is null || points.Length != expected.Length || points.Zip(expected, (actual, wanted) => Equal(wanted.X, actual.Attribute("x")) && Equal(wanted.Y, actual.Attribute("y"))).Any(x => !x))
                findings.Add(new DiagramDiagnostic("V7RendererRouteGeometry", $"Renderer route geometry does not match frozen route {route.PhysicalLinkId}.", route.PhysicalLinkId));
            var source = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var target = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
            var emittedSource = ReadPoint(cell, "v7SourceTerminalX", "v7SourceTerminalY");
            var emittedTarget = ReadPoint(cell, "v7TargetTerminalX", "v7TargetTerminalY");
            if (source is null || target is null || emittedSource is null || emittedTarget is null ||
                !Equal(source.Position.X, cell?.Attribute("v7SourceTerminalX")) || !Equal(source.Position.Y, cell?.Attribute("v7SourceTerminalY")) ||
                !Equal(target.Position.X, cell?.Attribute("v7TargetTerminalX")) || !Equal(target.Position.Y, cell?.Attribute("v7TargetTerminalY")))
                findings.Add(new DiagramDiagnostic("V7RendererTerminalGeometry", $"Renderer terminal coordinates do not exactly match the frozen terminal geometry for {route.PhysicalLinkId}.", route.PhysicalLinkId));
            var full = emittedSource is null || emittedTarget is null ? Array.Empty<(double X, double Y)>() : new[] { emittedSource.Value }
                .Concat(points.Select(point => (double.Parse((string)point.Attribute("x")!, CultureInfo.InvariantCulture), double.Parse((string)point.Attribute("y")!, CultureInfo.InvariantCulture))))
                .Append(emittedTarget.Value).ToArray();
            if (full.Zip(full.Skip(1), (a, b) => (a, b)).Any(pair => pair.a.Item1 != pair.b.Item1 && pair.a.Item2 != pair.b.Item2))
                findings.Add(new DiagramDiagnostic("V7RendererEndpointDiagonal", $"Renderer endpoint-to-waypoint geometry is diagonal for {route.PhysicalLinkId}.", route.PhysicalLinkId));
            if (HasRendererAxisReversal(full))
                findings.Add(new DiagramDiagnostic("V7RendererAxisReversal", $"Renderer endpoint-inclusive geometry reverses on a straight axis for {route.PhysicalLinkId}.", route.PhysicalLinkId));
            var link = projection.PhysicalLinks.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId);
            if (link is not null)
                foreach (var node in scene.Nodes.Where(item => item.PhysicalNodeId != link.SourcePhysicalNodeId && item.PhysicalNodeId != link.DestinationPhysicalNodeId))
                {
                    var emittedNode = cellsById.TryGetValue(ArchitectureV7MechanicalDrawioRenderer.IdFor("node", node.PhysicalNodeId), out var emittedNodeCell) ? AbsoluteBounds(emittedNodeCell, cellsById) : null;
                    if (emittedNode is not null && full.Zip(full.Skip(1), (start, end) => (start, end)).Any(segment => Intersects(emittedNode, segment.Item1, segment.Item2)))
                        findings.Add(new DiagramDiagnostic("V7RendererRouteThroughNode", $"Emitted route geometry intersects unrelated node {node.PhysicalNodeId} for {route.PhysicalLinkId} (intersected-node={node.PhysicalNodeId}).", route.PhysicalLinkId));
                }
        }
        return findings.ToArray();
    }

    private static (double X, double Y)? ReadPoint(XElement? cell, string xName, string yName) =>
        cell is not null && double.TryParse((string?)cell.Attribute(xName), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
        double.TryParse((string?)cell.Attribute(yName), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? (x, y) : null;
    private static ArchitectureV7PhysicalBounds AbsoluteBounds(XElement cell, IReadOnlyDictionary<string, XElement> cells)
    {
        var geometry = cell.Element("mxGeometry")!;
        var origin = AbsoluteOrigin(cell, cells);
        return new ArchitectureV7PhysicalBounds(origin.X, origin.Y, origin.X + double.Parse((string)geometry.Attribute("width")!, CultureInfo.InvariantCulture), origin.Y + double.Parse((string)geometry.Attribute("height")!, CultureInfo.InvariantCulture));
    }
    private static (double X, double Y) AbsoluteOrigin(XElement cell, IReadOnlyDictionary<string, XElement> cells)
    {
        var geometry = cell.Element("mxGeometry");
        var x = double.TryParse((string?)geometry?.Attribute("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out var localX) ? localX : 0;
        var y = double.TryParse((string?)geometry?.Attribute("y"), NumberStyles.Float, CultureInfo.InvariantCulture, out var localY) ? localY : 0;
        var parentId = (string?)cell.Attribute("parent");
        if (parentId is not null && cells.TryGetValue(parentId, out var parent) && parentId != "0" && parentId != "1")
        {
            var parentOrigin = AbsoluteOrigin(parent, cells);
            x += parentOrigin.X;
            y += parentOrigin.Y;
        }
        return (x, y);
    }
    private static bool Intersects(ArchitectureV7PhysicalBounds bounds, (double X, double Y) start, (double X, double Y) end)
    {
        if (start.X == end.X)
            return start.X >= bounds.Left && start.X <= bounds.Right && Math.Max(Math.Min(start.Y, end.Y), bounds.Top) <= Math.Min(Math.Max(start.Y, end.Y), bounds.Bottom);
        if (start.Y == end.Y)
            return start.Y >= bounds.Top && start.Y <= bounds.Bottom && Math.Max(Math.Min(start.X, end.X), bounds.Left) <= Math.Min(Math.Max(start.X, end.X), bounds.Right);
        return false;
    }

    private static bool Equal(double expected, XAttribute? actual) => actual is not null && double.TryParse(actual.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(expected - value) < 0.0001;
    private static bool Equal(double expected, double actual) => Math.Abs(expected - actual) < 0.0001;
    private static bool HasRendererAxisReversal(IReadOnlyList<(double X, double Y)> points)
    {
        var horizontalDirections = points.Zip(points.Skip(1), (a, b) => (a, b))
            .Where(pair => pair.a.Y == pair.b.Y && pair.a.X != pair.b.X)
            .Select(pair => Math.Sign(pair.b.X - pair.a.X)).ToArray();
        var verticalDirections = points.Zip(points.Skip(1), (a, b) => (a, b))
            .Where(pair => pair.a.X == pair.b.X && pair.a.Y != pair.b.Y)
            .Select(pair => Math.Sign(pair.b.Y - pair.a.Y)).ToArray();
        return HasReversal(horizontalDirections) || HasReversal(verticalDirections);

        static bool HasReversal(IReadOnlyList<int> directions)
        {
            for (var index = 1; index < directions.Count; index++)
                if (directions[index] != directions[index - 1]) return true;
            return false;
        }
    }
    private static IEnumerable<IReadOnlyList<(double X, double Y)>> RendererWaypointRuns(DrawioPage page) => page.GraphModel.Descendants("mxCell")
        .Where(cell => (string?)cell.Attribute("edge") == "1")
        .Select(cell => (IReadOnlyList<(double X, double Y)>)cell.Descendants("Array").Where(array => (string?)array.Attribute("as") == "points").Elements("mxPoint")
            .Select(point => (double.Parse((string)point.Attribute("x")!, CultureInfo.InvariantCulture), double.Parse((string)point.Attribute("y")!, CultureInfo.InvariantCulture)))
            .ToArray());
    private static int RendererEndpointDiagonalCount(DrawioPage page, ArchitectureV7PhysicalSceneFreeze scene)
    {
        var count = 0;
        foreach (var route in scene.Routes)
        {
            var source = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var target = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
            var cell = page.GraphModel.Descendants("mxCell").FirstOrDefault(item => (string?)item.Attribute("physicalLinkId") == route.PhysicalLinkId);
            if (source is null || target is null || cell is null) continue;
            var points = cell.Element("mxGeometry")?.Element("Array")?.Elements("mxPoint")
                .Select(point => (double.Parse((string)point.Attribute("x")!, CultureInfo.InvariantCulture), double.Parse((string)point.Attribute("y")!, CultureInfo.InvariantCulture)))
                .ToArray() ?? Array.Empty<(double X, double Y)>();
            var full = new[] { (source.Position.X, source.Position.Y) }.Concat(points).Append((target.Position.X, target.Position.Y)).ToArray();
            count += full.Zip(full.Skip(1), (a, b) => (a, b)).Count(pair => Math.Abs(pair.a.Item1 - pair.b.Item1) > 0.01 && Math.Abs(pair.a.Item2 - pair.b.Item2) > 0.01);
        }
        return count;
    }

    private static ArchitectureV7PrePlacementConfiguration Configuration(LayoutSettings layout) => new(layout.BaseCellWidth, layout.NodeWidth, layout.LabelCharacterWidth, layout.LinkNodeWidthPadding,
        Math.Max(1, layout.EdgePortSpacing), Math.Max(0, layout.LinkNodeWidthPadding), (layout.ReservedLayerTypePatterns ?? new List<string>()).Select((pattern, index) => new ArchitectureV7ReservedRoleRule(pattern, pattern, index)).ToArray());
    private static DrawioPage RejectedPage() => new("Architecture (rejected)", "architecture-rejected", new XElement("mxGraphModel", new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")))), new[] { new DiagramDiagnostic("V7StrictRejected", "V7 acceptance failed; Draw.io renderer was not invoked.") });
}

internal sealed class ArchitectureV7MechanicalDrawioRenderer
{
    public DrawioPage Render(ArchitectureDiagramModel diagram, ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7PhysicalSceneFreeze scene, ArchitectureRenderSettings settings)
    {
        var root = new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var projectOrigins = new Dictionary<string, (double Left, double Top)>(StringComparer.Ordinal);
        foreach (var project in placement.Projects.OrderBy(x => x.ProjectId, StringComparer.Ordinal))
        {
            var transform = project.Transform; var id = Id("project", project.ProjectId);
            var projectNodeIds = new HashSet<string>(projection.PhysicalNodes.Where(item => string.Equals(item.ProjectId, project.ProjectId, StringComparison.Ordinal) && !item.IsExternal && !item.IsStandalone).Select(item => item.PhysicalNodeId), StringComparer.Ordinal);
            var projectNodes = scene.Nodes.Where(item => projectNodeIds.Contains(item.PhysicalNodeId)).ToArray();
            var left = projectNodes.Length == 0 ? 0 : projectNodes.Min(item => item.Bounds.Left);
            var top = projectNodes.Length == 0 ? 0 : projectNodes.Min(item => item.Bounds.Top);
            var right = projectNodes.Length == 0 ? 0 : projectNodes.Max(item => item.Bounds.Right);
            var bottom = projectNodes.Length == 0 ? 0 : projectNodes.Max(item => item.Bounds.Bottom);
            projectOrigins[project.ProjectId] = (left, top);
            if (settings.ShowProjectContainers)
                root.Add(Vertex(id, project.ProjectId, Style(settings.ProjectContainerStyle), "1", left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)));
        }
        foreach (var node in scene.Nodes.OrderBy(x => x.PhysicalNodeId, StringComparer.Ordinal))
        {
            var source = projection.PhysicalNodes.FirstOrDefault(x => x.PhysicalNodeId == node.PhysicalNodeId); if (source is null) continue;
            var id = Id("node", node.PhysicalNodeId); nodes[node.PhysicalNodeId] = id;
            var parent = source.ProjectId is not null && placement.Projects.Any(x => x.ProjectId == source.ProjectId) ? Id("project", source.ProjectId) : "1";
            var nodeX = node.Bounds.Left;
            var nodeY = node.Bounds.Top;
            if (source.ProjectId is not null && projectOrigins.TryGetValue(source.ProjectId, out var origin))
            {
                nodeX -= origin.Left;
                nodeY -= origin.Top;
            }
            root.Add(Vertex(id, source.Name, source.IsExternal ? Style(settings.ExternalDependencyStyle) : Style(ResolveNodeStyle(source, settings)), parent, nodeX, nodeY, node.Bounds.Right - node.Bounds.Left, node.Bounds.Bottom - node.Bounds.Top));
        }
        foreach (var link in projection.PhysicalLinks.OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal))
        {
            var route = scene.Routes.FirstOrDefault(x => x.PhysicalLinkId == link.PhysicalLinkId); if (route is null || !nodes.ContainsKey(link.SourcePhysicalNodeId) || !nodes.ContainsKey(link.DestinationPhysicalNodeId)) continue;
            var points = route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).Select(point => new XElement("mxPoint", new XAttribute("x", point.X.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", point.Y.ToString(CultureInfo.InvariantCulture))));
            var sourceTerminal = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
            var targetTerminal = scene.Terminals.FirstOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId && item.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
            var sourceNode = scene.Nodes.FirstOrDefault(item => item.PhysicalNodeId == link.SourcePhysicalNodeId);
            var targetNode = scene.Nodes.FirstOrDefault(item => item.PhysicalNodeId == link.DestinationPhysicalNodeId);
            var targetProjection = projection.PhysicalNodes.FirstOrDefault(item => item.PhysicalNodeId == link.DestinationPhysicalNodeId);
            var targetStyle = targetProjection is null ? new NodeStyle() : targetProjection.IsExternal ? settings.ExternalDependencyStyle : ResolveNodeStyle(targetProjection, settings);
            var edge = new XElement("mxCell", new XAttribute("id", Id("edge", link.PhysicalLinkId)), new XAttribute("parent", "1"), new XAttribute("edge", "1"), new XAttribute("source", nodes[link.SourcePhysicalNodeId]), new XAttribute("target", nodes[link.DestinationPhysicalNodeId]), new XAttribute("physicalLinkId", link.PhysicalLinkId), new XAttribute("semanticLinkId", link.SemanticLinkId), new XAttribute("style", ConnectorStyle(settings.Connector, targetStyle.FillColor, sourceTerminal?.Position, sourceNode?.Bounds, targetTerminal?.Position, targetNode?.Bounds)), new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"), new XElement("Array", new XAttribute("as", "points"), points)));
            if (sourceTerminal is not null)
            {
                edge.Add(new XAttribute("v7SourceTerminalX", sourceTerminal.Position.X.ToString("G17", CultureInfo.InvariantCulture)));
                edge.Add(new XAttribute("v7SourceTerminalY", sourceTerminal.Position.Y.ToString("G17", CultureInfo.InvariantCulture)));
            }
            if (targetTerminal is not null)
            {
                edge.Add(new XAttribute("v7TargetTerminalX", targetTerminal.Position.X.ToString("G17", CultureInfo.InvariantCulture)));
                edge.Add(new XAttribute("v7TargetTerminalY", targetTerminal.Position.Y.ToString("G17", CultureInfo.InvariantCulture)));
            }
            root.Add(edge);
        }
        var graph = GraphModel(root, settings.Canvas.BackgroundColor);
        return new DrawioPage("Architecture", "architecture", graph, Array.Empty<DiagramDiagnostic>());
    }
    internal static XElement GraphModelForTest(string background) => GraphModel(new XElement("root"), background);
    private static XElement GraphModel(XElement root, string background) => new("mxGraphModel", new XAttribute("grid", "0"), new XAttribute("page", "1"), new XAttribute("adaptiveColors", "none"), new XAttribute("background", background), root);
    private static XElement Vertex(string id, string value, string style, string parent, double x, double y, double width, double height) => new("mxCell", new XAttribute("id", id), new XAttribute("value", value), new XAttribute("style", style), new XAttribute("vertex", "1"), new XAttribute("parent", parent), new XElement("mxGeometry", new XAttribute("x", x.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", y.ToString(CultureInfo.InvariantCulture)), new XAttribute("width", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("height", height.ToString(CultureInfo.InvariantCulture)), new XAttribute("as", "geometry")));
    internal static object StyleEvidence(ArchitectureV7PhysicalNode node, ArchitectureRenderSettings settings) => new
    {
        source = node.IsExternal ? "external-dependency-style" : ResolveNodeStyleSource(node, settings),
        style = node.IsExternal ? settings.ExternalDependencyStyle : ResolveNodeStyle(node, settings)
    };
    private static string ResolveNodeStyleSource(ArchitectureV7PhysicalNode node, ArchitectureRenderSettings settings) =>
        settings.Overrides.Any(item => string.Equals(item.FullName, node.FullName, StringComparison.Ordinal)) ? "exact-override" :
        settings.StyleRules.FirstOrDefault(rule => GlobMatcher.IsMatch(node.Name, rule.Match) || GlobMatcher.IsMatch(node.FullName, rule.Match))?.Match ?? "default-node-style";
    private static NodeStyle ResolveNodeStyle(ArchitectureV7PhysicalNode node, ArchitectureRenderSettings settings) =>
        settings.Overrides.FirstOrDefault(item => string.Equals(item.FullName, node.FullName, StringComparison.Ordinal))?.Style ??
        settings.StyleRules.FirstOrDefault(rule => GlobMatcher.IsMatch(node.Name, rule.Match) || GlobMatcher.IsMatch(node.FullName, rule.Match))?.Style ?? new NodeStyle();
    private static string Style(NodeStyle style) => string.Join(";", new[]
    {
        $"shape={style.Shape}", "html=1", "whiteSpace=wrap", $"fillColor={style.FillColor}", $"strokeColor={style.StrokeColor}", $"fontColor={style.FontColor}", $"shadow={(style.Shadow ? 1 : 0)}", style.ExtraStyle
    }.Where(value => !string.IsNullOrWhiteSpace(value))) + ";";
    private static string ConnectorStyle(ConnectorStyle style, string targetFillColor, ArchitectureV7PhysicalPoint? source, ArchitectureV7PhysicalBounds? sourceBounds,
        ArchitectureV7PhysicalPoint? target, ArchitectureV7PhysicalBounds? targetBounds) => string.Join(";", new[]
    {
        "edgeStyle=none", "orthogonal=0", "jettySize=0", "curved=0", "exitPerimeter=0", "entryPerimeter=0", $"rounded={(style.Rounded ? 1 : 0)}", $"strokeColor={targetFillColor}", $"strokeWidth={style.StrokeWidth}", $"opacity={style.Opacity}", $"startArrow={style.StartArrow}", $"endArrow={style.EndArrow}", $"startFill={(style.StartFill ? 1 : 0)}", $"endFill={(style.EndFill ? 1 : 0)}", $"fontColor={style.FontColor}", "labelPosition=none", style.ExtraStyle
        , ConnectionPoint("exit", source, sourceBounds), ConnectionPoint("entry", target, targetBounds)
    }.Where(value => !string.IsNullOrWhiteSpace(value))) + ";";
    private static string? ConnectionPoint(string prefix, ArchitectureV7PhysicalPoint? point, ArchitectureV7PhysicalBounds? bounds)
    {
        if (point is null || bounds is null || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return null;
        var x = Math.Min(1d, Math.Max(0d, (point.X - bounds.Left) / (bounds.Right - bounds.Left)));
        var y = Math.Min(1d, Math.Max(0d, (point.Y - bounds.Top) / (bounds.Bottom - bounds.Top)));
        return prefix + "X=" + x.ToString("G17", CultureInfo.InvariantCulture) + ";" + prefix + "Y=" + y.ToString("G17", CultureInfo.InvariantCulture);
    }
    internal static string IdFor(string kind, string value) { using var sha = SHA256.Create(); return "v7_" + kind + "_" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Take(8).Select(x => x.ToString("x2", CultureInfo.InvariantCulture))); }
    private static string Id(string kind, string value) => IdFor(kind, value);
}
