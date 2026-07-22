namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum RouteProjectRelationship
{
    SameProject,
    CrossProject,
    RootToProject,
    ProjectToRoot,
    RootOwned
}

internal sealed record RouteSemanticOwnership(
    string LogicalRouteId,
    string? SourceProjectId,
    string? TargetProjectId,
    RouteProjectRelationship Relationship,
    string? SameProjectOwnerId);
