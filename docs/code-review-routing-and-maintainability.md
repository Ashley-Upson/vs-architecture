# Routing and maintainability code review

## Scope and status

This is a review-only assessment of the layout, routing, ownership, serialization, diagnostics, CLI, and Visual Studio command boundaries. It does not change routing behaviour or begin the accepted layer-band migration.

The accepted layer-band design fits the repository's intended direction, but the current implementation has two correctness hazards that should be resolved before Stage A. Most corridor, traversal, selection, and repair code should then remain stable until the replacement pipeline is ready; refactoring those components now would spend effort on code intended for deletion.

## Executive findings

1. `CoordinateOwnershipCompiler.CompileLink` may normalize a validated logical route before serialization. Its reconstruction check compares physical segments with the normalized substitute rather than the authoritative validated route. This is a route-authority violation and can make diagnostics disagree with emitted XML.
2. `DiagramFileBroker.WriteTextAsync` truncates the destination before the write has completed. Cancellation or an I/O failure can leave a previously valid diagram incomplete.
3. `RenderLayout.Build` is a phase pipeline represented by local variables and parallel dictionaries rather than explicit lifecycle states. A layer-expansion retry can retain pre-expansion traceability findings and use them to decide whether the rebuilt layout needs repair.
4. Placement, candidate generation, optimisation, lane compilation, validation, expansion, and repair are composed inside one type. This makes route revision and layout revision implicit and is the principal regression risk for migration.
5. Normal Visual Studio generation eagerly creates diagnostic JSON, annotated XML, and focused outputs even when only the diagram document is consumed.
6. The legacy exporter is a public, DI-registered compatibility implementation. It is unused by in-repository production code but cannot be treated as dead without an API decision.
7. `DiagramFileBuilder` contains two substantially independent products: architecture XML serialization and data-model layout/routing. They can be separated, but that split is post-migration work except for a small typed diagnostic-metadata seam.
8. The strongest tests specify user-visible traceability, ownership, terminal, and determinism invariants. Exact coordinate and old-optimiser diagnostic tests are migration liabilities and should be rewritten or retired at the stage that removes their implementation.

## Severity-ranked findings

### Critical

#### C1. Serialization can change authoritative logical geometry

- **Location:** `DeterministicDrawioExporter.CoordinateOwnershipCompiler.cs`, `CompileLink` and `NormalizePolyline`.
- **Evidence:** `CompileLink` constructs `completePoints`, normalizes them, and in the immediate-reversal case substitutes the normalized points as `logicalPoints`. Reconstruction is then checked against that substituted sequence, not against the validated `LinkLayout` points received by the compiler.
- **Risk:** An ownership/serialization concern can remove or alter a routing bend after final traceability validation. Emitted XML may therefore differ from both the accepted route and its diagnostics.
- **Recommendation:** Make the compiler a pure coordinate-space partitioner. Reconstruct physical segments in absolute coordinates and compare them to the exact incoming logical polyline. If serialization requires a normalization rule, apply it before final validation and record the resulting route as a new validated revision.
- **When:** focused Pre-Stage A correction.
- **Blocks implementation:** yes. The layer-band pipeline requires one unambiguous route authority boundary.

#### C2. Diagram writes are destructive before success

- **Location:** `Brokers/Files/DiagramFileBroker.cs`, `WriteTextAsync`.
- **Evidence:** `new StreamWriter(path, false)` truncates the destination before `WriteAsync` completes. The cancellation token is checked before opening but is not part of the write/commit operation.
- **Risk:** I/O failure, process termination, or cancellation can replace a valid diagram with a partial file.
- **Recommendation:** write a same-directory temporary file, flush and close it, check cancellation before commit, then replace/move it over the destination using the most recoverable operation supported by the target framework. Clean up the temporary file on failure. Add failure and cancellation tests around an existing destination.
- **When:** focused Pre-Stage A correction, independently committed.
- **Blocks implementation:** yes for safe ordinary use; it does not block design work.

### High

#### H1. Stale validation findings can control a rebuilt layout

- **Location:** `DeterministicDrawioExporter.RenderLayout.cs`, `Build`, around adaptive lane-demand expansion and the subsequent repair decision.
- **Evidence:** the pipeline validates a compiled route set, may expand layers and rebuild nodes/projects/links, and then uses the earlier `traceability` value when deciding `duplicateNeedsRepair`. The final compile validates again, preventing silent invalid emission, but the repair path can be skipped or selected using findings from a different layout revision.
- **Risk:** unnecessary repair, missed repair, unstable performance, and misleading diagnostics after expansion.
- **Recommendation:** introduce a small immutable phase result carrying layout revision, route revision, geometry, and validation together. Any node/layout rebuild must invalidate route-derived state. Do not broadly refactor the old optimiser.
- **When:** Pre-Stage A pipeline seam extraction.
- **Blocks implementation:** yes; a new pipeline cannot safely compose with unversioned stale state.

#### H2. `RenderLayout` combines mutually invalidating lifecycle phases

- **Location:** `DeterministicDrawioExporter.RenderLayout.cs`, `Build`, `PositionLinks`, `ApplyRegionalOptimisation`, candidate builders, exposure routing, and expansion helpers.
- **Evidence:** 2,244 lines and approximately 58 private methods cover graph depth, placement, collision adjustment, route construction, optimisation, corridor compilation, validation, expansion, and repair. Parallel dictionaries and lists are rebuilt selectively.
- **Risk:** a local change can silently invalidate downstream geometry or diagnostics without a type or compiler boundary exposing it.
- **Recommendation:** first extract orchestration contracts and hierarchy/base-placement outputs. Leave old candidate, corridor, selector, traversal, and repair implementations intact behind a legacy routing adapter until Stage G/H.
- **When:** Pre-Stage A and Stage A.
- **Blocks implementation:** the orchestration seam blocks a controlled migration; wholesale extraction does not.

#### H3. Two public exporter operations have conflicting strictness

- **Location:** `DeterministicDrawioExporter`, `Export` versus `GenerateResult`.
- **Evidence:** `Export` calls `TraceabilityValidator.ThrowIfInvalid`, while `GenerateResult` can return a document plus advisory findings. The normal renderer uses `GenerateResult`.
- **Risk:** callers of the same exporter observe different product contracts: one receives a diagram with advisories and another gets a technical exception for geometry state.
- **Recommendation:** decide the public contract explicitly. Prefer a normal advisory generation method and a separately named strict verification operation/CLI mode. Do not encode strictness through two superficially equivalent exporter methods.
- **When:** Pre-Stage A API decision; implementation can coincide with composition-root work if compatibility requires deprecation.
- **Blocks implementation:** yes, until the authoritative generation contract is chosen.

#### H4. Normal rendering eagerly prepares all diagnostic artefacts

- **Location:** `DeterministicDrawioExporter.GenerateResult`, diagnostic builder calls; `DrawioDiagramRenderer.Render`.
- **Evidence:** the renderer consumes only `GenerateResult(...).Document`, but result construction also builds diagnostic JSON, annotated XML, and focused outputs.
- **Risk:** avoidable whole-document strings, XML work, and focused-output construction on every extension generation. This is not the dominant routing cost, but it couples normal output lifetime to development diagnostics.
- **Recommendation:** preserve one preparation pass, but separate the immutable prepared generation state from lazily materialized diagnostic artefacts. Ensure lazy generation is deterministic and thread-safe.
- **When:** Stage G composition switch or immediately after, not while old routing is being replaced.
- **Blocks implementation:** no.

#### H5. Validation and interaction analysis contain repeated quadratic route comparisons

- **Location:** `TraceabilityValidator.Validate`; `RegionalCorridorPathOptimizer.DiscoverInteractions`; global candidate scoring.
- **Evidence:** validation scans routes against nodes and route pairs against one another; regional discovery compares route pairs; global alternatives repeatedly rescore a selected graph.
- **Risk:** large graphs amplify the same segment-pair calculations across validation, region discovery, candidate evaluation, and repair. Repair telemetry has already shown this area dominating generation time.
- **Recommendation:** the band model should compute indexed occupancy/conflicts once per route revision. Avoid interim optimisation unless required to keep migration fixtures practical.
- **When:** replaced during Stages B-F and deleted at Stage H.
- **Blocks implementation:** no, provided migration runs remain practical.

#### H6. Architecture serialization depends on optimiser and traversal internals

- **Location:** `DeterministicDrawioExporter.DiagramFileBuilder.cs`, architecture edge metadata generation.
- **Evidence:** XML generation reads corridor path decisions/candidates/evaluations, regional selections, traversals, and fallback codes directly.
- **Risk:** deleting the old routing pipeline becomes an XML serializer rewrite; diagnostics schema and file emission are coupled to implementation-specific score models.
- **Recommendation:** project routing diagnostics into a serializer-owned, stable edge metadata DTO before XML writing. The DTO should describe outcomes, not expose selector classes.
- **When:** introduce the seam during Stages B-F; remove old projections at Stage H.
- **Blocks implementation:** no, but must be resolved before the Stage G switch.

#### H7. Ownership compilation has no explicit layout/route revision

- **Location:** `CoordinateOwnershipCompiler` and its project/node dictionaries.
- **Evidence:** ownership is compiled from independently supplied layout collections and logical links; compatibility is assumed rather than represented.
- **Risk:** after adaptive placement or route rebuilds, dictionaries from one revision can be combined with routes from another.
- **Recommendation:** pass one immutable validated-layout result containing owner geometry and logical routes from the same revision.
- **When:** Pre-Stage A result contract; fully enforced at Stage G.
- **Blocks implementation:** yes as part of the lifecycle seam.

#### H8. Exposure traversal can grow by path multiplicity

- **Location:** `RenderGraph` exposure path construction and recursion.
- **Evidence:** path lists and cycle guards are copied while traversing; a DAG with many converging/diverging paths can produce a number of render paths much larger than node count.
- **Risk:** high allocation and unexpectedly large render graphs in deduplicated/canonical scenarios.
- **Recommendation:** retain semantics during migration, instrument path count and allocation, and later represent shared exposure structure without cloning every prefix where product semantics allow. Do not add an arbitrary cap.
- **When:** deferred unless a concrete graph blocks migration; reassess after Stage G.
- **Blocks implementation:** no.

### Medium

#### M1. Geometry semantics are duplicated with different overlap meanings

- **Location:** `RenderLayout`, `TraceabilityValidator`, `CorridorObserver`, `RouteRepairCoordinator`, `RegionalCorridorPathOptimizer`, ownership compiler, legacy exporter, and the data-model renderer.
- **Evidence:** interval overlap, simplification, bounds, rectangle intersection, and translation each have several implementations. Some callers need closed intersection; traceability rules often require positive-length shared overlap.
- **Risk:** a shared endpoint can be treated as a collision in one phase and allowed in another.
- **Recommendation:** extract explicitly named internal primitives (`ClosedIntersects`, `PositiveLengthOverlap`, orthogonal segment/route bounds, coordinate transform). Preserve behavioural distinctions in names and tests.
- **When:** Pre-Stage A, limited to stable primitives used by both pipelines.
- **Blocks implementation:** yes for new band geometry; extraction itself must remain semantic-neutral.

#### M2. Route state is split across parallel collections

- **Location:** `RenderLayout` locals and `RenderLayout` result models.
- **Evidence:** nodes, projects, links, corridors, lanes, traversals, selection results, validation, and diagnostics can be replaced independently.
- **Risk:** invalid combinations are representable and commonly require ordering knowledge.
- **Recommendation:** introduce immutable phase results at lifecycle boundaries, not a single larger mutable context.
- **When:** Pre-Stage A orchestration seam and incrementally during Stages A-F.
- **Blocks implementation:** partly, through H1/H7.

#### M3. Route-selection state permits contradictory combinations

- **Location:** link/layout result models containing nullable global and regional selection fields.
- **Evidence:** independent nullable fields encode mutually meaningful modes without enforcing exclusivity or provenance.
- **Risk:** diagnostics and serialization can report a stale or contradictory selection.
- **Recommendation:** use a `RouteSelectionResult` union-style record with explicit producer/mode and selected candidate identity.
- **When:** when the band selector is introduced; do not retrofit every old selector.
- **Blocks implementation:** no.

#### M4. Identity and ownership boundaries are stringly typed

- **Location:** render graph, logical routes, physical segments, ownership metadata, corridor and region identifiers.
- **Evidence:** semantic node IDs, render clone IDs, logical edge IDs, physical segment IDs, project IDs, and derived corridor IDs are all strings and are passed across phases.
- **Risk:** correct-looking identifiers from different domains can be mixed; clone and semantic identity errors are difficult to detect.
- **Recommendation:** add strong composite identities only at hazardous boundaries: `RenderNodeIdentity`, `LogicalEdgeIdentity`, `PhysicalSegmentIdentity`, `LayoutRevision`, `RouteRevision`, and future `BandId`. Keep leaf serialization values as strings.
- **When:** alongside the phase result contracts and band models.
- **Blocks implementation:** no.

#### M5. Physical segment flags can encode invalid marker combinations

- **Location:** `PhysicalEdgeSegment`.
- **Evidence:** role/index and independent source-marker, target-arrow, and label flags can disagree.
- **Risk:** intermediate segments can accidentally acquire terminal styling or multiple labels.
- **Recommendation:** derive terminal decoration from segment position plus one logical-edge decoration policy; retain serialized booleans only at the XML DTO boundary.
- **When:** ownership/serialization cleanup after Stage G, unless needed for a concrete defect.
- **Blocks implementation:** no.

#### M6. Corridor and repair compilation hides whole-pipeline work

- **Location:** `RouteRepairCoordinator.Repair`, compile callbacks and regional candidate evaluation.
- **Evidence:** testing a candidate can invoke corridor observation, allocation, traversal, normalization, and validation; an accepted change compiles again.
- **Risk:** helper calls appear local but reconstruct and revalidate substantial route sets, causing unpredictable cost and complex cache invalidation.
- **Recommendation:** do not optimise this legacy path. Preserve metrics, then delete it when band selection replaces repair.
- **When:** Stage H deletion.
- **Blocks implementation:** no.

#### M7. Architecture XML metadata performs repeated linear lookups

- **Location:** `DiagramFileBuilder` architecture edge/segment emission.
- **Evidence:** per-segment metadata searches path and regional decisions and membership collections repeatedly.
- **Risk:** unnecessary allocation and rising serialization cost on highly segmented diagrams.
- **Recommendation:** build deterministic lookup dictionaries once in an architecture serialization context. Apply when extracting the serializer.
- **When:** post-Stage G serializer split.
- **Blocks implementation:** no.

#### M8. Cancellation stops analysis but not CPU-bound rendering

- **Location:** `DiagramCommands.GenerateDiagram`, deterministic rendering pipeline.
- **Evidence:** Roslyn compilation/root retrieval and analysis loops receive the token; rendering is placed on `Task.Run` but the renderer does not accept/check cancellation.
- **Risk:** Cancel closes the user's intent but a large layout/diagnostic build continues until completion; progress disposal may precede background completion handling.
- **Recommendation:** add cancellation to the future pipeline contract and check it between layer, band, candidate, validation, and serialization phases and inside bounded iteration loops. Treat cancellation distinctly from failure and advisory output.
- **When:** Stage A pipeline contract, implemented throughout Stages B-G.
- **Blocks implementation:** no, but should be designed in from Stage A.

#### M9. Diagnostic categories and reasons are strings at public boundaries

- **Location:** validation findings, repair/fallback diagnostics, diagnostic JSON.
- **Evidence:** internal violation codes coexist with public string categories and reason text.
- **Risk:** consumers cannot reliably distinguish unsupported topology, unresolved geometry, invariant failure, and technical failure.
- **Recommendation:** define stable diagnostic code and severity enums/values with route/node context; keep human messages supplementary. Technical exceptions and cancellation must remain outside geometry advisory collections.
- **When:** Stage A diagnostic contract; adapt old producers until Stage H.
- **Blocks implementation:** no.

#### M10. Exact-coordinate tests over-specify the outgoing implementation

- **Location:** `DeterministicDrawioExporterTests`.
- **Evidence:** useful invariants are mixed with assertions for exact waypoint coordinates, lane positions, candidate identities, and optimiser diagnostics.
- **Risk:** safe replacement is obscured by failures that represent old implementation detail rather than user-visible regression.
- **Recommendation:** classify tests before each migration stage. Preserve output invariants; replace exact old-pipeline assertions with band-level fixture assertions when their owning implementation is removed.
- **When:** continuously during Stages A-H.
- **Blocks implementation:** no.

### Low

#### L1. Partial-class filenames imply cohesion that the implementation lacks

- **Location:** `DeterministicDrawioExporter.*` files.
- **Evidence:** placement, routing, ownership, diagnostics, and two diagram products share a prefix despite distinct responsibilities.
- **Risk:** discoverability and ownership are poor.
- **Recommendation:** adopt responsibility-based folders after the composition switch; avoid a pre-migration namespace churn.
- **When:** post-Stage H.
- **Blocks implementation:** no.

#### L2. Some tuple/local composite results obscure meaning

- **Location:** geometry overlap helpers, scope calculations, and data-model routing helpers.
- **Evidence:** tuples are used for values that cross a meaningful algorithm boundary, while many short local tuples remain harmless.
- **Risk:** axis/scope/order values can be transposed.
- **Recommendation:** replace only cross-method lifecycle tuples with named records; retain obvious local coordinate pairs.
- **When:** opportunistically with owning component extraction.
- **Blocks implementation:** no.

#### L3. Data-model-only geometry records live beside architecture serialization

- **Location:** lower portion of `DiagramFileBuilder`.
- **Evidence:** table placement and relationship routing records are private to the data-model product.
- **Risk:** file size and false coupling.
- **Recommendation:** move them with the data-model renderer when that renderer is split.
- **When:** post-migration.
- **Blocks implementation:** no.

## `RenderLayout` responsibility map

| Responsibility | Principal methods | Inputs | Outputs | Mutable shared state | External dependencies | Proposed destination |
| --- | --- | --- | --- | --- | --- | --- |
| Pipeline composition | `Build` | `RenderGraph`, settings | complete layout/result state | nearly every phase-local collection | normalizer, validator, corridor, traversal, selectors, repair | `LayoutGenerationPipeline` orchestrator |
| SCC and hierarchy depth | `CalculateDepths`, `StrongComponents` | graph nodes/links | component/depth maps | local dictionaries/stacks | render graph | `Layout/Hierarchy/HierarchyAnalyzer` |
| Base layer sizing and placement | width/offset/position helpers around the first placement block | nodes, depths, dimensions, settings | absolute node rectangles | node-position dictionary | geometry/settings | `Layout/Hierarchy/BaseLayerLayout` |
| Exposure/canonical placement | `PositionExposureTrees`, rooted-forest/traversal-depth/gap/measure/place helpers | exposure paths, canonical nodes, base placement | exposure node positions | placement maps, visited/path state | `RenderGraph` exposure model | `Layout/Exposure/ExposureLayout` during Stage A |
| Parent/child centring | centring and subtree measurement helpers | hierarchy and placed rectangles | shifted node rectangles | shared position dictionary | geometry | retain inside hierarchy/exposure layout |
| Collision and baseline adjustment | baseline, external-node, relaxation, collision and corridor-reservation helpers | current rectangles/links/settings | mutated rectangles and offsets | node rectangles | route-related spacing assumptions | extract only stable collision primitive; replace placement policy in Stage A |
| Project bounds | project-position/bounds helpers | project ownership, node rectangles | project rectangles | project map | ownership expectations | `Layout/Projects/ProjectBoundsCalculator` |
| General link construction | `PositionLinks`, candidate helpers, route construction near file end | placed nodes, links, settings | `LinkLayout` candidates/selection | candidate and terminal maps | selectors/geometry | legacy adapter; delete Stage H |
| Global selection | selection block inside `PositionLinks` | route alternatives, current route set | chosen route/path decision | selected-link collection | global selector | leave intact; delete Stage H |
| Regional optimisation | `ApplyRegionalOptimisation` and helpers | selected links/interactions | replacement choices | link collection, regional metadata | regional optimiser | leave intact; delete Stage H |
| Terminal/fan-out ordering | terminal allocation and fan-out methods | source/target geometry, sibling edges | ports, prefix/suffix geometry | grouping/order dictionaries | settings/geometry | preserve behavioural tests; replace with band terminal allocator in Stages D-E |
| Exposure link routing | exposure link/candidate/fan-out block | exposure layout and graph links | exposure `LinkLayout`s | link/terminal maps | legacy candidate builder | leave intact until band routing supports canonical/exposure cases; delete Stage H |
| Corridor/capacity orchestration | calls in `Build`, capacity-pass helpers | selected logical routes | corridors, lanes, compiled links | several parallel route products | observer, allocator, compiler | legacy routing adapter; delete Stage H |
| Adaptive expansion | `ExpandLayersForLaneDemand` and supporting calculations | capacity requests, layout | shifted/rebuilt layout | nodes/projects/routes invalidated unevenly | lane allocation | Stage C-F band-demand feedback with explicit revision |
| Traversal/junction compilation | calls in `Build` | lane-compiled geometry | traversals/junction diagnostics | traversal maps | traversal compiler | leave intact; delete Stage H |
| Normalization and validation | calls in `Build`, local `Simplify` | compiled links/nodes | normalized routes/findings | link collection replaced | normalizer, validator | shared normalization/validation boundary |
| Repair | repair decision and coordinator call in `Build` | invalid/interacting routes | repaired compiled routes/history | route state/history | repair coordinator/full compiler | leave intact; delete Stage H |

### Hidden ordering assumptions

- Depth and placement must complete before terminal allocation, but the dependency is not represented by types.
- Project bounds depend on final owned visual geometry, while route compilation can create later owned geometry.
- Corridor observation is valid only for the exact selected route revision.
- Lane compilation and traversal are valid only for the exact corridor/allocation revision.
- Adaptive expansion invalidates node positions, terminal assignments, candidates, corridors, lanes, traversals, validation, and ownership inputs together.
- Repair eligibility must use findings from the same compiled route revision.
- Coordinate ownership must occur only after the final validated logical route and must not normalize it.

### Duplicated versus canonical branches

Exposure-tree placement, canonical first-placement behaviour, duplicate-specific repair eligibility, exposure-link routing, and fan-out construction contain mode-dependent branches. These should not be generalized inside the legacy type. Stage A should expose a placement result that records render identity and canonical/clone provenance; the band router should then consume that common result without moving canonical nodes.

### Extraction versus deletion guidance

Extract before migration only SCC/hierarchy analysis, base/exposure placement, project-bounds calculation, phase-result contracts, and stable geometry. Keep legacy candidate construction, corridor observation, lane allocation, traversal, global/regional selection, and repair behind an adapter. Delete those together after Stage G rather than polishing them individually.

## Geometry duplication matrix

| Operation | Implementations | Behaviour differences | Tolerance differences | Recommended owner |
| --- | --- | --- | --- | --- |
| Interval overlap | `RenderLayout.RangesOverlap`; validator axis/shared-segment helpers; corridor observer overlap helpers; legacy exporter | closed contact, positive-length shared segment, and corridor proximity are conflated by similar names | generally exact coordinates; spacing is sometimes applied before comparison | `Geometry.AxisInterval` with explicit closed/positive-length APIs |
| Point/segment and segment/rectangle intersection | shared `Segment.Intersects(Rect)`; validator; candidate validity; legacy `LineSegment`/`Rect`; data-model router | terminal contact may be allowed by validators while generic rectangle intersection is not terminal-aware | exact arithmetic; clearance is caller-added inconsistently | `Geometry.OrthogonalSegment` plus policy-aware route collision service |
| Route/node collision | validator; candidate builder; repair; legacy exporter; data-model relationship routing | some exclude terminals, some inflate obstacles, some test only candidate stages | configured clearance versus zero clearance | `Validation.RouteObstacleValidator` using shared geometry |
| Collinearity/simplification | `RenderLayout.Simplify`; `LogicalRouteNormalizer`; ownership `NormalizePolyline`; repair `Normalize`; legacy `SimplifyPoints`; data-model `SimplifyRoute` | duplicate removal, reversal removal, and collinear bend removal occur in different combinations | exact equality | one pre-validation `PolylineNormalizer`; serializer performs none |
| Immediate reversal removal | logical normalizer and ownership special case | ownership may change an already validated route | exact equality | logical normalization before final validation only |
| Route bounds | regional optimiser, repair coordinator, project-bound enumeration, diagnostic helpers | some include terminals/clearance, others raw points | raw versus inflated bounds | `Geometry.Polyline.Bounds`, explicit inflation at call site |
| Coordinate translation | ownership `ToRelative`/rebase; serializer node subtraction; render-layout shifts; data-model offset helpers | node and edge conversions live in different layers | exact | `Geometry.CoordinateTransform`; ownership selects transform, serializer applies DTO values |
| Absolute/project-relative conversion | ownership compiler for edges; `DiagramFileBuilder` for vertices | conversion authority is split between compiler and serializer | exact | ownership compiler produces fully relative physical DTOs for both vertices and edges |
| Junction/corridor intersection | corridor observer horizontal/vertical nesting; traversal builder | specialised topology semantics not reusable as generic collision | exact orthogonal coordinates | future `Routing/Bands` topology types built on shared segment primitives |

Stable geometry should live under `Services/Processings/Drawios/Geometry` (or a namespace-independent internal geometry area) and remain unaware of render nodes, projects, corridors, bands, XML, or diagnostics.

## Dependency and lifecycle coupling map

| From | To | Classification | Assessment |
| --- | --- | --- | --- |
| `RenderGraph` | semantic diagram/settings | appropriate | Converts semantic identities into render identities and exposure structure. |
| `RenderLayout` placement | route candidate construction | responsibility inversion | Placement owns candidate policy and terminal geometry. Separate at the placed-graph boundary. |
| `RenderLayout` placement | global/regional optimisation | responsibility inversion | Placement invokes selection instead of returning immutable positions. |
| `RenderLayout.Build` | corridor/lane/traversal/repair stack | temporary migration coupling | Keep behind a legacy routing adapter until Stage G. |
| corridor observations | selected logical routes | hidden lifecycle coupling | No route revision proves observations match the current points. |
| allocation/traversal | corridor observations | appropriate concept, hidden lifecycle in representation | The conceptual flow is valid; revision compatibility is implicit. |
| validation | multiple route-stage representations | hidden lifecycle coupling | Callers can validate selected, compiled, normalized, or reconstructed geometry without a stage type. |
| ownership | mutable layout dictionaries and logical links | hidden lifecycle coupling | Layout and route revision compatibility is assumed. |
| ownership | route normalization | responsibility inversion | Ownership must partition coordinates, not alter route shape. |
| serializer | physical ownership DTO | appropriate | XML emission should consume final physical cells. |
| serializer | optimiser scores/traversals/fallback internals | responsibility inversion | Project stable diagnostic metadata before serialization. |
| diagnostics | selector-specific score schemas | temporary migration coupling | Adapt old schemas now; replace with stable outcome/metrics at Stage G/H. |
| VS command | Roslyn project snapshots and Core services | appropriate | VS-only selection/dialog work stays on UI thread; analysis/rendering are background tasks. |
| legacy exporter DI registration | in-repository production consumers | dead dependency internally | Registration has no internal resolution, but public API makes the implementation compatibility-sensitive. |
| `DiagramFileBuilder.Edge(LinkLayout)` direct logical-edge serializer | current ownership flow | probable dead dependency | Confirm with compiler/reference search before deletion; current architecture output uses physical segments. |

The accepted plan's boundaries are sound if `RenderGraph`, placed graph, logical route, validated route, ownership compilation, and physical serialization become explicit immutable boundaries. A `BandRouter` must not depend on legacy corridor, selector, traversal, or repair types.

## Performance hotspots

| File/method | Input size | Current complexity | Calls per generation | Allocation concern | Plan outcome | Interim fix? |
| --- | --- | --- | --- | --- | --- | --- |
| `TraceabilityValidator.Validate` | routes `E`, nodes `V`, segments `S` | route/node about `O(E*V*S)` plus route pairs about `O(E^2*S^2)` | initial, compiled, repair/candidate and final paths | findings and repeated segment enumeration | band occupancy/conflict index can share work | no, except avoid duplicate unchanged-revision validation |
| `RegionalCorridorPathOptimizer.DiscoverInteractions` | routes/segments | `O(E^2*S^2)` | at least once when regional processing is enabled | interaction sets and pair scoring | removed Stage H | no |
| `GlobalCorridorPathSelector.Select` | alternatives `A`, route pairs | bounded passes, potentially `A * O(E^2*S^2)` scoring | once per selection/reselection | candidate route sets and score objects | replaced by bounded band scoring | no |
| `RouteRepairCoordinator.Repair` compile callback | invalid region size and alternatives | repeated full regional compile/validation per candidate | zero to many; dominant on difficult graphs | repeated corridors, lanes, traversals, route histories | removed Stage H | only preserve metrics and avoid accidental duplicate calls |
| `CorridorObserver.BuildJunctions` | horizontal `H`, vertical `V` segments | `O(H*V)` plus grouping/sorting | every compile, including repair alternatives | groupings and materialized observations | band/junction model replaces it | no |
| `RenderLayout.Build` capacity passes | nodes/routes | repeated placement/link construction and complete downstream compilation | initial plus up to bounded retries | invalidates and rebuilds most collections | explicit band-demand loop | no structural optimisation before replacement |
| `RenderGraph` exposure traversal | graph paths | proportional to path multiplicity, potentially exponential in DAG depth | once | cloned path lists and cycle guards | not automatically removed | instrument; redesign only from concrete evidence |
| `DiagramFileBuilder` metadata lookup | physical segments `P`, decisions `D` | repeated `O(P*D)`/membership scans | once | repeated enumerators/strings | remains after routing change | build lookup context during serializer split |
| `GenerateResult` diagnostic construction | document size and focused cases | multiple whole-output preparations | every normal render | several large XML/JSON strings | composition split enables lazy diagnostics | worthwhile at Stage G, not before |

Repeated sorting exists throughout corridor grouping and candidate organisation, but replacing those legacy components is preferable to tuning their LINQ chains. Deterministic ordering must remain explicit in the new band model.

## Test review

### `DeterministicDrawioExporterTests.cs`

| Test characteristic | Classification | Action |
| --- | --- | --- |
| byte determinism, stable IDs/order | retain unchanged | Keep as migration gate. |
| no shared non-zero-length segments, configured spacing | retain unchanged | Keep; express through geometry helpers where possible. |
| bottom source/top target terminals and monotonic fan-out | retain unchanged | Keep as product invariants. |
| ownership segmentation, project-relative geometry, exact reconstruction, save/reopen metadata | retain unchanged | Keep; add an explicit assertion that serialization never alters the validated logical polyline. |
| canonical first placement and project-owned external diamonds | retain unchanged | Keep as placement/ownership gates. |
| exact intermediate lane/corridor coordinates | retain but rewrite assertion | Assert clearance, order, spacing, and traceability instead of an old lane number/Y value. |
| global/regional candidate signatures, optimiser scores, repair/fallback internals | replace during migration | Add band-level candidate and selection fixtures when the corresponding stage arrives. |
| tests whose only subject is a deleted corridor/repair implementation | delete with old pipeline | Remove at Stage H, after replacement invariants pass. |
| stopwatch/performance thresholds and real-project generation | move to integration suite | Keep deterministic artefacts and telemetry outside ordinary unit runs. |

The file should later be divided into focused fixture suites: placement, terminal allocation, route geometry, validation, ownership/serialization, determinism, and migration compatibility. Shared graph/settings/XML inspection belongs in test builders, not in one exporter test class.

### `DrawioExporterTests.cs`

These tests instantiate the legacy exporter directly. First identify any user-visible style, escaping, data-model, or basic XML behaviours not covered by deterministic exporter tests and port those as compatibility gates. Delete implementation-specific routing/layout assertions with the legacy implementation. The suite should not force refactoring a compatibility implementation scheduled for removal.

### `RoslynDependencyAnalyzerTests.cs`

These are largely trustworthy semantic-analysis specifications and are outside the routing replacement. Retain them. Extract repeated in-memory project/source construction into a focused Roslyn test fixture, but do not rewrite their semantic assertions as part of routing migration.

### Missing tests

- Process-level CLI tests for default advisory output, explicit strict mode, exit codes, settings loading, and diagnostics generated from the same preparation result.
- File-output tests proving cancellation or write failure cannot corrupt an existing diagram.
- Visual Studio command harness tests for cancellation/disposal and for keeping DTE/workspace service access on the UI thread.
- An automated, opt-in real-project comparison suite recording counts, timings, violations, and deterministic hashes without burdening ordinary unit runs.
- Phase-boundary contract tests proving layout/route revision compatibility and authoritative geometry preservation.

## Legacy exporter status

**Classification: compatibility implementation.**

- `IDrawioExporter` and `DrawioExporter` are public.
- The implementation is registered by `IServiceCollectionExtensions.FoundationServices.cs`.
- In-repository production rendering resolves `IDeterministicDrawioExporter`; no in-repository production call to the legacy interface was found.
- `DrawioExporterTests` directly instantiate the legacy class.
- It substantially duplicates layout, geometry, XML, and style work. No architecture-routing capability should be migrated from it.

Deletion requires an explicit public API decision:

1. Mark `IDrawioExporter` and `DrawioExporter` obsolete in a compatibility release.
2. If binary/source compatibility is required, make the legacy implementation a thin adapter to a supported deterministic public operation where semantics match; preserve the DI registration during the deprecation window.
3. Port unique product-behaviour tests before deleting implementation tests.
4. Remove the public types and DI registration only in an approved breaking release, or retain a permanent shim if the package contract demands it.

Do not refactor its internal routing. The data-model implementation in `DiagramFileBuilder` is separate and does not justify retaining the legacy architecture exporter.

## `DiagramFileBuilder` split assessment

The file can be split cleanly along product boundaries.

### Architecture-only responsibilities

- project, internal/external node, boundary-anchor, and physical edge-segment cells;
- ownership-relative geometry;
- logical dependency metadata and segment roles;
- architecture routing/validation diagnostic metadata;
- architecture page ordering and deterministic IDs.

### Data-model-only responsibilities

- entity/table sizing and placement;
- column/field rendering;
- radial/table overlap adjustment;
- relationship candidate generation, routing, lane choice, scoring, and simplification;
- data-model-only geometry records.

### Shared responsibilities

- `mxGraphModel`/root/page construction;
- safe XML attribute/value writing;
- basic vertex/edge geometry elements;
- stable style fragments and document metadata.

### Proposed types

- `Serialization/DrawioDocumentWriter`: shared graph model, cell, geometry, and XML helpers.
- `Serialization/ArchitecturePageSerializer`: consumes final architecture physical DTOs and stable diagnostic metadata only.
- `DataModels/DataModelDiagramLayout`: all table placement and relationship routing.
- `Serialization/DataModelPageSerializer`: emits the data-model layout.
- `Serialization/ArchitectureEdgeMetadata`: stable projection independent of old/new routing implementation.

Architecture serialization should not normalize or repair geometry. The data-model renderer may retain its own domain-specific route simplification, but it should not masquerade as a shared architecture primitive.

Perform the large split after Stage G/H to avoid simultaneous routing and serializer churn. The small metadata DTO seam is appropriate before the composition switch.

## API compatibility assessment

| Surface | Current role | Proposed impact | Compatibility class |
| --- | --- | --- | --- |
| `IDrawioExporter` / `DrawioExporter` | public legacy exporter | deprecate, adapt, then remove only with approval | public binary/API and DI affecting |
| `IDeterministicDrawioExporter` and generation result | public/core generation contract | clarify advisory versus strict operation; preserve normal rendering | public API affecting |
| renderer registrations | selects deterministic renderer | switch internal implementation behind same renderer | DI affecting but can be binary compatible |
| Draw.io settings models/JSON | persisted CLI/extension behaviour | layer-band settings need schema/version/default decision | settings-schema affecting |
| CLI options and exit semantics | default/strict output contract | add or clarify explicit strict behaviour without changing default silently | CLI contract affecting |
| Visual Studio command | project selection, settings, save/progress | Core pipeline replacement should remain invisible | VSIX command affecting if progress/cancellation contract changes |
| internal route/corridor/traversal types | migration implementation | delete after Stage G | internal-only unless exposed through public result models |
| `InternalsVisibleTo` test APIs | direct algorithm testing | replace with band component tests | test-visible internal |
| physical ownership metadata in XML | manual editing and reconstruction | preserve names/semantics | persisted artefact compatibility |

Before deleting old public types or changing persisted settings, confirm package compatibility expectations and whether existing consumers deserialize generation result/diagnostic models.

## Threading and cancellation assessment

`DiagramCommands.GenerateDiagram` switches to the Visual Studio UI thread before reading selection, DTE/workspace state, settings UI, and the save path. It passes selected Roslyn `Project` snapshots to background analysis and does not appear to use DTE or `VisualStudioWorkspace` services inside the background delegate. Roslyn project/solution snapshots are designed for asynchronous compiler operations, so this boundary is reasonable.

Analysis propagates cancellation to compilation and syntax-root retrieval and checks it in major declaration/type/registration/constructor loops. Rendering runs in `Task.Run` but is not cooperatively cancellable. Progress updates rely on the joinable-task/UI context; completion/cancellation must avoid updating a disposed dialog. No unsafe background DTE access was identified, but this deserves a VS integration test rather than inference alone.

The band pipeline should check cancellation:

- before and after hierarchy/base placement;
- between layer and band construction;
- per dependency or bounded candidate batch;
- per bounded selection/normalization iteration;
- before validation, ownership compilation, diagnostic materialization, and file commit.

Cancellation is not a geometry finding and must not produce a partial normal or diagnostic artefact.

## Error and diagnostic boundary

The target model should distinguish:

| Outcome | Representation |
| --- | --- |
| technical generation failure | exception with operation/context; CLI non-success exit; no replacement of existing output |
| unsupported route shape | stable diagnostic code with edge and topology context; fallback may still render |
| resolved layout demand | informational diagnostic/telemetry, not a violation |
| unresolved geometry advisory | typed finding with edge(s), node(s), stage, and final geometry revision |
| development invariant violation | dedicated internal exception/assertion; never silently converted to a user advisory |
| user cancellation | cancellation result/exception; no error advisory and no output commit |

Diagnostics must be derived from the exact final validated logical route and, where relevant, the reconstructed physical route. Deduplicate findings by stable code plus involved identities and geometry revision. Selector scores and fallback prose should not be the public diagnostic category.

## Proposed eventual file structure

```text
Drawios/
  Graph/
    RenderGraph.cs
    RenderIdentity.cs
  Geometry/
    AxisInterval.cs
    OrthogonalSegment.cs
    Polyline.cs
    CoordinateTransform.cs
  Layout/
    HierarchyAnalyzer.cs
    BaseLayerLayout.cs
    ExposureLayout.cs
    ProjectBoundsCalculator.cs
    PlacedGraph.cs
  Routing/
    Bands/
      BandGraph.cs
      BandCandidateBuilder.cs
      BandAllocator.cs
      BandRouteCompiler.cs
    Normalization/
      LogicalRouteNormalizer.cs
    Legacy/
      LegacyRoutingAdapter.cs
  Validation/
    TraceabilityValidator.cs
  Ownership/
    CoordinateOwnershipCompiler.cs
  Serialization/
    DrawioDocumentWriter.cs
    ArchitecturePageSerializer.cs
    ArchitectureEdgeMetadata.cs
    DataModelPageSerializer.cs
  DataModels/
    DataModelDiagramLayout.cs
  Diagnostics/
    GenerationDiagnostic.cs
    DiagnosticArtefactBuilder.cs
```

Current corridor, allocation, traversal, global/regional selection, and repair files should temporarily reside behind `Routing/Legacy` conceptually; physically moving them before deletion would create noise with no product value.

## Extraction sequence

### Pre-Stage A: correctness and stable seams

1. Correct ownership compilation so it cannot alter authoritative geometry; add exact reconstruction tests against the incoming validated route.
2. Make diagram file replacement recoverable/atomic and test cancellation/failure with an existing destination.
3. Decide and document advisory versus strict public exporter semantics.
4. Extract stable geometry primitives with explicit contact/overlap semantics and characterization tests.
5. Define immutable `PlacedGraph`, `ValidatedLogicalRoutes`, and generation-phase results carrying layout/route revisions and cancellation.
6. Thin `RenderLayout.Build` into orchestration over the existing implementation without changing route behaviour. Wrap the old route stack in one legacy adapter rather than decomposing it.

### Stage A: placement boundary

1. Move SCC/depth, base hierarchy placement, exposure/canonical placement, and project bounds into focused components.
2. Preserve canonical first placement, duplicate render identity, external ownership, and all accepted placement tests.
3. Produce one immutable placed graph consumed by either router.

### Stages B-F: layer-band pipeline

1. Introduce band identifiers, topology, demand, terminal allocation, candidate generation, bounded selection, and compilation without referencing old corridor/selector/repair models.
2. Reuse shared geometry and validation only.
3. Add stable diagnostic projection and cancellation points at each bounded phase.
4. Run old and new pipelines from the same placed graph for comparison, not through shared mutable dictionaries.

### Stage G: composition switch

1. Make the band pipeline authoritative behind the existing renderer/CLI/VS contracts.
2. Keep final validation, coordinate ownership, physical segmentation, and XML output unchanged.
3. Materialize optional diagnostic artefacts from the same immutable generation result.

### Stage H: deletion

Delete legacy route candidate construction in `RenderLayout`, corridor observation/allocation/compilation, traversal/junction compilation, global and regional selectors, repair coordination, legacy-only score/history models, and their implementation-specific tests. Retain only independently useful validation and normalized geometry primitives.

### Post-migration

Split architecture and data-model serialization, reorganize namespaces/files, decide legacy public exporter removal, reorganize large test fixtures, and consider exposure graph representation from measured evidence.

## Deletion candidates

- old corridor observer, capacity/allocation, and lane compiler types after Stage G equivalence;
- old traversal/junction compiler and fallback score types after supported/fallback band topology coverage;
- global/regional corridor path selectors and their candidate/evaluation models;
- route repair coordinator and legacy compilation history once the new bounded selector owns alternatives;
- legacy route candidate/fan-out construction remaining in `RenderLayout` after band terminal/candidate stages pass compatibility gates;
- old optimiser/corridor implementation tests;
- probable unused direct logical-edge serializer overload after reference confirmation;
- legacy exporter implementation after public API/DI deprecation decision (or replace it permanently with a shim).

## Migration blockers

1. Ownership serialization must preserve the exact validated logical route.
2. Output writing must not corrupt an existing diagram on failure/cancellation.
3. Layout and route revisions need an explicit immutable phase boundary so stale findings cannot control rebuilt geometry.
4. Normal advisory versus strict exporter semantics require a named contract.
5. Shared geometry semantics must distinguish endpoint contact from positive-length overlap before band occupancy is implemented.

## Deferred cleanup

- Physical file/namespace reorganization.
- Micro-optimising old corridor/repair LINQ and sorts.
- Rewriting the legacy exporter internally.
- Full `DiagramFileBuilder` split.
- Replacing every string ID or tuple.
- Redesigning exposure path representation without a measured failing graph.
- Making multi-parent physical edge segments select as one logical edge in diagrams.net.

## Open questions requiring approval

1. Should `IDrawioExporter` remain supported through a permanent deterministic adapter, or may it be removed in a future breaking package version?
2. Should `IDeterministicDrawioExporter.Export` retain strict refusal semantics under a clearer name, or should normal advisory generation become the sole public behaviour with strictness confined to CLI/test tooling?
3. May diagnostic JSON evolve to stable typed codes with a schema version, or must its current string fields remain backward compatible?
4. May future layer-band settings add a persisted settings-schema version and migration defaults, or must the existing JSON remain shape-compatible without versioning?
5. Is same-volume atomic replacement sufficient for output safety, or is a backup/recovery file required when replacing an existing user diagram?

## Review conclusion

The accepted layer-band plan matches the real dependency structure when it begins at an immutable placed-graph boundary and ends at the existing validated-route/ownership boundary. The safe strategy is a small correctness-and-seams tranche, focused placement extraction, parallel construction of the new router, one composition switch, and wholesale deletion of the old routing stack. Broad refactoring of corridor, traversal, selection, repair, or the legacy exporter before replacement would increase risk without improving the migration path.
