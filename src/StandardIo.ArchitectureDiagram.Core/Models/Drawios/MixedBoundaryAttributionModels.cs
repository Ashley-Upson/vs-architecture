using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum DownwardIntegrationFamily
{
    AdjacentDownward,
    MultiBandDownward,
    SameLayer,
    UpwardOrReturn,
    UnsupportedConnectionTopology,
    NonOrthogonalRegeneration,
    CrossProject,
    ExposureTreeSpecific
}

internal sealed record AttributedTrialRoute(
    string LogicalRouteId,
    DownwardIntegrationFamily PrimaryFamily,
    bool CurrentlyEligible,
    IReadOnlyList<string> SecondaryReasons,
    IReadOnlyList<string> BandIds,
    IReadOnlyList<string> InteractionsWithAdjacentRoutes);

internal sealed record CandidateFamilyUnlock(
    DownwardIntegrationFamily CandidateFamily,
    int RoutesCovered,
    int FullySupportedComponents,
    int ExistingAdjacentRoutesUnlocked,
    IReadOnlyList<string> DeficientBandsUnlocked,
    int RemainingUnsupportedRoutes);

internal sealed record DeficientBandAttribution(
    string BandId,
    int AvailableExtent,
    int RequiredExtent,
    int MissingExtent,
    IReadOnlyDictionary<DownwardIntegrationFamily, IReadOnlyList<string>> RoutesByFamily);

internal sealed record MixedBoundaryAttribution(
    IReadOnlyList<AttributedTrialRoute> Routes,
    IReadOnlyList<CandidateFamilyUnlock> UnlockAnalysis,
    IReadOnlyList<DeficientBandAttribution> DeficientBands);
