using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DrawioDiagnosticReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string BuildJson(
        RenderLayout layout,
        CoordinateOwnershipCompilation ownership,
        IReadOnlyList<TraceabilityViolation> enforced,
        int requiredSpacing)
    {
        var projectNames = layout.Graph.Projects.ToDictionary(project => project.Id, project => project.Name, StringComparer.Ordinal);
        var routes = enforced
            .GroupBy(violation => violation.EdgeId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var link = layout.Links[group.Key];
                var source = layout.Nodes[link.Link.SourceId];
                var target = layout.Nodes[link.Link.TargetId];
                var selected = SelectedCandidate(layout, group.Key);
                var evaluations = CandidateEvaluations(layout, group.Key);
                var decision = CandidateDecision(layout, group.Key);
                var physical = ownership.Segments
                    .Where(segment => segment.LogicalEdgeId == group.Key)
                    .OrderBy(segment => segment.SegmentIndex)
                    .Select(segment => segment.Id)
                    .ToArray();
                var failedCorridors = layout.Corridors.SegmentMappings
                    .Where(mapping => mapping.EdgeId == group.Key && layout.Lanes.FailedCorridorIds.Contains(mapping.CorridorId))
                    .Select(mapping => mapping.CorridorId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    logicalRouteId = group.Key,
                    sourceNode = new
                    {
                        id = source.Node.Id,
                        name = source.Node.Name,
                        fullName = source.Node.FullName,
                        projectId = source.Node.ProjectId,
                        project = ProjectName(projectNames, source.Node.ProjectId),
                        visualLayer = source.Depth
                    },
                    targetNode = new
                    {
                        id = target.Node.Id,
                        name = target.Node.Name,
                        fullName = target.Node.FullName,
                        projectId = target.Node.ProjectId,
                        project = ProjectName(projectNames, target.Node.ProjectId),
                        visualLayer = target.Depth
                    },
                    selectedCandidateId = selected?.Signature.Value ?? decision?.FinalSignature,
                    selectedPoints = CompletePoints(link).Select(ToPoint).ToArray(),
                    physicalDrawioSegmentIds = physical,
                    selectionScore = evaluations.FirstOrDefault(item => item.IsSelected)?.Score.ToString(),
                    selectionReason = decision?.Reason,
                    fallbackStatus = new
                    {
                        used = failedCorridors.Length > 0 || selected is null,
                        failedCorridorIds = failedCorridors,
                        traversalDiagnostics = layout.Traversals.Traversals.TryGetValue(group.Key, out var traversal)
                            ? traversal.Diagnostics.Select(item => (object)new
                            {
                                item.Code,
                                item.Message,
                                item.SegmentIndex,
                                item.JunctionId
                            }).ToArray()
                            : Array.Empty<object>()
                    },
                    candidateAlternativesAvailable = evaluations.Count(item => !item.IsSelected),
                    candidates = evaluations.Select(item => new
                    {
                        id = item.Signature,
                        selected = item.IsSelected,
                        score = item.Score.ToString(),
                        scoreComponents = Score(item.Score),
                        reason = item.Reason
                    }).ToArray(),
                    violations = group.Select((violation, index) => Finding(violation, index + 1, requiredSpacing)).ToArray()
                };
            })
            .ToArray();

        var findings = enforced
            .OrderBy(violation => violation.EdgeId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Code)
            .ThenBy(violation => violation.OtherEdgeId, StringComparer.Ordinal)
            .Select((violation, index) => Finding(violation, index + 1, requiredSpacing))
            .ToArray();
        var categories = enforced
            .GroupBy(Category)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                category = group.Key,
                uniqueLogicalRoutes = group.Select(item => item.EdgeId).Distinct(StringComparer.Ordinal).Count(),
                rawValidatorFindings = group.Count(),
                distinctPhysicalLocations = group
                    .SelectMany(item => item.Locations ?? Array.Empty<Point>())
                    .Distinct()
                    .Count()
            })
            .ToArray();
        var routeGeometry = layout.Links.Values
            .OrderBy(link => link.Link.Id, StringComparer.Ordinal)
            .Select(link => new
            {
                logicalRouteId = link.Link.Id,
                sourceId = link.Link.SourceId,
                targetId = link.Link.TargetId,
                selectedCandidateId = SelectedCandidate(layout, link.Link.Id)?.Signature.Value,
                selectedCandidatePoints = SelectedCandidate(layout, link.Link.Id)?.Points.Select(ToPoint).ToArray(),
                points = CompletePoints(link).Select(ToPoint).ToArray()
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            diagnostic = true,
            generatedAtUtc = DateTime.UtcNow,
            summary = new
            {
                uniqueRejectedRoutes = routes.Length,
                enforcedFindings = enforced.Count,
                allValidatorFindings = layout.Traceability.Violations.Count
            },
            categories,
            findings,
            routes,
            routeGeometry
        }, JsonOptions);
    }

    public static IReadOnlyDictionary<string, string> FocusedOutputs(
        string content,
        IReadOnlyList<TraceabilityViolation> violations,
        RenderLayout? layout = null) =>
        violations.GroupBy(FocusGroup)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Annotate(content, group.ToArray(), group.Key, layout),
                StringComparer.Ordinal);

    public static string Annotate(
        string content,
        IReadOnlyList<TraceabilityViolation> violations,
        string title,
        RenderLayout? layout = null)
    {
        var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        var file = document.Root ?? throw new InvalidOperationException("Draw.io document has no root.");
        file.SetAttributeValue("diagnostic", "true");
        file.SetAttributeValue("validationFindings", violations.Count);
        var architecture = file.Elements("diagram").First(element => (string?)element.Attribute("name") == "Architecture");
        var architectureRoot = architecture.Descendants("root").First();
        architectureRoot.Add(Banner(title, violations.Count));

        var markerCells = MarkerCells(violations, layout).ToArray();
        foreach (var marker in markerCells)
        {
            architectureRoot.Add(new XElement(marker));
        }

        var diagnosticRoot = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")),
            Banner(title, violations.Count));
        foreach (var marker in markerCells)
        {
            diagnosticRoot.Add(marker);
        }

        file.Add(new XElement("diagram",
            new XAttribute("name", $"Diagnostics - {title}"),
            new XElement("mxGraphModel",
                new XAttribute("grid", "0"),
                new XElement("root", diagnosticRoot.Elements()))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static object Finding(TraceabilityViolation violation, int number, int requiredSpacing) => new
    {
        number,
        category = Category(violation),
        validatorCode = violation.Code.ToString(),
        logicalRouteId = violation.EdgeId,
        otherRouteId = violation.OtherEdgeId,
        otherNodeId = violation.OtherNodeId,
        violation.Magnitude,
        violation.Description,
        locations = (violation.Locations ?? Array.Empty<Point>()).Select(ToPoint).ToArray(),
        offendingSegments = (violation.OffendingSegments ?? Array.Empty<Segment>()).Select(ToSegment).ToArray(),
        requiredSpacing = violation.RequiredSpacing ?? (violation.Code == TraceabilityViolationCode.ParallelSpacing ? requiredSpacing : (int?)null),
        actualSpacing = violation.ActualSpacing,
        spacingDeficit = violation.Code == TraceabilityViolationCode.ParallelSpacing ? violation.Magnitude : (int?)null,
        parallelOverlapLength = violation.ParallelOverlapLength
    };

    private static string Category(TraceabilityViolation violation) => violation.Code switch
    {
        TraceabilityViolationCode.NodeCollision => "NodeInteriorIntersection",
        TraceabilityViolationCode.SharedSegment => "SharedNonZeroLengthSegment",
        TraceabilityViolationCode.ParallelSpacing => "SpacingDeficit",
        TraceabilityViolationCode.ReusedBend => "ReusedBend",
        TraceabilityViolationCode.ImmediateReversal => "ImmediateReversal",
        _ => "Other"
    };

    private static string FocusGroup(TraceabilityViolation violation) => violation.Code switch
    {
        TraceabilityViolationCode.NodeCollision => "node-intersections",
        TraceabilityViolationCode.SharedSegment or TraceabilityViolationCode.ReusedBend => "shared-or-ambiguous-geometry",
        TraceabilityViolationCode.ParallelSpacing => "spacing-problems",
        TraceabilityViolationCode.ImmediateReversal => "malformed-traversal-or-reversals",
        _ => "other"
    };

    private static IEnumerable<XElement> MarkerCells(
        IReadOnlyList<TraceabilityViolation> violations,
        RenderLayout? layout)
    {
        var number = 0;
        foreach (var violation in violations
            .OrderBy(item => item.EdgeId, StringComparer.Ordinal)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.OtherEdgeId, StringComparer.Ordinal))
        {
            number++;
            var location = (violation.Locations ?? Array.Empty<Point>()).FirstOrDefault();
            var x = location == default ? 20 + number * 6 : location.X;
            var y = location == default ? 60 + number * 6 : location.Y;
            var colour = violation.Code switch
            {
                TraceabilityViolationCode.NodeCollision => "#ff0000",
                TraceabilityViolationCode.SharedSegment => "#ff6600",
                TraceabilityViolationCode.ParallelSpacing => "#ffd966",
                TraceabilityViolationCode.ReusedBend => "#a64dff",
                _ => "#cc0000"
            };
            var routeLabel = layout is not null && layout.Links.TryGetValue(violation.EdgeId, out var link)
                ? $"{layout.Nodes[link.Link.SourceId].Node.Name} → {layout.Nodes[link.Link.TargetId].Node.Name}"
                : violation.EdgeId;
            var value = $"{number}: {routeLabel} | {Category(violation)} | {violation.EdgeId}" +
                (violation.OtherEdgeId is null ? string.Empty : $" / {violation.OtherEdgeId}") +
                (violation.OtherNodeId is null ? string.Empty : $" / node {violation.OtherNodeId}") +
                $" | ({x},{y})";
            yield return new XElement("mxCell",
                new XAttribute("id", $"diagnostic_marker_{number:D4}"),
                new XAttribute("value", value),
                new XAttribute("style", $"shape=ellipse;whiteSpace=wrap;html=1;fillColor={colour};strokeColor=#ffffff;fontColor=#ffffff;fontSize=10;"),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", "1"),
                new XElement("mxGeometry",
                    new XAttribute("x", x - 9),
                    new XAttribute("y", y - 9),
                    new XAttribute("width", 18),
                    new XAttribute("height", 18),
                    new XAttribute("as", "geometry")));
        }
    }

    private static XElement Banner(string title, int count) =>
        new("mxCell",
            new XAttribute("id", $"diagnostic_banner_{SafeId(title)}"),
            new XAttribute("value", $"DIAGNOSTIC OUTPUT - generation failed strict validation - {title} ({count})"),
            new XAttribute("style", "rounded=0;whiteSpace=wrap;html=1;fillColor=#990000;strokeColor=#ffffff;fontColor=#ffffff;fontStyle=1;"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", "1"),
            new XElement("mxGeometry",
                new XAttribute("x", 20),
                new XAttribute("y", 20),
                new XAttribute("width", 620),
                new XAttribute("height", 32),
                new XAttribute("as", "geometry")));

    private static CorridorPathCandidate? SelectedCandidate(RenderLayout layout, string edgeId)
    {
        if (layout.RegionalPathSelection?.Selected.TryGetValue(edgeId, out var regional) == true)
        {
            return regional;
        }

        return layout.PathSelection?.Selected.TryGetValue(edgeId, out var global) == true ? global : null;
    }

    private static IReadOnlyList<CorridorPathEvaluation> CandidateEvaluations(RenderLayout layout, string edgeId) =>
        layout.PathSelection?.Evaluations.Where(item => item.EdgeId == edgeId).ToArray()
        ?? Array.Empty<CorridorPathEvaluation>();

    private static CorridorPathDecision? CandidateDecision(RenderLayout layout, string edgeId) =>
        layout.PathSelection?.Decisions.FirstOrDefault(item => item.EdgeId == edgeId);

    private static IEnumerable<Point> CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint });

    private static string? ProjectName(IReadOnlyDictionary<string, string> projects, string? projectId) =>
        projectId is not null && projects.TryGetValue(projectId, out var name) ? name : null;

    private static object ToPoint(Point point) => new { point.X, point.Y };

    private static object ToSegment(Segment segment) => new { start = ToPoint(segment.Start), end = ToPoint(segment.End), segment.Length };

    private static object Score(GlobalRouteScore score) => new
    {
        nodeCollisionOrInvalidGeometryCount = score.InvalidGeometry,
        sharedSegmentLength = score.SharedSegmentLength,
        spacingDeficit = score.SpacingDeficit,
        terminalFanoutViolations = score.TerminalFanoutViolations,
        ambiguousTransitions = score.AmbiguousTransitions,
        capacityFailure = score.CapacityFailure,
        perpendicularCrossingsAndCongestion = score.CrossingsAndCongestion,
        routeEnvelopeExpansion = RouteEnvelopeExpansion(score),
        pathEconomy = score.PathEconomy
    };

    private static int RouteEnvelopeExpansion(GlobalRouteScore score)
    {
        var property = score.GetType().GetProperty("RouteEnvelopeExpansion") ??
            score.GetType().GetProperty("CanvasEscape");
        return property?.GetValue(score) is int value ? value : 0;
    }

    private static string SafeId(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
