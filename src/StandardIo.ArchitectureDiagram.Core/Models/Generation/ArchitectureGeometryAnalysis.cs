using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.Generation;

public enum ArchitectureAnalysisSeverity { HardInvalid, LikelyDefect, VisualQualityWarning, Informational }

public sealed record ArchitectureGeometryFinding(
    ArchitectureAnalysisSeverity Severity, string Code, string Description,
    string? NodeId = null, string? LogicalRouteId = null, string? OtherId = null,
    IReadOnlyList<ValidationPoint>? Locations = null);

public sealed record ValidationRectangle(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

public sealed record ArchitectureNodeGeometry(
    string Id, string? OwnerProjectId, string Label, bool IsExternal, ValidationRectangle Bounds);

public sealed record ArchitectureProjectGeometry(string Id, string Label, ValidationRectangle Bounds);

public enum ArchitectureLinkDisposition { Rendered, ExplicitlyOmitted, CollapsedByApprovedRule, Unsupported }

public sealed record ArchitectureLinkReconciliation(
    string SemanticLinkId, ArchitectureLinkDisposition Disposition, string Reason,
    IReadOnlyList<string> RenderedLogicalRouteIds);

public sealed record ArchitectureRouteAnalysis(
    string LogicalRouteId, int Length, int DirectManhattanLength, double DetourRatio,
    int BendCount, int PointCount, int MaximumTerminalStubLength);

public sealed record ArchitectureGeometrySummary(
    int SemanticNodeCount, int SemanticLinkCount, int RenderedNodeCount, int RenderedLogicalRouteCount,
    int PhysicalEdgeCellCount, int ProjectCount, int HardFindingCount, int LikelyDefectCount,
    int VisualWarningCount, int NodeOverlapCount, int LinkNodeIntersectionCount, int SharedSegmentCount,
    int InvalidPerpendicularContactCount, int ParallelClearanceDeficitCount, int DiagonalCount,
    int ZeroLengthSegmentCount, int TotalRouteLength, int MaximumRouteLength, double MaximumDetourRatio,
    int TotalBends, int MaximumBendsPerRoute, int RoutePointCount, ValidationRectangle PageBounds,
    string PageSha256, string AnalysisSha256);

public sealed record ArchitectureGeometryAnalysis(
    ArchitectureGeometrySummary Summary,
    IReadOnlyList<ArchitectureNodeGeometry> Nodes,
    IReadOnlyList<ArchitectureProjectGeometry> Projects,
    IReadOnlyList<ArchitectureRouteAnalysis> Routes,
    IReadOnlyList<ArchitectureLinkReconciliation> Links,
    IReadOnlyList<ArchitectureGeometryFinding> Findings);
