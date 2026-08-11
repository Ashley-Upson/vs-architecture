using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        var projection = Measure("projection", () => new ArchitectureV7PhysicalProjectionStage().Project(diagram, new ArchitectureV7ProjectionPolicy(
            job.Rendering.NodeDuplication.AllowDuplicateNodes ? ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches : ArchitectureV7ProjectionMode.Canonical,
            (job.Rendering.NodeDuplication.DuplicationExceptionPatterns ?? new List<string>()).Concat(job.Rendering.Layout.DuplicateHighNoiseNodePatterns ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray())));
        var ownership = Measure("ownership", () => new ArchitectureV7PositionalOwnershipStage().Resolve(projection));
        var softCohorts = Measure("soft-cohort-analysis", () => new ArchitectureV7SoftCohortAnalyzer().Analyze(ownership, pre));
        var sizing = Measure("sizing", () => new ArchitectureV7PreRoutingNodeSpanSizer().Size(ownership, pre));
        var reservation = Measure("reservation", () => new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, pre)));
        var layerSchedule = Measure("soft-layer-scheduling", () => new ArchitectureV7SoftLayerSchedulingStage().Schedule(ownership, reservation, softCohorts));
        var scheduledReservation = new ArchitectureV7ReservationReconciliationResult(reservation.Inspection, layerSchedule.Reservations);
        var trees = Measure("recursive-placement", () => new ArchitectureV7RecursiveTreeGridStage().Build(sizing, layerSchedule));
        var placement = Measure("project-composition", () => new ArchitectureV7ProjectCompositionStage().Compose(trees, pre));
        var routes = Measure("logical-routing", () => new ArchitectureV7LogicalRelationshipRoutingStage().Route(placement, projection));
        var allocation = Measure("collective-allocation", () => new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routes,
            new ArchitectureV7AllocationConfiguration(job.Rendering.Layout.ParallelLaneSpacing, job.Rendering.Layout.EdgePortSpacing, job.Rendering.Layout.LinkNodeWidthPadding, job.Rendering.Layout.BaseCellWidth,
                job.Rendering.Layout.LinkPadding)));
        var sceneConfiguration = new ArchitectureV7PhysicalSceneConfiguration(job.Rendering.Layout.BaseCellWidth, job.Rendering.Layout.RoutingRowMinimum,
            job.Rendering.Layout.BoundaryRowMinimum, job.Rendering.Layout.NodeWidth, job.Rendering.Layout.NodeHeight, job.Rendering.Layout.ProjectHeaderHeight,
            job.Rendering.Layout.LabelCharacterWidth, job.Rendering.Layout.LinkNodeWidthPadding, job.Rendering.Layout.LinkPadding,
            job.Rendering.Layout.VerticalNodeClearance, job.Rendering.Layout.ParallelLaneSpacing, job.Rendering.Layout.EdgePortSpacing, job.Rendering.Layout.LinkNodeWidthPadding);
        var scene = Measure("physical-sizing-scene-compilation", () => new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routes, allocation, sceneConfiguration,
            sizing.Requirements.ToDictionary(item => item.PhysicalNodeId, item => item.RequiredWidth, StringComparer.Ordinal)));
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
            EmittedRouteCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("edge") == "1")
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
                projection = new { PhysicalNodeCount = projection.PhysicalNodes.Count, PhysicalLinkCount = projection.PhysicalLinks.Count, Fingerprint = projection.FreezeFingerprint },
                ownership = new { DecisionCount = ownership.Decisions.Count, Fingerprint = ownership.FreezeFingerprint },
                softCohorts = new { CohortCount = softCohorts.Cohorts.Count, MinimumSize = softCohorts.MinimumSize, Fingerprint = softCohorts.Fingerprint },
                sizing = new { RequirementCount = sizing.Requirements.Count, Fingerprint = sizing.FreezeFingerprint },
                reservation = new { ReservationCount = scheduledReservation.Table.Reservations.Count, Fingerprint = scheduledReservation.Table.Fingerprint, PreSoftFingerprint = layerSchedule.PreSoftFingerprint },
                softLayerSchedule = new
                {
                    Fingerprint = layerSchedule.Fingerprint,
                    Entries = layerSchedule.Entries,
                    Preferences = layerSchedule.SoftPreferences,
                    Diagnostics = layerSchedule.Diagnostics,
                    PreSoftReservations = layerSchedule.PreSoftReservations.Select(item => new { item.Name, item.NodeRow }).ToArray(),
                    InsertedNodeLayers = layerSchedule.Entries.Where(item => !item.IsHardReservation && item.TokenSuffix is not null).Select(item => item.NodeLayer).ToArray(),
                    ReusedOrdinaryLayers = layerSchedule.Entries.Where(item => !item.IsHardReservation && item.Name.StartsWith("ordinary:", StringComparison.Ordinal)).Select(item => item.NodeLayer).ToArray(),
                    ShiftedHardReservations = scheduledReservation.Table.Reservations.Select(item => new { item.Name, item.NodeRow }).ToArray()
                },
                placement = new { ProjectCount = placement.Projects.Count, NodeCount = placement.Nodes.Count, Fingerprint = placement.PlacementFingerprint },
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
                stageTimings,
                allocation = new { AssignmentCount = allocation.RunAssignments.Count, Fingerprint = allocation.AllocationFingerprint },
                scene = new { NodeCount = scene.Nodes.Count, RouteCount = scene.Routes.Count, DiagnosticCount = scene.Diagnostics.Count, scene.PhysicalSceneFingerprint }
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
                }, acceptance.HardFailureCount + rendererFindings.Length, routes.Routes.Count(x => !x.IsComplete)),
            serializationRepeatCount > 0 ? new SerializationRepeatResult(serializationRepeatCount, true, Array.Empty<string>()) : null));
    }

    private static DiagramDiagnostic[] ValidateRendererFidelity(DrawioPage page, ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PhysicalSceneFreeze scene)
    {
        var cells = page.GraphModel.Descendants("mxCell").ToArray();
        var emittedNodes = cells.Where(x => (string?)x.Attribute("vertex") == "1").ToArray();
        var emittedArchitectureNodes = emittedNodes.Where(x => ((string?)x.Attribute("id"))?.StartsWith("v7_node_", StringComparison.Ordinal) == true).ToArray();
        var emittedEdges = cells.Where(x => (string?)x.Attribute("edge") == "1").ToArray();
        var findings = new List<DiagramDiagnostic>();
        if (emittedArchitectureNodes.Length != scene.Nodes.Count) findings.Add(new DiagramDiagnostic("V7RendererNodeCount", $"Renderer emitted {emittedArchitectureNodes.Length} architecture nodes; scene contains {scene.Nodes.Count}."));
        if (emittedEdges.Length != scene.Routes.Count) findings.Add(new DiagramDiagnostic("V7RendererRouteCount", $"Renderer emitted {emittedEdges.Length} edges; scene contains {scene.Routes.Count}."));
        foreach (var node in scene.Nodes)
        {
            var cell = cells.FirstOrDefault(x => (string?)x.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("node", node.PhysicalNodeId));
            var geometry = cell?.Element("mxGeometry");
            if (geometry is null || !Equal(node.Bounds.Left, geometry.Attribute("x")) || !Equal(node.Bounds.Top, geometry.Attribute("y")) || !Equal(node.Bounds.Right - node.Bounds.Left, geometry.Attribute("width")) || !Equal(node.Bounds.Bottom - node.Bounds.Top, geometry.Attribute("height")))
                findings.Add(new DiagramDiagnostic("V7RendererNodeBounds", $"Renderer node geometry does not match frozen bounds for {node.PhysicalNodeId}.", node.PhysicalNodeId));
        }
        foreach (var route in scene.Routes)
        {
            var cell = cells.FirstOrDefault(x => (string?)x.Attribute("physicalLinkId") == route.PhysicalLinkId);
            var points = cell?.Element("mxGeometry")?.Element("Array")?.Elements("mxPoint").ToArray() ?? Array.Empty<XElement>();
            var expected = route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).ToArray();
            if (cell is null || points.Length != expected.Length || points.Zip(expected, (actual, wanted) => Equal(wanted.X, actual.Attribute("x")) && Equal(wanted.Y, actual.Attribute("y"))).Any(x => !x))
                findings.Add(new DiagramDiagnostic("V7RendererRouteGeometry", $"Renderer route geometry does not match frozen route {route.PhysicalLinkId}.", route.PhysicalLinkId));
        }
        return findings.ToArray();
    }

    private static bool Equal(double expected, XAttribute? actual) => actual is not null && double.TryParse(actual.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(expected - value) < 0.0001;

    private static ArchitectureV7PrePlacementConfiguration Configuration(LayoutSettings layout) => new(layout.BaseCellWidth, layout.NodeWidth, layout.LabelCharacterWidth, layout.LinkNodeWidthPadding,
        Math.Max(1, layout.EdgePortSpacing), Math.Max(0, layout.LinkNodeWidthPadding), (layout.ReservedLayerTypePatterns ?? new List<string>()).Select((pattern, index) => new ArchitectureV7ReservedRoleRule(pattern, pattern, index)).ToArray(), layout.SoftCohortMinimumSize);
    private static DrawioPage RejectedPage() => new("Architecture (rejected)", "architecture-rejected", new XElement("mxGraphModel", new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")))), new[] { new DiagramDiagnostic("V7StrictRejected", "V7 acceptance failed; Draw.io renderer was not invoked.") });
}

internal sealed class ArchitectureV7MechanicalDrawioRenderer
{
    public DrawioPage Render(ArchitectureDiagramModel diagram, ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7PhysicalSceneFreeze scene, ArchitectureRenderSettings settings)
    {
        var root = new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in placement.Projects.OrderBy(x => x.ProjectId, StringComparer.Ordinal))
        {
            var transform = project.Transform; var id = Id("project", project.ProjectId);
            if (settings.ShowProjectContainers)
                root.Add(Vertex(id, project.ProjectId, Style(settings.ProjectContainerStyle), "1", transform.InteriorOriginColumn, transform.InteriorOriginRow, transform.Width, transform.Height));
        }
        foreach (var node in scene.Nodes.OrderBy(x => x.PhysicalNodeId, StringComparer.Ordinal))
        {
            var source = projection.PhysicalNodes.FirstOrDefault(x => x.PhysicalNodeId == node.PhysicalNodeId); if (source is null) continue;
            var id = Id("node", node.PhysicalNodeId); nodes[node.PhysicalNodeId] = id;
            var parent = source.ProjectId is not null && placement.Projects.Any(x => x.ProjectId == source.ProjectId) ? Id("project", source.ProjectId) : "1";
            root.Add(Vertex(id, source.Name, source.IsExternal ? Style(settings.ExternalDependencyStyle) : Style(ResolveNodeStyle(source, settings)), parent, node.Bounds.Left, node.Bounds.Top, node.Bounds.Right - node.Bounds.Left, node.Bounds.Bottom - node.Bounds.Top));
        }
        foreach (var link in projection.PhysicalLinks.OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal))
        {
            var route = scene.Routes.FirstOrDefault(x => x.PhysicalLinkId == link.PhysicalLinkId); if (route is null || !nodes.ContainsKey(link.SourcePhysicalNodeId) || !nodes.ContainsKey(link.DestinationPhysicalNodeId)) continue;
            var points = route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).Select(point => new XElement("mxPoint", new XAttribute("x", point.X.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", point.Y.ToString(CultureInfo.InvariantCulture))));
            root.Add(new XElement("mxCell", new XAttribute("id", Id("edge", link.PhysicalLinkId)), new XAttribute("parent", "1"), new XAttribute("edge", "1"), new XAttribute("source", nodes[link.SourcePhysicalNodeId]), new XAttribute("target", nodes[link.DestinationPhysicalNodeId]), new XAttribute("physicalLinkId", link.PhysicalLinkId), new XAttribute("semanticLinkId", link.SemanticLinkId), new XAttribute("style", ConnectorStyle(settings.Connector)), new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"), new XElement("Array", new XAttribute("as", "points"), points))));
        }
        var graph = new XElement("mxGraphModel", new XAttribute("grid", "0"), new XAttribute("page", "0"), new XAttribute("background", settings.Canvas.BackgroundColor), root);
        return new DrawioPage("Architecture", "architecture", graph, Array.Empty<DiagramDiagnostic>());
    }
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
    private static string ConnectorStyle(ConnectorStyle style) => string.Join(";", new[]
    {
        "edgeStyle=none", "orthogonal=0", "curved=0", $"rounded={(style.Rounded ? 1 : 0)}", $"strokeColor={style.StrokeColor}", $"strokeWidth={style.StrokeWidth}", $"opacity={style.Opacity}", $"startArrow={style.StartArrow}", $"endArrow={style.EndArrow}", $"startFill={(style.StartFill ? 1 : 0)}", $"endFill={(style.EndFill ? 1 : 0)}", $"fontColor={style.FontColor}", "labelPosition=none", style.ExtraStyle
    }.Where(value => !string.IsNullOrWhiteSpace(value))) + ";";
    internal static string IdFor(string kind, string value) { using var sha = SHA256.Create(); return "v7_" + kind + "_" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Take(8).Select(x => x.ToString("x2", CultureInfo.InvariantCulture))); }
    private static string Id(string kind, string value) => IdFor(kind, value);
}
