using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class RouteSemanticOwnershipCompiler
{
    public static IReadOnlyDictionary<string, RouteSemanticOwnership> Compile(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links) =>
        links.Values.OrderBy(link => link.Link.Order).ThenBy(link => link.Link.Id, StringComparer.Ordinal)
            .ToDictionary(link => link.Link.Id, link => Classify(nodes, link), StringComparer.Ordinal);

    private static RouteSemanticOwnership Classify(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        LinkLayout link)
    {
        var sourceProjectId = nodes.TryGetValue(link.Link.SourceId, out var source)
            ? source.Node.ProjectId : null;
        var targetProjectId = nodes.TryGetValue(link.Link.TargetId, out var target)
            ? target.Node.ProjectId : null;
        var sameProject = sourceProjectId is not null &&
            string.Equals(sourceProjectId, targetProjectId, StringComparison.Ordinal);
        var relationship = sameProject
            ? RouteProjectRelationship.SameProject
            : sourceProjectId is not null && targetProjectId is not null
                ? RouteProjectRelationship.CrossProject
                : sourceProjectId is null && targetProjectId is not null
                    ? RouteProjectRelationship.RootToProject
                    : sourceProjectId is not null
                        ? RouteProjectRelationship.ProjectToRoot
                        : RouteProjectRelationship.RootOwned;
        return new RouteSemanticOwnership(
            link.Link.Id, sourceProjectId, targetProjectId, relationship,
            sameProject ? sourceProjectId : null);
    }
}
