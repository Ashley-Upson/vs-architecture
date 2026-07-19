using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class MixedBoundaryAttributor
{
    private static readonly DownwardIntegrationFamily[] Candidates =
    {
        DownwardIntegrationFamily.MultiBandDownward,
        DownwardIntegrationFamily.SameLayer,
        DownwardIntegrationFamily.UpwardOrReturn,
        DownwardIntegrationFamily.UnsupportedConnectionTopology,
        DownwardIntegrationFamily.NonOrthogonalRegeneration
    };

    public static MixedBoundaryAttribution Attribute(
        IReadOnlyList<AdjacentDownwardLinkContext> contexts,
        AdjacentDownwardObservationReport observation,
        IReadOnlyList<CommonAuthorityInteraction> interactions,
        InterLayerReport bands)
    {
        var observed = observation.Routes.ToDictionary(item => item.LogicalRouteId, StringComparer.Ordinal);
        var contextById = contexts.ToDictionary(item => item.Route.Link.Id, StringComparer.Ordinal);
        var adjacentIds = new HashSet<string>(contexts.Where(item => SemanticFamily(item) == DownwardIntegrationFamily.AdjacentDownward)
            .Select(item => item.Route.Link.Id), StringComparer.Ordinal);
        var attributed = contexts.OrderBy(item => item.Route.Link.Id, StringComparer.Ordinal).Select(context =>
        {
            var routeId = context.Route.Link.Id;
            var secondary = SecondaryReasons(context, observed[routeId]);
            var adjacentInteractions = interactions.Where(item =>
                    (item.FirstRouteId == routeId && adjacentIds.Contains(item.SecondRouteId) ||
                     item.SecondRouteId == routeId && adjacentIds.Contains(item.FirstRouteId)) && item.CouplesAuthority)
                .Select(item => item.Kind).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            return new AttributedTrialRoute(
                routeId,
                SemanticFamily(context),
                observed[routeId].Eligible,
                secondary,
                context.BandMemberships.Select(item => item.BandId.ToString()).Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                adjacentInteractions);
        }).ToArray();

        var deficient = bands.Bands.Where(item => item.MissingExtent > 0).OrderBy(item => item.Id.UpperLayer)
            .Select(band => new DeficientBandAttribution(
                band.Id.ToString(), band.CurrentExtent, band.RequiredExtent, band.MissingExtent,
                band.Memberships.Select(item => item.LogicalEdgeIdentity).Distinct(StringComparer.Ordinal)
                    .Where(contextById.ContainsKey)
                    .GroupBy(routeId => SemanticFamily(contextById[routeId]))
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key,
                        group => (IReadOnlyList<string>)group.OrderBy(item => item, StringComparer.Ordinal).ToArray())))
            .ToArray();
        var unlock = Candidates.Select(candidate => Unlock(candidate, attributed, interactions, deficient)).
            OrderByDescending(item => item.FullySupportedComponents)
            .ThenByDescending(item => item.ExistingAdjacentRoutesUnlocked)
            .ThenByDescending(item => item.DeficientBandsUnlocked.Count)
            .ThenByDescending(item => item.RoutesCovered)
            .ThenBy(item => item.CandidateFamily)
            .ToArray();
        return new MixedBoundaryAttribution(attributed, unlock, deficient);
    }

    private static CandidateFamilyUnlock Unlock(
        DownwardIntegrationFamily candidate,
        IReadOnlyList<AttributedTrialRoute> routes,
        IReadOnlyList<CommonAuthorityInteraction> interactions,
        IReadOnlyList<DeficientBandAttribution> deficientBands)
    {
        bool Supported(AttributedTrialRoute route) => route.CurrentlyEligible || CandidateSupports(candidate, route);
        var capabilities = routes.Select(route => new CommonAuthorityRouteCapability(
            route.LogicalRouteId, Supported(route), Supported(route) ? "Supported" : route.PrimaryFamily.ToString())).ToArray();
        var closure = CommonAuthorityComponentClassifier.Classify(capabilities, interactions);
        var eligibleComponents = closure.Components.Where(item => item.Disposition == CommonAuthorityComponentDisposition.Eligible).ToArray();
        var supportedIds = new HashSet<string>(eligibleComponents.SelectMany(item => item.Routes)
            .Select(item => item.LogicalRouteId), StringComparer.Ordinal);
        var unlockedBands = deficientBands.Where(band => band.RoutesByFamily.Values.SelectMany(item => item).All(supportedIds.Contains))
            .Select(item => item.BandId).ToArray();
        return new CandidateFamilyUnlock(
            candidate,
            routes.Count(route => !route.CurrentlyEligible && CandidateSupports(candidate, route)),
            eligibleComponents.Length,
            routes.Count(route => route.PrimaryFamily == DownwardIntegrationFamily.AdjacentDownward && supportedIds.Contains(route.LogicalRouteId)),
            unlockedBands,
            routes.Count(route => !supportedIds.Contains(route.LogicalRouteId)));
    }

    private static bool CandidateSupports(DownwardIntegrationFamily candidate, AttributedTrialRoute route)
    {
        var terminalUnsupported = route.SecondaryReasons.Contains(
            AdjacentDownwardRejectionReason.UnsupportedConnectionTopology.ToString());
        return candidate switch
        {
            DownwardIntegrationFamily.MultiBandDownward =>
                route.PrimaryFamily is DownwardIntegrationFamily.AdjacentDownward or DownwardIntegrationFamily.MultiBandDownward &&
                !terminalUnsupported,
            DownwardIntegrationFamily.SameLayer => route.PrimaryFamily == DownwardIntegrationFamily.SameLayer && !terminalUnsupported,
            DownwardIntegrationFamily.UpwardOrReturn => route.PrimaryFamily == DownwardIntegrationFamily.UpwardOrReturn && !terminalUnsupported,
            DownwardIntegrationFamily.UnsupportedConnectionTopology =>
                route.PrimaryFamily == DownwardIntegrationFamily.AdjacentDownward && terminalUnsupported,
            DownwardIntegrationFamily.NonOrthogonalRegeneration =>
                route.PrimaryFamily == DownwardIntegrationFamily.AdjacentDownward && !terminalUnsupported &&
                route.SecondaryReasons.Contains(AdjacentDownwardRejectionReason.NonOrthogonal.ToString()),
            _ => false
        };
    }

    private static DownwardIntegrationFamily SemanticFamily(AdjacentDownwardLinkContext context)
    {
        if (context.ExposureTreeSpecific) return DownwardIntegrationFamily.ExposureTreeSpecific;
        if (!string.Equals(context.Source.Node.ProjectId, context.Target.Node.ProjectId, StringComparison.Ordinal))
            return DownwardIntegrationFamily.CrossProject;
        if (context.Target.Depth == context.Source.Depth) return DownwardIntegrationFamily.SameLayer;
        if (context.Target.Depth < context.Source.Depth) return DownwardIntegrationFamily.UpwardOrReturn;
        return context.Target.Depth == context.Source.Depth + 1
            ? DownwardIntegrationFamily.AdjacentDownward
            : DownwardIntegrationFamily.MultiBandDownward;
    }

    private static IReadOnlyList<string> SecondaryReasons(
        AdjacentDownwardLinkContext context,
        AdjacentDownwardLinkObservation observation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (observation.RejectionReason is not null) result.Add(observation.RejectionReason.Value.ToString());
        var points = new[] { context.Route.SourcePoint }.Concat(context.Route.Points).Concat(new[] { context.Route.TargetPoint }).ToArray();
        if (points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Any(item => !item.IsOrthogonal))
            result.Add(AdjacentDownwardRejectionReason.NonOrthogonal.ToString());
        if (context.Route.ExitY != 1 || context.Route.EntryY != 0 ||
            context.Route.SourcePoint.Y != context.Source.Rect.Bottom ||
            context.Route.TargetPoint.Y != context.Target.Rect.Y)
            result.Add(AdjacentDownwardRejectionReason.UnsupportedConnectionTopology.ToString());
        if (context.BandMemberships.Select(item => item.BandId).Distinct().Count() > 1)
            result.Add(AdjacentDownwardRejectionReason.MultipleInterLayer.ToString());
        return result.OrderBy(item => item, StringComparer.Ordinal).ToArray();
    }
}
