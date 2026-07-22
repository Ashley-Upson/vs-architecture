using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class DeterministicDrawioExporter : IDeterministicDrawioExporter
{
    public ArchitectureRenderResult GenerateArchitectureProjectRegionResult(
        ArchitectureRenderGraph graph,
        DiagramSettings settings)
    {
        var region = GenerateProjectRegion(graph, settings);
        return ToArchitectureRenderResult(region);
    }

    public ArchitectureRenderResult GenerateArchitectureProjectRegionResult(DiagramModel diagram, DiagramSettings settings)
    {
        var region = GenerateProjectRegion(diagram, settings);
        return ToArchitectureRenderResult(region);
    }

    private static ArchitectureRenderResult ToArchitectureRenderResult(ProjectRegionGenerationResult region)
    {
        var document = System.Xml.Linq.XDocument.Parse(region.Document);
        var graph = new System.Xml.Linq.XElement(document.Root!.Elements("diagram").Single().Element("mxGraphModel")!);
        var page = new DrawioPage("Architecture", "architecture", graph, Array.Empty<DiagramDiagnostic>());
        using var invariant = JsonDocument.Parse(region.InvariantJson);
        var root = invariant.RootElement;
        var logical = ReadRegionFindings(root, "logicalFindings");
        var physical = ReadRegionPhysicalFindings(root);
        var routes = root.GetProperty("logicalRoutes").EnumerateArray().Select(route => new GeneratedRoute(
            route.GetProperty("logicalEdgeId").GetString() ?? string.Empty,
            route.GetProperty("points").EnumerateArray().Select(point => new ValidationPoint(
                point.GetProperty("X").GetInt32(), point.GetProperty("Y").GetInt32())).ToArray())).ToArray();
        return new ArchitectureRenderResult(
            page, Array.Empty<ValidationFinding>(), logical, physical,
            Array.Empty<RouteRepairAttempt>(), routes, region.StageTimings,
            new ArchitectureEligibilityResult(region.Eligible, region.FallbackReasons),
            () => new DrawioDiagnosticExportResult(region.Document, region.InvariantJson,
                new Dictionary<string, string>(), logical.Count(item => item.IsStrictlyEnforced) + physical.Length,
                logical.Select(item => item.LogicalRouteId).Concat(physical.Select(item => item.LogicalRouteId))
                    .Distinct(StringComparer.Ordinal).Count()),
            new ArchitectureDevelopmentArtifacts(region.InvariantJson,
                new Dictionary<string, string> { ["invariants.json"] = region.InvariantJson }));
    }

    public DrawioPage GenerateArchitecturePage(DiagramModel diagram, DiagramSettings settings)
        => GenerateArchitectureResult(diagram, settings).Page;

    public ArchitectureRenderResult GenerateArchitectureResult(DiagramModel diagram, DiagramSettings settings)
    {
        var prepared = Prepare(diagram, settings);
        var graphModel = Measure(prepared.StageTimings, "architecture page serialization", () =>
            new DiagramFileBuilder(prepared.Settings).BuildArchitecturePage(prepared.Layout, prepared.Ownership));
        var pageDiagnostics = prepared.Layout.Traceability.Violations
            .OrderBy(violation => violation.EdgeId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Code)
            .Select(violation => new DiagramDiagnostic(
                violation.Code.ToString(), violation.Description, violation.EdgeId))
            .ToArray();
        var page = new DrawioPage("Architecture", "architecture", graphModel, pageDiagnostics);
        var findings = prepared.Layout.Traceability.Violations
            .Select(violation => ToFinding(violation, prepared.IsEnforced(violation))).ToArray();
        var preRepair = prepared.Layout.PreRepairTraceability.Violations
            .Select(violation => ToFinding(violation, false)).ToArray();
        var routes = prepared.Layout.Links.Values.OrderBy(link => link.Link.Id, StringComparer.Ordinal)
            .Select(link => new GeneratedRoute(link.Link.Id,
                new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint })
                    .Select(point => new ValidationPoint(point.X, point.Y)).ToArray())).ToArray();
        var document = new DrawioDocumentComposer().Compose(new[] { page }, new DrawioDocumentSettings()).Content;
        return new ArchitectureRenderResult(
            page, preRepair, findings, prepared.PhysicalFindings,
            prepared.Layout.RepairAttempts, routes, AllTimings(prepared),
            new ArchitectureEligibilityResult(
                findings.Concat(prepared.PhysicalFindings).All(finding => !finding.IsStrictlyEnforced),
                findings.Concat(prepared.PhysicalFindings).Where(finding => finding.IsStrictlyEnforced)
                    .Select(finding => finding.Category).Distinct().ToArray()),
            () => BuildDiagnostic(prepared, document));
    }

    public ProjectRegionGenerationResult GenerateProjectRegion(
        DiagramModel diagram,
        DiagramSettings settings)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        settings ??= DiagramSettings.CreateDefault();
        var timings = new List<PipelineStageMetric>();
        var reasons = ProjectRegionEligibility.Explain(diagram);
        var graph = Measure(timings, "project-region semantic preparation", () => RenderGraph.From(diagram, settings));
        return GenerateProjectRegion(
            graph, settings, timings, reasons, diagram.Projects.Count,
            diagram.Projects.Sum(project => project.Types.Count) + diagram.ExternalDependencies.Count,
            diagram.Edges.Count);
    }

    public ProjectRegionGenerationResult GenerateProjectRegion(
        ArchitectureRenderGraph projected,
        DiagramSettings settings)
    {
        if (projected is null) throw new ArgumentNullException(nameof(projected));
        settings ??= DiagramSettings.CreateDefault();
        var timings = new List<PipelineStageMetric>();
        var graph = Measure(timings, "projected render graph adaptation", () => RenderGraph.From(projected));
        return GenerateProjectRegion(
            graph, settings, timings, Array.Empty<string>(), projected.Projects.Count,
            projected.Nodes.Select(node => node.SemanticNodeId).Distinct(StringComparer.Ordinal).Count(),
            projected.Links.Select(link => link.SemanticLinkId).Distinct(StringComparer.Ordinal).Count());
    }

    private ProjectRegionGenerationResult GenerateProjectRegion(
        RenderGraph graph,
        DiagramSettings settings,
        List<PipelineStageMetric> timings,
        IReadOnlyList<string> initialReasons,
        int semanticProjectCount,
        int semanticNodeCount,
        int semanticLinkCount)
    {
        var reasons = initialReasons;
        var layout = Measure(timings, "project-region generation", () => ProjectRegionLayoutBuilder.Build(graph, settings));
        var ownership = Measure(timings, "project-region ownership compilation", () =>
            CoordinateOwnershipCompiler.Compile(layout.Nodes, layout.Projects, layout.Links, settings.ShowProjectContainers));
        if (settings.ShowProjectContainers)
        {
            var projects = Measure(timings, "project-region bounds", () => ProjectOwnershipBoundsCompiler.Compile(
                layout.Projects, layout.Nodes, ownership, settings.Layout.ContainerPadding,
                settings.Layout.ProjectHeaderHeight));
            layout = layout.WithProjects(projects);
            ownership = Measure(timings, "project-region ownership rebase", () =>
                CoordinateOwnershipCompiler.Rebase(ownership, projects));
        }
        var projectLabels = Measure(timings, "project-region project-label bounds", () =>
            ProjectLabelGeometryMeasurer.Measure(
                layout.Projects, settings.Layout.ProjectHeaderHeight, settings.Layout.LinkPadding));
        var physicalValidation = Measure(timings, "project-region physical validation", () =>
            ProjectPhysicalGeometryValidator.Validate(
                layout.Nodes, layout.Links, ownership, projectLabels,
                settings.Layout.ParallelLaneSpacing));
        var document = Measure(timings, "project-region serialization", () =>
            new DiagramFileBuilder(settings).Build(layout, ownership));
        var hard = layout.Traceability.Violations.Where(violation =>
            violation.Code != TraceabilityViolationCode.PerpendicularCrossing).ToArray();
        if (hard.Length > 0) reasons = reasons.Concat(new[] { $"HardValidationFindings:{hard.Length}" }).ToArray();
        if (physicalValidation.Findings.Count > 0)
            reasons = reasons.Concat(new[] { $"PhysicalValidationFindings:{physicalValidation.Findings.Count}" }).ToArray();
        var allTimings = timings.Concat(layout.StageTimings).ToArray();
        var invariantJson = JsonSerializer.Serialize(new
        {
            mode = "IndependentProjectRegion",
            semanticProjectCount,
            semanticNodeCount,
            semanticLinkCount,
            renderNodeCount = layout.Nodes.Count,
            renderLinkCount = layout.Links.Count,
            eligible = reasons.Count == 0,
            fallbackReasons = reasons,
            legacyRenderLayoutUsed = false,
            legacyCoordinatesUsed = false,
            legacyPathsUsed = false,
            horizontalSegmentYAuthority = "DeterministicSlotAllocator",
            verticalColumnXAuthority = "VerticalLinkColumnAllocator / ReturnColumnAllocator",
            topologySelectionAuthority = "CanonicalTopologyFamilySelector",
            topologyFamilies = layout.CanonicalTopologyPlans.Values
                .GroupBy(plan => plan.Family).OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString(), group => group.Count()),
            interLayersDiscovered = layout.ProjectSlotCompilation?.InterLayerCount ?? 0,
            slotDemands = layout.ProjectSlotCompilation?.Demands.Count ?? 0,
            slotsAssigned = layout.ProjectSlotCompilation?.Assignments.Count ?? 0,
            slotAllocations = layout.ProjectSlotCompilation?.Demands
                .OrderBy(demand => demand.Id, StringComparer.Ordinal).Select(demand => new
                {
                    demand.Id,
                    demand.LogicalRouteId,
                    role = demand.Role.ToString(),
                    occupiedMinimum = demand.OccupiedInterval.Minimum,
                    occupiedMaximum = demand.OccupiedInterval.Maximum,
                    allowedMinimum = demand.AllowedAxisRange.Minimum,
                    allowedMaximum = demand.AllowedAxisRange.Maximum,
                    minimumEndpointRole = demand.MinimumEndpointRole.ToString(),
                    maximumEndpointRole = demand.MaximumEndpointRole.ToString(),
                    assignedCoordinate = layout.ProjectSlotCompilation.Assignments[demand.Id].AxisCoordinate,
                    slotIndex = layout.ProjectSlotCompilation.Assignments[demand.Id].SlotIndex,
                    movementScope = demand.MovementScope?.Id,
                    bandId = demand.BandId,
                    coordinateFrameId = demand.CoordinateFrameId,
                    demandCategory = demand.DemandCategory,
                    allocationEnvelope = $"project-interLayer:{demand.MovementScope?.Id}:{demand.AllowedAxisRange.Minimum}:{demand.AllowedAxisRange.Maximum}"
                }),
            interLayersExpanded = layout.ProjectSlotCompilation?.ExpandedInterLayerCount ?? 0,
            corridorLaneYAssignmentsRemaining = 0,
            repairBasedHorizontalOffsetsRemaining = 0,
            destinationColumnsAssigned = layout.ProjectSlotCompilation?.VerticalColumns.ColumnsByDemandId.Count(item =>
                !layout.ProjectSlotCompilation.ReturnSideByRouteId.ContainsKey(item.Value.LinkId)) ?? 0,
            columnAllocations = layout.ProjectSlotCompilation?.VerticalColumns.ColumnsByDemandId.Values
                .OrderBy(column => column.DemandId, StringComparer.Ordinal),
            returnColumnsAssigned = layout.ProjectSlotCompilation?.ReturnSideByRouteId.Count ?? 0,
            returnSideSelections = layout.ProjectSlotCompilation?.ReturnSideByRouteId,
            corridorLaneXAssignmentsRemaining = 0,
            repairBasedVerticalOffsetsRemaining = 0,
            legacyCandidateSelectionInvoked = false,
            traversalTopologyReplacementRemaining = 0,
            repairTopologyMutationRemaining = 0,
            projectLabelGeometry = projectLabels.Values,
            logicalRoutes = layout.Links.Values.OrderBy(link => link.Link.Id, StringComparer.Ordinal).Select(link => new
            {
                logicalEdgeId = link.Link.Id,
                family = layout.CanonicalTopologyPlans.TryGetValue(link.Link.Id, out var plan)
                    ? plan.Family.ToString() : null,
                points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint })
            }),
            obstacleCompilationAuthority = "ProjectInterLayerSlotCompiler",
            boundedTopologyRecompileAuthority = (string?)null,
            interLayerSlotAllocationUsed = true,
            developmentTrialUsed = false,
            fallbackOccursOutsideRenderer = true,
            findings = layout.Traceability.Violations.Select(violation => new
            {
                code = violation.Code.ToString(),
                edgeId = violation.EdgeId,
                violation.OtherEdgeId,
                violation.OtherNodeId,
                violation.Description
            }),
            logicalFindings = layout.Traceability.Violations.Select(violation => new
            {
                category = violation.Code == TraceabilityViolationCode.PerpendicularCrossing
                    ? "LogicalGeometryFinding" : "LogicalTopologyFinding",
                code = violation.Code.ToString(),
                edgeId = violation.EdgeId,
                violation.OtherEdgeId,
                violation.OtherNodeId,
                violation.Description
            }),
            physicalFindings = physicalValidation.Findings,
            timings = allTimings
        }, new JsonSerializerOptions { WriteIndented = true });
        return new ProjectRegionGenerationResult(
            reasons.Count == 0, reasons, document, invariantJson, allTimings,
            layout.Nodes.Count, layout.Links.Count);
    }

    public DrawioGenerationResult GenerateResult(
        DiagramModel diagram,
        DiagramSettings settings,
        IReadOnlyList<PipelineStageMetric>? upstreamTimings = null)
    {
        var typed = GenerateArchitectureResult(diagram, settings);
        var document = new DrawioDocumentComposer().Compose(
            new[] { typed.Page }, new DrawioDocumentSettings()).Content;
        var timings = upstreamTimings is null
            ? typed.Timings
            : upstreamTimings.Concat(typed.Timings).ToArray();
        return new DrawioGenerationResult(
            document,
            typed.PreRepairFindings,
            typed.LogicalFindings,
            typed.RepairAttempts,
            typed.Routes,
            serializationSucceeded: true,
            strictValidationPassed: typed.LogicalFindings.Concat(typed.PhysicalFindings)
                .All(finding => !finding.IsStrictlyEnforced),
            () => typed.Diagnostics,
            stageTimings: timings);
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
        PerformanceAudit.Increment("diagnostic materializations");
        var enforced = prepared.Layout.Traceability.Violations.Where(prepared.IsEnforced).ToArray();
        PerformanceAudit.Increment("diagnostic projections", prepared.Layout.Traceability.Violations.Count);
        var placement = new PlacedGraph(
            prepared.Layout.Graph,
            prepared.Layout.Nodes,
            prepared.Layout.Projects,
            prepared.Layout.LayoutRevision);
        var routeRevision = new RouteRevision(prepared.Layout.Links.Values
            .Select(link => link.RouteState.Revision)
            .DefaultIfEmpty(0)
            .Max());
        var generated = new GeneratedLogicalRoutes(placement, prepared.Layout.Links, routeRevision);
        InterLayerReport bands;
        using (PerformanceAudit.Measure(
            "Inter-layer demand discovery",
            placement.Nodes.Count,
            generated.Links.Count,
            generated.Links.Values.Sum(link => link.Points.Count + 1),
            layoutRevision: placement.Revision.Value,
            routeRevision: routeRevision.Value))
        {
            bands = InterLayerDemandDiscovery.Observe(
                placement, generated, prepared.Settings, prepared.Layout.Traceability);
        }
        var reportJson = DrawioDiagnosticReportBuilder.BuildJson(
            prepared.Layout,
            prepared.Ownership,
            enforced,
            prepared.Settings.Layout,
            AllTimings(prepared),
            diagnosticReuse: true,
            bands);
        var annotated = DrawioDiagnosticReportBuilder.Annotate(content, enforced, "All enforced findings", prepared.Layout);
        var focused = DrawioDiagnosticReportBuilder.FocusedOutputs(content, enforced, prepared.Layout);
        PerformanceAudit.Increment("focused diagnostic outputs", focused.Count);
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
            IReadOnlyDictionary<string, ProjectLayout> projects;
            using (PerformanceAudit.Measure(
                "project-bound calculation",
                layout.Nodes.Count,
                layout.Links.Count,
                ownership.Segments.Count,
                layout.Graph.Projects.Count,
                layout.LayoutRevision.Value))
            {
                projects = ProjectOwnershipBoundsCompiler.Compile(
                    layout.Projects,
                    layout.Nodes,
                    ownership,
                    settings.Layout.ContainerPadding,
                    settings.Layout.ProjectHeaderHeight);
            }
            layout = layout.WithProjects(projects);
            using (PerformanceAudit.Measure(
                "ownership rebase",
                layout.Nodes.Count,
                layout.Links.Count,
                ownership.Segments.Count,
                ownership.Segments.Count,
                layout.LayoutRevision.Value))
            {
                ownership = CoordinateOwnershipCompiler.Rebase(ownership, projects);
            }
        }
        var projectLabels = Measure(timings, "project-label bounds", () => ProjectLabelGeometryMeasurer.Measure(
            layout.Projects, settings.Layout.ProjectHeaderHeight, settings.Layout.LinkPadding));
        var physicalFindings = Measure(timings, "node-overlap validation", () =>
            NodeOverlapValidator.Validate(layout.Nodes, projectLabels));

        return new PreparedExport(settings, layout, ownership, physicalFindings, IsEnforced, timings);
    }

    private static T Measure<T>(ICollection<PipelineStageMetric> timings, string stage, Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        T result;
        var performanceStage = stage == "render graph construction"
            ? "RenderGraph construction"
            : stage == "ownership"
                ? "ownership compilation"
                : stage;
        using (PerformanceAudit.Measure(performanceStage))
        {
            result = action();
        }
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
        IReadOnlyList<ValidationFinding> PhysicalFindings,
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

    private static ValidationFinding[] ReadRegionFindings(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var findings)
            ? findings.EnumerateArray().Select(finding => new ValidationFinding(
                finding.TryGetProperty("category", out var category) ? category.GetString() ?? "LogicalGeometryFinding" : "LogicalGeometryFinding",
                finding.TryGetProperty("edgeId", out var edge) ? edge.GetString() ?? string.Empty : string.Empty,
                finding.TryGetProperty("OtherEdgeId", out var otherEdge) ? otherEdge.GetString() : null,
                finding.TryGetProperty("OtherNodeId", out var otherNode) ? otherNode.GetString() : null,
                0,
                finding.TryGetProperty("Description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                Array.Empty<ValidationPoint>(), Array.Empty<ValidationSegment>(), null, null, null,
                finding.TryGetProperty("code", out var code) && code.GetString() != TraceabilityViolationCode.PerpendicularCrossing.ToString())).ToArray()
            : Array.Empty<ValidationFinding>();

    private static ValidationFinding[] ReadRegionPhysicalFindings(JsonElement root) =>
        root.TryGetProperty("physicalFindings", out var findings)
            ? findings.EnumerateArray().Select(finding => new ValidationFinding(
                "PhysicalGeometryFinding",
                finding.TryGetProperty("LogicalEdgeId", out var edge) ? edge.GetString() ?? string.Empty : string.Empty,
                null, null, 0,
                finding.TryGetProperty("Description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                Array.Empty<ValidationPoint>(), Array.Empty<ValidationSegment>(), null, null, null, true)).ToArray()
            : Array.Empty<ValidationFinding>();
}
