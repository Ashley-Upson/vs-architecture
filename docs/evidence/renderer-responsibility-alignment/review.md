# Renderer responsibility alignment

Date: 2026-07-19

Baseline: `5cbbf8d`

## Conclusion

There are currently three routing architectures, not one layered implementation:

1. Normal production uses `LegacyRoutingPipeline`. It has a grouped InterLayer branch when `InterLayerSpacingConstraintProducer.Supports` succeeds and a corridor/lane/traversal/repair branch otherwise.
2. `GenerateProjectRegion` is independent of legacy input state, but reuses `RenderLayout.PositionLinks` and `RouteRepairCoordinator`. It therefore uses corridor lanes, not canonical InterLayer slots/vertical columns, as its final coordinate authority.
3. `DevelopmentCommonAuthorityTrial` starts from the complete legacy `RenderLayout`, then uses InterLayer demands, deterministic slots, vertical/return columns, movement constraints, alternative search, and mixed rollback.

The intended target is a fourth, consolidated form: project-owned placement; canonical topology families; InterLayer slots and vertical/return columns as coordinate authorities; corridor/traversal code only as obstacle-checked compilation; normalization; validation; ownership; serialization; whole-project acceptance. It does not exist end to end yet.

No routing capability was added in this pass. The current project renderer remains explicit about its actual authorities rather than claiming canonical slot integration.

## Entry-point call graphs

### Normal production

```text
DeterministicDrawioExporter.GenerateResult
  -> Prepare
     -> RenderGraph.From
     -> RenderLayout.Build
        -> PlacementPipeline.Place
        -> LegacyRoutingPipeline.Run
           -> RenderLayout.PositionLinks
              -> terminal allocation / BuildRouteCandidates / global or regional selection
           -> InterLayerDemandDiscovery.Observe
           -> [supported grouped branch]
              -> InterLayerSpacingConstraintProducer.Produce
              -> InterLayerSpacingConstraintMaterializer.Materialize
              -> LogicalRouteNormalizer.Normalize
              -> TraceabilityValidator.Validate
           -> [corridor branch]
              -> CorridorObserver.Observe
              -> CorridorLaneAllocator.Allocate
              -> CorridorLaneGeometryCompiler.Compile
              -> EdgeTraversalCompiler.Compile + Apply
              -> LogicalRouteNormalizer.Normalize
              -> TraceabilityValidator.Validate
              -> ExpandLayersForLaneDemand / rerun when required
              -> RouteRepairCoordinator.Repair
                 -> bounded candidate mutation
                 -> regional/full corridor+lane+traversal recompilation
                 -> validation
     -> CoordinateOwnershipCompiler.Compile
     -> ProjectOwnershipBoundsCompiler.Compile
     -> CoordinateOwnershipCompiler.Rebase
  -> DiagramFileBuilder.Build
```

Normal placement writes node/project coordinates. `PositionLinks` writes terminals and selected paths. The grouped branch writes layer spacing; the corridor branch writes lanes, traversal geometry, normalized paths, and repairs. Ownership compilation writes physical segments/anchors; bounds can expand projects; serialization writes XML.

### `GenerateProjectRegion`

```text
DeterministicDrawioExporter.GenerateProjectRegion
  -> ProjectRegionEligibility.Explain
  -> RenderGraph.From
  -> RenderLayout.BuildProjectRegion
     -> PlacementPipeline.Place
     -> RenderLayout.PositionLinks
     -> RouteRepairCoordinator.Repair
        -> CorridorObserver.Observe
        -> CorridorLaneAllocator.Allocate
        -> CorridorLaneGeometryCompiler.Compile
        -> EdgeTraversalCompiler.Compile + Apply
        -> LogicalRouteNormalizer.Normalize
        -> TraceabilityValidator.Validate
        -> bounded repair candidates and recompilation
     -> ExpandLayersForLaneDemand
     -> optional placement revision, PositionLinks and Repair rerun
  -> CoordinateOwnershipCompiler.Compile
  -> ProjectOwnershipBoundsCompiler.Compile
  -> CoordinateOwnershipCompiler.Rebase
  -> DiagramFileBuilder.Build
  -> invariant JSON and eligibility result
```

Inputs are semantic data and settings. No legacy coordinates, paths, lanes, or `RenderLayout` are read. Shared normal-path calls are `RenderGraph.From`, `PlacementPipeline.Place`, `PositionLinks`, `ExpandLayersForLaneDemand`, `RouteRepairCoordinator`, ownership/bounds, and serializer. The project path does **not** call `LegacyRoutingPipeline`, InterLayer discovery, deterministic slot allocation, vertical-column allocation, return-column allocation, or the development trial.

### `DevelopmentCommonAuthorityTrial`

```text
GenerateDevelopmentCommonAuthorityTrial
  -> Prepare (complete normal production and legacy RenderLayout)
  -> DevelopmentCommonAuthorityTrial.Apply
     -> InterLayerDemandDiscovery.Observe
     -> AdjacentDownward/GeneralDownward demand production
     -> PreAssignmentConstraintDemandProducer.Detect
     -> ColumnDifferenceConstraintMaterializer.Select
     -> PreAssignmentMovementPlanner.Solve
     -> PreAssignmentRouteProjection.Project
     -> GeneralDownwardCommonAllocator.Assign
        -> DeterministicSlotAllocator.Assign
        -> VerticalLinkColumnAllocator.Assign
     -> ReturnLinkCommonAllocator.Assign
        -> DeterministicSlotAllocator.Assign
        -> VerticalLinkColumnAllocator.Assign
     -> component classification / mixed-boundary attribution
     -> TraceabilityValidator.Validate in mixed graph
     -> component acceptance or rollback
  -> CoordinateOwnershipCompiler.Compile
  -> DiagramFileBuilder.Build
```

This path reads/writes legacy-relative placement and projected paths. It is historical migration evidence, not a target renderer.

### Fixture runners

Both synthetic and StandardIo extract commands enter `Program.GenerateDrawioAsync`, import a `DiagramModel` through `DiagramModelSerializer`, call normal `GenerateResult` for labelled `legacy-before`, call `GenerateProjectRegion` for `common-after` and invariants, then write three files through `IDiagramFileBroker`. Common generation is production-like code; only artifact orchestration is fixture-specific.

## Responsibility matrix

| Subsystem | Current inputs -> outputs / consumers | Paths N/P/T | Final disposition | Action |
|---|---|---|---|---|
| `LegacyRoutingPipeline` | placed graph -> routed/validated layout | Y/N/indirect | `TemporaryCompatibility` | retain normal fallback; remove per approved project slice |
| natural sizing | render nodes/settings -> widths/heights | Y/Y/Y via legacy | `TargetKeep` | one shared sizing authority |
| positional hierarchy/forest | graph -> depths/parents/order | Y/Y/read T | `TargetKeep` | retain, remove legacy-order requirements |
| subtree envelopes | placement -> owned bounds | N/N/Y | `SharedPrimitiveKeep` | integrate target placement only if needed |
| layered placement | hierarchy/settings -> node/project XY | Y/Y/T projects legacy | `TargetKeep` | authoritative target placement |
| connection allocation | link incidence/node width -> terminal points | Y/Y/reads legacy | `TargetKeep` | retain as sole node-terminal authority |
| topology-family producers | context -> `LinkPath`/demands | partial Y/PositionLinks P/Y T | `DuplicateAuthority` | consolidate before production enablement |
| candidate generation/selection | nodes/obstacles -> selected path | Y/Y/N | `TemporaryCompatibility` | replace with canonical topology compilation |
| corridor discovery | selected paths/nodes -> corridor graph | Y/Y/N | `RenameOrRelocate` | obstacle/occupancy compiler only |
| corridor lane allocation | corridor occupancy -> horizontal Y and vertical X | Y/Y/N | `DuplicateAuthority` | remove coordinate authority from target once slots/columns integrated |
| traversal compilation | lane geometry/junctions -> compiled path | Y/Y/N | `Simplify` | constrain to selected topology; no unrelated replacement |
| lane-demand expansion | findings -> moved downstream layers | Y/Y/N | `DeterministicReallocation` | target may retain bounded region regeneration |
| InterLayer discovery | placed routes -> vertical bands/demands | Y diagnostic/grouped/Y T | `TargetKeep` | make canonical routing-space authority |
| slot demand/allocation | InterLayer demands -> horizontal slot Y | grouped Y/N/Y | `TargetKeep` | integrate project renderer; then delete lane-Y authority |
| vertical columns | column demand -> vertical X | N/N/Y | `TargetKeep` | integrate project renderer as sole X authority |
| return columns | ownership envelopes -> side/column X | N/N/Y | `TargetKeep` | integrate project renderer |
| difference/persistent constraints | mixed placement conflicts -> coordinate minima | N/N/Y | `HistoricalDiagnostic` | archive tests/delete runtime with trial |
| movement planning/scopes | mixed constraints -> moved legacy placement | N/N/Y | `HistoricalDiagnostic` | whole-region placement supersedes |
| positive-cycle/alternative search | mixed alternatives -> selected/rejected movement | N/N/Y | `HistoricalDiagnostic` | preserve proof docs/tests; delete runtime with trial |
| revision/invalidation | mixed mutations -> recomputation closure | limited Y/N/Y | `Simplify` | immutable region revisions only |
| repair passes | findings -> candidate waypoint/topology mutation | Y/Y/N | `TemporaryCompatibility` | quarantine post-hoc mutations; retain compile-only primitives |
| regeneration | changed nodes/lanes -> full reroute | Y/Y/Y | `TargetKeep` | region-atomic only |
| normalization | compiled path -> canonical points | Y/Y/Y | `SharedPrimitiveKeep` | retain |
| logical validation | nodes/logical routes -> findings | Y/Y/Y | `SharedPrimitiveKeep` | retain |
| physical validation | ownership segments -> findings | incomplete all | `Unclear` | concrete missing implementation; required before enablement |
| ownership compilation | logical paths/projects -> anchors/segments | Y/Y/Y | `TargetKeep` | retain |
| boundary transition handling | ownership split -> continuous node-to-node segments | Y/Y/Y | `TargetKeep` | add label-aware boundary validation later |
| project bounds | owned vertices/paths -> project rect | Y/Y/Y | `TargetKeep` | retain |
| serialization | layout/ownership -> XML | Y/Y/Y | `SharedPrimitiveKeep` | one serializer |
| exact/equivalent parity | legacy/common geometry -> report | N/N/Y | `HistoricalDiagnostic` | delete with trial runtime |
| mixed attribution/classification | mixed interactions -> components | N/N/Y | `HistoricalDiagnostic` | delete with trial runtime |
| trial rollback | mixed candidate -> accept/reject | N/N/Y | `HistoricalDiagnostic` | whole-project fallback supersedes |
| project eligibility | semantic model/findings -> reasons | N/Y/N | `TargetKeep` | selector remains outside core renderer |
| fixture artifact wrapper | manifest -> before/after/json | N/dev/N | `TemporaryCompatibility` | retain until production selector supplies evidence |

N/P/T means normal, project, trial. `Unclear` is limited to physical validation: repository evidence proves it is absent; the needed behavior is already specified, so no product decision remains.

## Corridor, InterLayer, lanes, and slots

`CorridorObserver` derives geometric corridors from already selected route segments and obstacle boundaries. `CorridorLaneAllocator` assigns an axis coordinate to every corridor lane. `CorridorLaneGeometryCompiler` writes those coordinates back into both horizontal and vertical segments. It is therefore both occupancy model and coordinate allocator today.

`InterLayerDemandDiscovery` derives vertical bands from placed layer geometry and route membership. `DeterministicSlotAllocator` assigns horizontal axis coordinates inside a band. `VerticalLinkColumnAllocator` separately assigns vertical X. In the grouped normal branch and trial these are canonical decisions; in the project renderer they are unused.

Current project-region answers:

```text
Authoritative horizontal-segment Y source: CorridorLaneAllocator
Legacy lane authority remaining: complete in project-region compilation
Canonical slot authority remaining: implemented but not invoked by project regions
Compilation relationship: none; they are alternative authorities
Duplicate coordinate decisions: yes at architecture level, not simultaneously in one project run
```

Target resolution:

- InterLayer owns available inter-node vertical space.
- deterministic slots own horizontal Y;
- vertical/return allocators own vertical X and exterior side;
- corridors describe obstacle-free search/occupancy around fixed canonical coordinates;
- traversal compiles the selected topology and may fail, but may not independently reassign slot/column coordinates;
- repair requests deterministic reallocation/recompilation rather than nudging a single waypoint.

## Topology and traversal

Normal/project `PositionLinks` selects routes from candidate families. Corridor lane compilation may shift axes. `EdgeTraversalCompiler` may compile junction transitions and fall back. `RouteRepairCoordinator.Candidates` can insert obstacle bypass bends or offset individual segments, then recompile the interaction closure. Thus topology is replaceable after selection today.

The trial's topology producers are more explicit: general downward, vertical-column downward, same-layer return, and upward return produce demands/transitions. Cross-project/external cases are capability-gated. Multi-parent behavior is primarily placement/candidate context rather than a standalone final producer.

Traversal classification today: `TopologyCompiler`, `ObstaclePathfinder`, `TopologyRewriter`, and `LegacyFallback`. Target classification: only `ObstaclePathfinder` plus `TopologyCompiler` constrained by the selected family. Candidate enumeration belongs before selection. Repair must not become a second topology selector.

## Repair audit

| Pass | Trigger / mutation | Class | Target action |
|---|---|---|---|
| `LogicalRouteNormalizer.Normalize` | redundant/unsafe points; no node movement | `CanonicalNormalization` | keep |
| corridor/lane compilation | occupancy/capacity; rewrites segment axes | `DeterministicReallocation` currently | compile from canonical slots later |
| `EdgeTraversalCompiler` | corridor junctions; can apply alternate/fallback geometry | `BoundedTopologyRecompile` | restrict to selected family |
| `ExpandLayersForLaneDemand` | spacing/shared findings; moves layer suffix and regenerates | `DeterministicReallocation` | keep region-atomic if allocator requests it |
| capacity expansion | failed corridor capacity; moves nodes and reroutes | `LegacyCompatibilityRepair` | replace by canonical band capacity request |
| `RouteRepairCoordinator.ObstacleBypasses` | node collision; inserts per-link detour | `PostHocWaypointMutation` | quarantine/delete after canonical obstacle compiler covers it |
| `RouteRepairCoordinator.ParallelOffsets` | shared/spacing/bend; offsets one segment | `PostHocWaypointMutation` | delete from target path after slot reallocation |
| regional repair closure | candidate change; recompiles interacting links | `BoundedTopologyRecompile` | retain mechanism only with canonical demands |
| global accepted-candidate confirmation | any repair candidate | `CanonicalNormalization` safety check | retain validation, remove duplicate full compile when immutable region result supplies it |

All are deterministic and validated. The post-hoc candidates can change bends without changing node terminals; they do not move nodes or terminal sides. Their main architecture defect is bypassing slot/column authority.

## Sources of truth

| Value | Current producers/mutators | Target authority |
|---|---|---|
| project X/Y | `PlacementPipeline.PositionProjects`, bounds compiler | region placement, then bounds may expand size without relocating owned absolute geometry |
| project width/height | placement then `ProjectOwnershipBoundsCompiler` | bounds compiler over all owned geometry |
| label text bounds | not explicitly represented | measured label geometry model |
| node X/Y | placement; legacy expansions; project lane expansion | project positional placement plus atomic allocator-requested regeneration |
| node width/height | connection demand/natural sizing | natural sizing |
| positional parent/order | hierarchy/forest and legacy ordering inputs | positional forest; semantic/stable tie-break only |
| terminals | `PositionLinks.PortOffset` | connection allocator |
| horizontal Y | lane allocator in project; slots in grouped/trial | InterLayer slot allocator |
| vertical X | lane allocator in project; vertical columns in trial | vertical/return column allocators |
| return side/X | generic candidates in project; return allocator in trial | ownership-local return allocator |
| topology | candidate selection then traversal/repair rewrites | family producer; compiler may only materialize/fail |
| boundary point | ownership compiler rectangle intersections | ownership compiler consuming final path |
| ownership segmentation | `CoordinateOwnershipCompiler` | same |
| physical waypoints | ownership compiler/rebase then serializer | ownership compiler plus physical validator |

Duplicate authorities found: horizontal Y, vertical X, topology, return side, and repair-driven waypoint shape. None were removed because choosing the target implementation requires the next routing integration tranche and changing them now would alter artifacts. Their exact consolidation path is defined above.

## Old-to-new map

| Old responsibility | Intended replacement | Status / gate |
|---|---|---|
| legacy project/node placement | project positional forest placement | shared implementation exists; remove legacy orchestration per approved slice |
| corridor/lane coordinate allocation | InterLayer slots plus vertical/return columns | not integrated in project path |
| candidate/traversal route selection | family selection plus constrained obstacle compiler | partial |
| waypoint repair | deterministic slot/column reallocation, bounded family recompile, normalization | not started for post-hoc mutations |
| link-level fallback | whole-project selector outside renderer | implemented as evidence/result boundary; production selector disabled |
| legacy path projection | direct semantic-to-path generation | complete for project path |
| geometry parity | semantic/invariant JSON | complete for project evidence; trial parity remains |
| mixed movement closure | complete region placement | complete conceptually; complex allocator movement not integrated |
| legacy ownership | coordinate ownership compiler | shared and complete |
| full title avoidance | measured text bounds only | not started |

## Movement/constraint disposition

Normal consumers: grouped vertical constraints and layout revisions; not horizontal difference alternatives or mixed movement search.

Project consumers: placement revision and bounded layer expansion only. It does not consume persistent mixed constraints, difference constraints, positive cycles, movement alternatives, component search, mixed invalidation, or rollback.

Trial-only chain: parity/context projection -> constraint demands -> persistent/difference stores -> component alternative solver/positive cycles -> movement planner/projection -> invalidation closure -> mixed classifier/rollback/report.

Final concepts worth retaining are immutable constraint demands, deterministic unsatisfiable detection, atomic region acceptance, and a monotonic generation revision. The trial-specific coexistence graph is superseded by whole-region generation.

Recommended trial disposition: `ArchiveAsTestsAndDeleteRuntime`. Preserve the negative-coordinate regression, focused allocator/constraint tests, final proof reports, and legacy-before/common-after artifacts. Delete CLI trial option, runtime orchestration, parity/mixed projection/report chain, alternative movement runtime, and their runtime-only models in a dedicated deletion tranche. Expected consequence: removal of the only consumers for most persistent/difference/mixed-closure infrastructure. This pass does not execute that large deletion because normal behavior and the project artifacts must be compared after each consumer-chain cut.

## Independent renderer review

`GenerateProjectRegion` is genuinely independent of legacy state. It duplicates exporter preparation/ownership/bounds/serialization orchestration. The correct consolidation is a shared `PrepareExport` shell parameterized by a layout authority (`LegacyWholeDiagram` or `ProjectRegion`), with selection/fallback outside the renderer. This is a top-level simplification, not a second layout abstraction.

Permanent new code: result contract, semantic eligibility explanation, `BuildProjectRegion`, invariant authority metadata.

Temporary orchestration: CLI manifest/evidence mode and duplicated exporter ownership/bounds sequence.

Fixture-only code: two manifests and evidence files. They invoke the real exporter, validator, ownership compiler, and serializer.

Drift risks: duplicated exporter shell; project path's direct reuse of private `PositionLinks`; repair acting as effective topology allocator; lack of physical validation; no label-bounds model.

## Safe cleanup

Deleted `CommonAuthorityDevelopmentFixture`, its two report models, and its dedicated tests. It was a hard-coded one-off before/after generator with no runtime or evidence consumer; the executable semantic manifests supersede it.

No normal or project routing behavior changed. No active parity or mixed solver code was deleted while `DevelopmentCommonAuthorityTrial` remains callable.

## Canonical target pipeline

| Phase | Authority / model | Current implementation | Complete | Remaining overlap / deletion gate |
|---|---|---|---|---|
| semantic model | `DiagramModel` | analysis + manifest import | yes | none |
| region selection | project eligibility/whole fallback | `ProjectRegionEligibility`, CLI evidence boundary | experimental | production selector after hard-green artifact |
| natural sizing | connection-aware node dimensions | `PlacementPipeline` / demand calculator | yes | none |
| positional forest | hierarchy/parent/order | `PlacementPipeline` hierarchy | partial | remove legacy-order accidents |
| placement | `PlacedGraph` | `PlacementPipeline.Place` | yes | legacy shell still calls same primitive |
| label bounds | measured text obstacle | absent | no | required before quality pass |
| node connections | terminal demand allocation | `PositionLinks.PortOffset` | yes | extract into named target primitive |
| topology family | `LinkPath` family plan | split between `PositionLinks` and trial producers | no | consolidate families |
| bands/slots/columns | InterLayer + slot/column assignments | implemented in grouped/trial only | no in project | integrate; remove lane coordinate authority |
| obstacle compilation | corridor/traversal over fixed assignments | currently reallocates and rewrites | partial | constrain compiler |
| normalization | canonical point list | `LogicalRouteNormalizer` | yes | none |
| logical validation | target invariants | `TraceabilityValidator` | yes | distinguish logical/physical redundancy |
| ownership | physical segments/anchors | `CoordinateOwnershipCompiler` | yes | none |
| physical validation | physical segments and text obstacles | absent | no | implement before enablement |
| project bounds | owned geometry union | bounds compiler/rebase | yes | include explicit label geometry |
| serialization | Draw.io XML | `DiagramFileBuilder` | yes | share exporter shell |
| acceptance/fallback | whole-project atomic decision | explicit dev result/CLI | experimental | production selector remains disabled |

The next safe deletion is the archived development-trial runtime consumer chain. The next diagram-quality pass, after that deletion or an explicit deferral, is integration of InterLayer slot/vertical-column authority into `BuildProjectRegion`, followed by removal of lane coordinate decisions from that path. No further repair heuristic should be added first.
