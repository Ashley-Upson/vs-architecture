# Routing terminology and deletion inventory

This inventory defines the vocabulary boundary between the consolidated link-authority model and the retained normal-production router. It is intentionally behavioural-neutral: normal production remains owned by `LegacyRoutingPipeline` until the development authority passes the later review gate.

> Current audit, 19 July 2026: every `Rename now` entry below has been completed in active code. The old names remain in this table only as migration history. `MultiBandDownward` was also replaced by `MultiLayerDownward`; active diagnostics now say link segment rather than rail. Remaining `route` wording is limited to path selection, public/serialized compatibility, and the explicitly retained legacy boundary.

## Canonical vocabulary

| Canonical term | Meaning |
|---|---|
| `Layer` | Horizontal group of nodes at one dependency-tree depth. |
| `InterLayer` | Routing space between adjacent layers. |
| `Node` | Rendered semantic box. |
| `Link` | Complete semantic dependency. |
| `LinkPath` | Chosen geometry of a link. |
| `LinkSegment` | One straight part of a link path. |
| `LinkTransition` | Bend joining two link segments. |
| `LinkConnection` | Point at which a link connects to a node. |
| `InterLayerSlot` | Reusable Y allocation for a horizontal segment in an inter-layer. |
| `VerticalLinkColumn` | X allocation and layer range for an uninterrupted vertical segment. |
| `Bounds` | Two-dimensional rectangle. |
| `Interval` | One-dimensional span. |
| `Envelope` | Bounds occupied by a positional subtree, overall or per layer. |
| `MovementScope` | Exact coherent set moved by a persistent spacing constraint. |
| `ConflictComponent` | Connected set of links competing for geometry or movement. |
| `SlotIndex` | Reusable index of an allocated horizontal inter-layer segment. |
| `AxisCoordinate` | Final X or Y coordinate of an allocated segment or column. |

`Route` is reserved for finding or comparing link paths, for example `LinkPathCandidate`, `LinkPathSelector`, `LinkRouter`, and `RoutingDiagnostics`.

## Active type and ownership inventory

`Rename now` means the type already models a consolidated concept. `Retain temporarily` identifies an explicit compatibility or normal-production boundary. Historical evidence documents are not renamed.

| Current name | Actual meaning | Canonical replacement | Current consumers | Authority | Duplicate owner | Action | Deletion gate |
|---|---|---|---|---|---|---|---|
| `RailOrientation` | Axis orientation of a demanded straight link part | `LinkSegmentOrientation` | common demand producers, allocator, tests | development | legacy corridor orientation | Rename now | none |
| `RailSemanticRole` | Semantic role of a straight link part | `LinkSegmentRole` | common producers, validation, diagnostics | development | legacy corridor roles | Rename now | none |
| `RailDemand` | Requested straight link segment allocation | `LinkSegmentDemand` | common producers and slot allocator | development | legacy corridor usage | Rename now | none |
| `AssignedRail` | Allocated straight link segment | `AssignedLinkSegment` | common reconstruction and diagnostics | development | `AllocatedCorridorLane` | Rename now | none |
| `RailTransition` | Ordered bend between assigned segments | `LinkTransition` | downward common authority | development | legacy junction transitions | Rename now | none |
| `RailAllocationRegionIdentity` | Allocation-domain identity | `LinkSegmentAllocationRegionIdentity` | allocator and projections | development | legacy corridor identity | Rename now | none |
| `RailAssignmentOptions` | Deterministic segment allocation options | `LinkSegmentAssignmentOptions` | allocator callers | development | legacy lane options | Rename now | none |
| `RailAssignmentComponent` | Segment conflict component | `LinkSegmentConflictComponent` | allocator result | development | `BandConflictGroup` | Rename now | none |
| `DeterministicRailAssignment` | Complete deterministic allocation result | `DeterministicSlotAssignment` | common diagnostics | development | legacy lane allocation | Rename now | none |
| `DeterministicRailAllocator` | Allocates reusable axis slots | `DeterministicSlotAllocator` | all common demand integrations | development | `CorridorLaneAllocator` | Rename now | none |
| `RouteInvalidationCause` | Reason a selected link path became stale | `LinkInvalidationCause` | constraint materialisation and diagnostics | development | legacy route-state replacement | Rename now | none |
| `RouteInvalidation` | Stale-link record | `LinkInvalidation` | invalidation calculator and fixture | development | legacy route-state replacement | Rename now | none |
| `SemanticRouteReference` | Revisioned semantic link reference | `SemanticLinkReference` | invalidation calculator | development | generated route state | Rename now | none |
| `RouteInvalidationCalculator` | Computes links invalidated by movement | `LinkInvalidationCalculator` | development fixture and diagnostics | development | legacy pipeline state mutation | Rename now | none |
| `BandRouteDemand` | Horizontal segment demand in one inter-layer | `InterLayerLinkDemand` | observation, grouping, reports | development | `LinkSegmentDemand` projection | Rename now | vertical-column integration |
| `BandRouteMembership` | Link membership in an inter-layer | `InterLayerLinkMembership` | observation and diagnostics | development | none | Rename now | none |
| `InterLayerBandId` | Identity of adjacent-layer routing space | `InterLayerId` | observation, grouping, reports | development | none | Rename now | none |
| `InterLayerBandObservation` | Observed inter-layer resource state | `InterLayerObservation` | diagnostic report | development | none | Rename now | none |
| `InterLayerBandObserver` | Discovers inter-layer memberships and demands | `InterLayerDemandDiscovery` | CLI trial diagnostics | development | none | Rename now | none |
| `BandConflictGrouper` | Groups conflicting horizontal inter-layer demands | `InterLayerConflictGrouper` | grouped spacing planner/tests | development | deterministic allocator grouping | Rename now; reassess duplication | vertical-column tranche |
| `BandConflictGroup` | Conflict component plus spacing proposal | `InterLayerConflictComponent` | grouped spacing planner | development | allocator component | Rename now | allocator convergence |
| `BandMembershipRole` | Link role within an inter-layer | `InterLayerMembershipRole` | observer | development | segment role | Rename now | vertical-column integration |
| `BandRouteDirection` | Direction observed in an inter-layer | `InterLayerLinkDirection` | observer | development | segment orientation | Rename now | vertical-column integration |
| `BandReturnRegionObservation` | Observed return-path area | `InterLayerReturnRegionObservation` | diagnostics | development | future return column | Rename now | return-column tranche |
| `BandFindingCorrelation` | Finding-to-inter-layer attribution | `InterLayerFindingCorrelation` | diagnostics | development | none | Rename now | none |
| `InterLayerBandTelemetry` | Inter-layer discovery counters | `InterLayerTelemetry` | diagnostics | development | none | Rename now | none |
| `InterLayerBandReport` | Discovery report | `InterLayerReport` | diagnostics | development | none | Rename now | none |
| `GroupedVerticalBandPlanner` | Historical grouped spacing proposal | `InterLayerSpacingConstraintProducer` | development fixture/tests | development | shared allocator plus persistent constraints | Rename now; then reassess | vertical-column tranche |
| `GroupedVerticalBandPlan/Result` | Inter-layer spacing proposal/result | `InterLayerSpacingConstraintPlan/Result` | planner/tests | development | generation constraints | Rename now | persistent-constraint convergence |
| `DownwardRailDemandFactory` | Builds downward segment demands | `DownwardLinkSegmentDemandFactory` | adjacent and general producers | development | none | Rename now | vertical-column producer replaces multi-band generation |
| `AdjacentDownwardRailDemandObserver` | Discovers adjacent downward demands | `AdjacentDownwardLinkDemandDiscovery` | trial | development | general producer | Rename now; later consolidate | general topology parity |
| `GeneralDownwardRailDemandProducer` | Produces one-or-more inter-layer demands | `GeneralDownwardLinkDemandProducer` | full trial | development | adjacent observer | Rename now | vertical-column tranche |
| `AdjacentDownwardCommonRailObserver` | Observes common allocation parity | `AdjacentDownwardCommonAuthorityObserver` | trial diagnostics | development | parity projection | Rename now | authority switchover |
| `CommonRail*` models/fixture | Common-authority parity/development evidence | `CommonAuthority*` | CLI trial/tests | development | none | Rename now | none |
| `TerminalDemandCalculator` | Computes node connection span and attachments | `LinkConnectionDemandCalculator` | placement and render layout | shared layout | legacy fan-out models | Rename now | none |
| `TerminalNodeDemand` | Required connection span for a node | `NodeConnectionDemand` | placement/tests | shared layout | none | Rename now | none |
| `TerminalAttachmentRequest` | Link connection request | `LinkConnectionRequest` | render layout/tests | shared layout | terminal fan-out membership | Rename now | none |
| `TerminalAttachment` | Allocated link connection | `LinkConnectionAssignment` | calculator | shared layout | none | Rename now | none |
| `TerminalAttachmentSide` | Valid top/bottom connection role | `LinkConnectionSide` | validation and rendering | shared layout | data-model side enum | Rename now | none |
| `TerminalInteractionEdges` | Discovers links interacting at connections | `LinkConnectionInteractions` | path scoring | production | fan-out grouping | Rename now | none |
| `TerminalRouteCompatibility` | Preserves connection prefix/suffix compatibility | `LinkConnectionPathCompatibility` | selectors | production | none | Rename now | none |
| `TerminalFanout*` | Connection fan-out grouping and ordering | `LinkConnectionFanout*` | production renderer | production | connection calculator | Rename now | none |
| `LogicalRoute*` state/history | Authoritative revisioned chosen geometry | `LogicalLinkPath*` | generation pipeline | shared | none | Retain temporarily | separate pipeline-wide migration |
| `GeneratedRoute` | Public generated link path | `GeneratedLinkPath` | generation result consumers | public compatibility | none | Retain temporarily | public-versioned change |
| `CommonAuthorityRouteCapability` | Trial support status for a link | `CommonAuthorityLinkCapability` | CLI trial | development | none | Rename now | none |
| `GeneralDownwardRoutePlan/Assignment` | General downward link plan/allocation | `GeneralDownwardLinkPlan/Assignment` | development trial | development | none | Rename now | none |
| `AttributedTrialRoute` | Trial link with family attribution | `AttributedTrialLink` | mixed-boundary diagnostics | development | none | Rename now | none |
| `DeficientBandAttribution` | Deficient inter-layer attribution | `DeficientInterLayerAttribution` | reports | development | none | Rename now | none |
| `CorridorObserver` and corridor models | Observes accepted production path geometry | retained legacy corridor model | legacy pipeline | normal production | new segment/column model | Retain temporarily under explicit legacy boundary | production authority review |
| `CorridorLaneAllocator` | Mutates parallel production geometry | retained legacy lane allocator | legacy pipeline | normal production | deterministic slot allocator | Retain temporarily | normal production replacement |
| `CorridorLaneGeometryCompiler` | Compiles legacy lane shifts | retained legacy compiler | legacy pipeline | normal production | future common path regenerator | Retain temporarily | normal production replacement |
| `GlobalCorridorPathSelector` | Bounded candidate selector | retained legacy selector | legacy pipeline | normal production | future common selector | Retain temporarily | production authority review |
| `RegionalCorridorPathOptimizer` | Bounded regional candidate selection | retained legacy optimiser | legacy pipeline | normal production | future common selector | Retain temporarily | production authority review |
| `RouteRepairCoordinator` | Legacy heuristic repair | retained legacy repair | legacy pipeline | normal production | forbidden in common authority | Retain temporarily under `Legacy` ownership | zero normal-production consumers |
| `CanonicalSharedNodeRouteCandidateBuilder` | Candidate construction for shared targets | retained legacy candidate builder | legacy pipeline | normal production | future common topology producers | Retain temporarily | production authority review |
| `EdgeTraversalCompiler` and traversal models | Legacy route-to-corridor projection | retained legacy compatibility | legacy pipeline | normal production | direct common path regeneration | Retain temporarily | normal production replacement |
| `ProjectOwnershipBoundsCompiler` | Bounds for project-owned vertices and path segments | project ownership bounds compiler | serializer | shared | none | Keep canonical `Bounds` name | none |
| `RoutePointContactKind` | Contact classification while comparing link paths | `LinkPathPointContactKind` | grouping and tests | development | canonical contact policy | Rename now | none |
| `SpacingConstraintScope` | Historical movement target enum | `MovementScopeKind` adapter candidate | grouped spacing | development | canonical movement scope | Retain temporarily, mark duplicate | constraint-producer convergence |
| `CapacityRequest` | Legacy corridor expansion request | retained legacy capacity record | production corridor pipeline | normal production | generation constraint | Retain temporarily | production authority replacement |
| `Region*` optimisation models | Bounded optimisation search region | retained legacy selector vocabulary | normal production | normal production | conflict component | Retain: region is accurate search vocabulary | production authority review |
| `EnvelopeIdentity` members | Stable identity of allocation envelope | `EnvelopeIdentity` | common allocator | development | none | Keep canonical | none |
| `Bounds` members and `Rect` | Two-dimensional occupied geometry | `Bounds` concept (`Rect` compatibility type) | all layout/serialization | shared | none | Retain type for compatibility | separate geometry API review |
| `MovementScope*` | Coherent placement movement identity/definition | canonical | common constraints | development | spacing scope enum | Keep | none |
| `DifferenceAlternativeComponentSolver` | Selects coherent positional alternatives and formally rejects positive cyclic SCCs | canonical development authority | common constraints | development | none | Keep | production authority decision |

## Historical Stage B and Stage C disposition

Stage letters describe completed work phases and are not active architecture. Active responsibilities are named `InterLayerDemandDiscovery`, `DeterministicSlotAllocator`, `InterLayerSpacingConstraintProducer`, and (in the later topology tranche) `LinkPathRegenerator`. Historical documents under `docs/evidence` may retain their original wording.

## Immediate deletion candidates and gates

| Candidate | Current evidence | Action/gate |
|---|---|---|
| Former embedded allocation sweep | Shared deterministic allocator is authoritative; search for remaining private allocation implementations | Delete when focused parity tests identify no consumer. |
| Projection-only common parity models | Still consumed by development diagnostics | Retain until diagnostic report is migrated to canonical assignment result. |
| Removed trial wrappers | Search compiler references and CLI dispatch | Delete any zero-consumer wrapper immediately. |
| Duplicate classification helpers | Compare with `CommonAuthorityComponentClassifier` | Delete only after exact parity test. |
| Whole-graph experimental models | Search execution entry points | Delete zero-consumer records and unreachable branches. |
| Capacity/grouping records | Some remain production-owned | Delete only zero-consumer development records; retain production corridor records until authority review. |
| Rollback experiment methods | Search for no callers and obsolete diagnostics | Delete immediately when compiler/reference audit confirms orphaning. |
| Mutual destination-cycle diagnostic | Superseded by formal SCC analysis over complete alternatives | Deleted with its focused mutual-pair tests. |

The former multi-layer zigzag transition-coordinate implementation has no remaining symbol or consumer. Long common downward links compile from one horizontal departure demand plus one `VerticalLinkColumnDemand`. The adjacent one-InterLayer topology remains deliberately separate because it has no long vertical column to allocate.

Disconnected-node production placement remains inside `PlacementPipeline` solely to preserve byte-identical normal output. `DisconnectedNodeProjectLayouter` is the development-authority replacement and its deletion gate is approval to change normal placement/parentage.

## Structural boundary

The retained normal-production implementation is explicitly owned by `LegacyRoutingPipeline`. Common-authority code must depend on canonical foundation models and may compare against legacy output only through diagnostic/projection adapters. Physical directory moves of production files are deferred until a dedicated behaviour-neutral namespace tranche because moving the tightly coupled partial exporter and internal models simultaneously would create review risk without changing ownership.
