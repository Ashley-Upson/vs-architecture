using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record DevelopmentFixtureDefects(
    int NodeCollision,
    int SharedSegment,
    int ParallelSpacing,
    int ReusedBend,
    int ImmediateReversal,
    int NonOrthogonalSegment,
    int BendInvolvedPerpendicularContact,
    int EndpointToInteriorContact);

internal sealed record CommonAuthorityDevelopmentFixtureResult(
    string BeforeDrawio,
    string AfterDrawio,
    DevelopmentFixtureDefects BeforeDefects,
    DevelopmentFixtureDefects AfterDefects,
    int NodesMoved,
    int LayersMoved,
    int SpaceAdded,
    int RoutesRegenerated,
    int RailsAssigned,
    int TurnsAssigned,
    IReadOnlyList<LinkInvalidation> Invalidations,
    long ConvergenceMicroseconds,
    bool RouteRepairCoordinatorRan,
    bool SeparateOverlappingCornersRan,
    bool TraversalFallbackRan);
