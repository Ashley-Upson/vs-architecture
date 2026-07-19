using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectPhysicalGeometryValidator
{
    public static ProjectPhysicalValidationResult Validate(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> logicalLinks,
        CoordinateOwnershipCompilation ownership,
        IReadOnlyDictionary<string, ProjectLabelGeometry> projectLabels,
        int requiredSpacing)
    {
        var findings = new List<ProjectPhysicalFinding>();
        var reconstructed = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);
        foreach (var link in logicalLinks.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal))
        {
            var expected = Complete(link);
            var actual = CoordinateOwnershipCompiler.ReconstructAbsolutePoints(ownership, link.Link.Id);
            if (!LogicalRouteNormalizer.NormalizePoints(expected)
                .SequenceEqual(LogicalRouteNormalizer.NormalizePoints(actual)))
                findings.Add(new ProjectPhysicalFinding(
                    ProjectPhysicalFindingCategory.OwnershipCompilationFinding, link.Link.Id,
                    "Physical ownership segments do not reconstruct the logical route exactly."));
            if (actual.Count < 2 || actual.Zip(actual.Skip(1), (a, b) => new Segment(a, b)).Any(segment => !segment.IsOrthogonal))
                findings.Add(new ProjectPhysicalFinding(
                    ProjectPhysicalFindingCategory.PhysicalGeometryFinding, link.Link.Id,
                    "Physical reconstruction contains missing or non-orthogonal geometry."));
            foreach (var label in projectLabels.Values)
                if (actual.Zip(actual.Skip(1), (a, b) => new Segment(a, b))
                    .Any(segment => segment.Intersects(label.ProjectLabelObstacleBounds)))
                    findings.Add(new ProjectPhysicalFinding(
                        ProjectPhysicalFindingCategory.PhysicalGeometryFinding, link.Link.Id,
                        $"Physical route intersects project label text obstacle {label.ProjectId}."));
            if (actual.Count >= 2)
                reconstructed[link.Link.Id] = link.AcceptGeometry(
                    actual, LogicalRouteStage.Validated, nameof(ProjectPhysicalGeometryValidator));
        }
        foreach (var violation in TraceabilityValidator.Validate(nodes, reconstructed, requiredSpacing).Violations)
            findings.Add(new ProjectPhysicalFinding(
                ProjectPhysicalFindingCategory.PhysicalGeometryFinding, violation.EdgeId,
                $"{violation.Code}:{violation.Description}"));
        var physicalIds = new HashSet<string>(
            ownership.Segments.Select(item => item.LogicalEdgeId), StringComparer.Ordinal);
        foreach (var missing in logicalLinks.Keys.Where(id => !physicalIds.Contains(id)).OrderBy(id => id, StringComparer.Ordinal))
            findings.Add(new ProjectPhysicalFinding(
                ProjectPhysicalFindingCategory.SerializationFinding, missing,
                "No physical edge segment was emitted for the logical dependency."));
        return new ProjectPhysicalValidationResult(findings);
    }

    private static IReadOnlyList<Point> Complete(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
}
