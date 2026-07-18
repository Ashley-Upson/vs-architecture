using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum CommonAuthorityComponentDisposition
{
    Eligible,
    MixedBoundaryUnsafe,
    Unsupported
}

internal sealed record CommonAuthorityRouteCapability(
    string LogicalRouteId,
    bool Eligible,
    string Reason);

internal sealed record CommonAuthorityInteraction(
    string FirstRouteId,
    string SecondRouteId,
    string Kind,
    bool CouplesAuthority);

internal sealed record CommonAuthorityComponent(
    string Id,
    IReadOnlyList<CommonAuthorityRouteCapability> Routes,
    IReadOnlyList<CommonAuthorityInteraction> Interactions,
    CommonAuthorityComponentDisposition Disposition,
    string Reason);

internal sealed record CommonAuthorityClosure(
    IReadOnlyList<CommonAuthorityComponent> Components,
    IReadOnlyList<CommonAuthorityInteraction> AdvisoryCrossovers);

internal sealed record SharedTurnAllocation(
    IReadOnlyDictionary<string, IReadOnlyList<RailTransition>> TransitionsByRouteId,
    IReadOnlyList<string> RejectedRouteIds);
