using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum ProjectPhysicalFindingCategory
{
    OwnershipCompilationFinding,
    PhysicalGeometryFinding,
    SerializationFinding
}

internal sealed record ProjectPhysicalFinding(
    ProjectPhysicalFindingCategory Category,
    string LogicalEdgeId,
    string Description);

internal sealed record ProjectPhysicalValidationResult(
    IReadOnlyList<ProjectPhysicalFinding> Findings);
