using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CommonAuthorityComponentClassifier
{
    public static CommonAuthorityClosure Classify(
        IEnumerable<CommonAuthorityRouteCapability> sourceRoutes,
        IEnumerable<CommonAuthorityInteraction> sourceInteractions)
    {
        var routes = sourceRoutes.OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToArray();
        var interactions = sourceInteractions
            .Select(Stable)
            .Distinct()
            .OrderBy(item => item.FirstRouteId, StringComparer.Ordinal)
            .ThenBy(item => item.SecondRouteId, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();
        var coupling = interactions.Where(item => item.CouplesAuthority)
            .Select(item => new ConflictEdge(item.FirstRouteId, item.SecondRouteId, item.Kind));
        var components = ConflictComponentBuilder.Build(routes, item => item.LogicalRouteId, coupling)
            .Select(component => Build(component, interactions))
            .ToArray();
        return new CommonAuthorityClosure(
            components,
            interactions.Where(item => !item.CouplesAuthority).ToArray());
    }

    private static CommonAuthorityComponent Build(
        ConflictComponent<CommonAuthorityRouteCapability> component,
        IReadOnlyList<CommonAuthorityInteraction> interactions)
    {
        var ids = new HashSet<string>(component.Members.Select(item => item.LogicalRouteId), StringComparer.Ordinal);
        var internalInteractions = interactions.Where(item =>
            ids.Contains(item.FirstRouteId) && ids.Contains(item.SecondRouteId)).ToArray();
        var eligible = component.Members.Count(item => item.Eligible);
        var disposition = eligible == component.Members.Count
            ? CommonAuthorityComponentDisposition.Eligible
            : eligible == 0
                ? CommonAuthorityComponentDisposition.Unsupported
                : CommonAuthorityComponentDisposition.MixedBoundaryUnsafe;
        var reason = disposition == CommonAuthorityComponentDisposition.Eligible
            ? "ClosedEligibleComponent"
            : disposition == CommonAuthorityComponentDisposition.Unsupported
                ? string.Join(";", component.Members.Select(item => item.Reason).Distinct().OrderBy(item => item, StringComparer.Ordinal))
                : "SupportedAndUnsupportedRoutesShareClosedInteractionBoundary";
        return new CommonAuthorityComponent(
            component.Id, component.Members, internalInteractions, disposition, reason);
    }

    private static CommonAuthorityInteraction Stable(CommonAuthorityInteraction interaction) =>
        string.CompareOrdinal(interaction.FirstRouteId, interaction.SecondRouteId) <= 0
            ? interaction
            : interaction with
            {
                FirstRouteId = interaction.SecondRouteId,
                SecondRouteId = interaction.FirstRouteId
            };
}
