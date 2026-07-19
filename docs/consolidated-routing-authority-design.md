# Consolidated routing authority design

> Vocabulary note: active common-authority code now uses link, link path, link segment, link transition, link connection, inter-layer slot, and vertical link column. Historical evidence below retains the terminology used when it was captured.

Date: 2026-07-18. This document defines the agreed target and the first shared foundations. It does not replace the historical audits and it does not claim the current legacy/grouped migration is complete.

## Confirmed product rules

Every semantic dependency must remain present and traceable. Final authoritative routes must be orthogonal, avoid node interiors, contain no immediate reversal, and avoid ambiguous collinear sharing, bends or endpoints. Required horizontal and vertical space is created; diagram dimensions have no product limit. Algorithm guards may diagnose non-convergence but may not impose canvas, node, route, layer, lane or project limits.

A clean perpendicular crossover is permitted when it is strictly interior to both straight segments and neither route bends, begins, ends, merges or branches there. Route quality after hard validity prefers reasonable directness, fewer avoidable crossings, fewer bends, shorter length and less unrelated movement. Global mathematical shortest-path optimality is not required.

Each semantic link is independently routed. Sharing a semantic node, project or layer does not alone create an interaction component. Coupling arises only from competition for a visual resource: the same terminal side/region, rail interval, assigned turn, obstacle bypass, overlapping movement scope or affected project boundary.

Moving a node changes only that node rectangle by default and invalidates incident route geometry. It does not move dependency-connected nodes. A subtree, sibling suffix, lower-layer suffix or project suffix moves only when explicitly selected as the smallest coherent scope preserving order and containment.

## Node width and padding semantics

For each node:

```text
required width = max(
    current width,
    measured text width + configured node padding,
    max(incoming attachment span, outgoing attachment span) + configured node padding)
```

`Layout.LinkNodeWidthPadding` retains its existing public meaning: **one total horizontal amount**, not a per-side value. A 20 px setting means a 10 px usable-edge inset on each side. It is added once to text or attachment span and is neither doubled nor halved.

The attachment separation is the existing production terminal separation:

```text
max(Layout.EdgePortSpacing, 2 * Layout.ParallelLaneSpacing)
```

For `n` attachments on one side, required span is `(n - 1) * separation`. Incoming/top and outgoing/bottom are measured independently and combined with `max`, never sum. The stable allocation order is remote X coordinate then logical route ID. Positions are centred on the node edge, meet minimum separation and remain within the padded usable width. Incoming and outgoing groups may reuse the same horizontal span.

The first production integration is `PlacementPipeline.CalculateWidths` and `TerminalDemandCalculator`. It corrects a mismatch where width used all incident links and 5 px `EdgePortSpacing`, while actual terminal allocation used independent sides and normally 24 px. The common allocation helper now supplies the existing `PortOffset` behaviour.

Base placement calculates width before route geometry exists, so no live route is translated. If a later iteration increases width, it raises `MinimumWidth` for that node, creates a new layout revision, and issues `EndpointResized` invalidations for incident routes. Linked nodes do not move unless a separate explicit spacing scope requires it.

## Canonical contact classification

`CanonicalContactClassifier` owns geometry facts; consumers own policy.

| Fact | Geometry meaning |
|---|---|
| `Disjoint` | no contact and no configured near-parallel conflict |
| `NearParallelSpacingConflict` | positive projected overlap with axis separation below required spacing |
| `PositiveCollinearOverlap` | same axis with positive-length overlap |
| `EndpointToEndpoint` | contact at endpoints without a more specific classification |
| `EndpointToInterior` | one endpoint touches the strict interior of the other segment |
| `SharedBend` | two collinear endpoint contacts both turn at the point |
| `StraightContinuation` | collinear endpoint contact without either route turning |
| `CleanPerpendicularCrossover` | strict interior of both straight segments; no bend/terminal |
| `BendInvolvedPerpendicularContact` | perpendicular contact involving a turn |
| `TerminalContact` | contact explicitly marked as a semantic terminal |
| `IntentionalSemanticJunction` | caller identifies the contact as a modelled merge/branch |

The classifier never chooses repair, movement or fallback. `BandConflictGrouper.ClassifyContact` is the first adapter: it consumes canonical facts while preserving the current Stage C policy, including its conservative treatment of any supplied continuation. Remaining duplicate predicates are in `TraceabilityValidator`, global/regional interaction discovery, route candidate scoring, obstacle/crossing helpers and junction metrics. They remain until parity fixtures demonstrate the same locations, magnitudes and policies.

Optional diagnostics classify final segment pairs through the canonical classifier. This scan is not added to normal generation.

## Common rail model

The branch-neutral geometry is deliberately small:

```text
RailDemand
    stable demand and route identity
    orientation
    occupied interval
    preferred axis and allowed axis range
    semantic role
    terminal/turn order
    optional movement scope
    placement and route revisions

AssignedRail
    demand and route identity
    orientation, final axis and lane index
    occupied interval and role
    placement and route revisions

RailTransition
    route and ordered from/to assigned rails
    orthogonal turn coordinate
    placement and route revisions
```

Roles are `TerminalDeparture`, `TerminalArrival`, `Through`, `Return`, `ObstacleBypass` and `TurnTransition`. Draw.io parentage and coordinate ownership are intentionally excluded.

### Lossless mappings and specialised metadata

| Existing model | Common mapping | Metadata remaining specialised |
|---|---|---|
| `RoutingCorridor` | orientation, axis range/envelope and usage intervals become rail-group context | corridor role, junction identities and discovery envelope |
| `AllocatedCorridorLane` | edge/demand ID, lane index, offset/axis and interval become `AssignedRail` | failed-corridor/capacity bookkeeping |
| `BandRouteDemand` | route ID, horizontal interval, direction/order, revisions become `RailDemand` | upper/lower layer membership and return-region observation |
| Stage C `AssignedLanes` | lane index and band-derived axis become `AssignedRail` | group constraint origin and convergence telemetry |
| terminal fan-out membership | route, side, remote order and occupied departure region become terminal rail demand/order | semantic source/target side permission |
| terminal transition | short departure/arrival rail plus transition | terminal attachment ratio/side semantics |
| return region | exterior interval and side become return rail demands | side-choice feasibility and return topology |
| corridor/junction traversal | ordered assigned rails and transitions | unsupported-topology diagnostics |

The common models are currently contracts and diagnostic/test foundations. They do not replace corridor, Stage B or Stage C types in this tranche.

## Persistent generation constraints

`GenerationConstraintStore` persists across the generation/convergence lifetime. Constraints are `MinimumX`, `MinimumY`, `MinimumWidth` and `MinimumHeight`, keyed by a stable `MovementScopeIdentity`. Merge is monotonic:

```text
stored minimum = max(stored minimum, proposed minimum)
```

Materialisation always reads immutable base rectangles plus the complete store. Previously translated rectangles are never promoted to a new base.

Initial scope types and actual membership rules:

| Scope | Owner and membership |
|---|---|
| `Node` | one stable render-node ID; only that rectangle |
| `LayoutSubtree` | hierarchy node owner and all visual descendants in that stable hierarchy revision |
| `OrderedSiblingSuffix` | siblings from a stable child order position onward, including their subtrees |
| `LayerAndLowerSuffix` | all nodes at the named visual layer and every lower visual layer in the same layout component |
| `ProjectRoot` | the project container and its project-owned visual elements for size/bounds constraints |
| `OrderedProjectSuffix` | projects from one stable root project order position onward |

Bands and conflict groups own demands but are not movement scopes. No production router consumes the new store yet; node sizing uses the same width formula directly during base placement. Diagnostic observation exercises merge/materialisation over real nodes.

## Explicit route invalidation

`RouteInvalidation` records route ID, cause, source route revision, source/target placement revisions and optional scope/rail. Causes are:

- `EndpointMoved`;
- `EndpointResized`;
- `TerminalAllocationChanged`;
- `CrossedBoundaryMoved`;
- `AssignedRailChanged`;
- `ObstacleRelationshipChanged`;
- `ProjectBoundsChanged`;
- `SharedTurnAllocationChanged`.

Direct node invalidation accepts only `EndpointMoved` or `EndpointResized`, requires a strictly later target placement revision, and returns every incident route in stable ID order. The semantic route reference remains. Stale points are discarded/regenerated by the future authority; translating them is not a solution. Unrelated routes and dependency-connected nodes are untouched.

Complete transitive closure is intentionally not implemented in this tranche.

## Defect-to-demand contract

| Defect | Provisional contract |
|---|---|
| `NodeCollision` | viable-side `ObstacleBypass` rail alternatives |
| `SharedSegment` | distinct parallel `Through` rail demands |
| `ReusedBend` | distinct `TurnTransition` demands |
| `SpacingDeficit` | increased rail or rectangle extent demand |
| `NonOrthogonalSegment` | reject topology and request orthogonal regeneration; not a spacing demand |
| `ImmediateReversal` | reject topology and request regeneration; not a spacing demand |

These contracts do not change current repair decisions.

## Conflict components and link independence

`ConflictComponentBuilder` is a deterministic union-find over stable item identities and caller-supplied conflict edges. It knows nothing about routes, rectangles, projects or bypasses. Component ID is the smallest stable member ID, member order is ordinal, causes are distinct/ordered, and reversed inputs produce the same result.

It is shared infrastructure, not a universal solver. Intended solvers remain:

1. interval/rail assignment;
2. ordered rectangle placement.

Both will emit generation constraints through the same protocol.

`TerminalInteractionEdges` represents the corrected terminal closure:

- before allocation, claims couple only within the same node, terminal side and attachment region;
- after allocation, they couple only if they retain the same actual attachment coordinate;
- incoming/top and outgoing/bottom claims on the same node are independent;
- semantic node identity alone creates no edge.

Further coupling comes from rail conflicts, assigned turns, obstacle bypass choices, overlapping movement scopes and affected project boundaries.

## Observed interaction closure

The current Release CLI regenerated all four configurations. `scripts/report-routing-consolidation-evidence.ps1` builds two closures without changing geometry:

- **terminal-unresolved**: same-side/region competition plus existing rail/contact/obstacle/movement relations;
- **terminal-resolved**: exact assigned terminal contact plus those same non-terminal relations.

The script no longer unions routes merely because they share a semantic node. Its committed output is `docs/evidence/consolidated-routing-foundation-evidence.csv`.

Deduplicated cCoder still resolves to one component. The transitive causes are not semantic-node unioning: final validator contact edges, broad Stage B rail-interval overlap, and deficient-band lower-scope closure connect the route set. The leading recorded causes are perpendicular contact, rail interval conflict, spacing contact, shared segment and reused bend. Some current relations are intentionally conservative; the future production closure needs canonical policy-filtered contacts and exact movement-scope membership before it can be a migration boundary.

### Four-graph closure evidence

| Graph | Unresolved components / largest / median / singletons | Resolved components / largest / median / singletons | Clean crossovers | Bend-involved contacts |
|---|---|---|---:|---:|
| StandardIo duplicated | 16 / 3 / 1 / 12 | 20 / 2 / 1 / 19 | 0 | 0 |
| StandardIo deduplicated | 5 / 8 / 1 / 4 | 7 / 6 / 1 / 6 | 5 | 3 |
| cCoder duplicated | 630 / 42 / 1 / 423 | 928 / 39 / 1 / 906 | 332 | 96 |
| cCoder deduplicated | 1 / 294 / 294 / 0 | 1 / 294 / 294 / 0 | 3,400 | 1,569 |

The deduplicated cCoder resolved component contains 180 nodes, three bands and one project. Its five leading recorded merge causes are `PerpendicularCrossing=2760`, `RailIntervalConflict=1232`, `ParallelSpacing=812`, `SharedSegment=777`, and `ReusedBend=31`. Resolved terminal-contact edges are zero, so the collapse is caused by non-terminal contact/rail relations and movement closure, not shared semantic endpoints or assigned terminal collisions.

### Node-width and performance evidence

| Graph | Already sufficient | Text | Incoming | Outgoing | Maximum increase | Incident invalidations |
|---|---:|---:|---:|---:|---:|---:|
| StandardIo duplicated | 0 | 22 | 0 | 0 | 0 | 0 |
| StandardIo deduplicated | 0 | 11 | 0 | 0 | 0 | 0 |
| cCoder duplicated | 427 | 667 | 0 | 0 | 0 | 0 |
| cCoder deduplicated | 21 | 155 | 4 | 0 | 416 | 81 |

Only deduplicated cCoder changed. The four corrected incoming-demand widths are `ICoreContextFactory` 200→548 (23 incoming), `AuthorizationBroker` 228→644 (27 incoming, one outgoing), `ICoreAuthInfo` 200→356 (15 incoming), and `IEventHub` 200→380 (16 incoming). StandardIo duplicated/deduplicated and cCoder duplicated remained byte-identical.

The five measured fresh-process normal cCoder deduplicated generations were 14,081–14,499 ms, median 14,297 ms, with one stable output hash. This is 118 ms (about 0.8%) above the accepted 14,179 ms median. Canonical all-segment contact classification remains diagnostic-only; it introduces no full route-pair scan into normal generation. Direct diagnostic foundation timings are retained per graph in the evidence CSV because microbenchmarks at this scale are environment-sensitive.

## One route-selection semantic

Every graph size uses the same comparison meaning:

1. all semantic links present and traceable;
2. hard geometry valid: orthogonal, node/obstacle clear, no ambiguous sharing/bend/endpoint, no reversal, spacing satisfied;
3. reasonable local detour rather than an absurd envelope expansion;
4. crossing count/advisory cost;
5. bend count;
6. route length;
7. stable route signature.

Mapping from current scores:

| Target term | Current evidence |
|---|---|
| hard validity/traceability | `InvalidGeometry`, validation findings, terminal compatibility |
| obstacle clearance | candidate obstacle rejection / `NodeCollision` |
| ambiguous sharing | `SharedSegmentLength`, `SpacingDeficit`, `AmbiguousTransitions`, terminal fan-out violations |
| reversal | normalizer/validator finding; must become a hard candidate rejection |
| local detour | `RouteEnvelopeExpansion`, path-length local cost |
| crossings | `CrossingsAndCongestion` and regional pair contributions |
| bends | candidate bend count |
| length | candidate path length |
| stable tie-break | `CorridorPathSignature` |

Large graphs may index, bound candidate production, cache pair contributions, partition by genuine components, score pure partitions in parallel and incrementally select. A declared advisory optimisation budget may reduce advisory quality. It must not permit a hard defect or alter what “better” means. Current global and regional implementations have not yet demonstrated this single contract, so neither changes here.

## Target authority and convergence flow

```text
1. Semantic/render graph
2. Immutable hierarchy and base placement
3. Text and terminal demand measurement
4. Node-size constraints and terminal allocation
5. Provisional topology and RailDemand production
6. Canonical contact/conflict discovery
7. Persistent X/Y/size constraint solving
8. Materialized placement revision
9. AssignedRail route generation
10. Invariant validation
11. Coordinate ownership and serialization
```

Steps 5–9 repeat only while a constraint increases or a route remains invalidated:

```text
detect -> express demand or reject topology -> raise constraints
       -> regenerate affected component -> validate
```

Completed waypoint nudging and a second unrelated repair algorithm are not part of the target authority.

## Specialised demand producers

These producers specialise topology and requirements only. All share contact classification, conflict components, rail assignment, persistent constraints, invalidation and validation.

| Producer | Semantic input / produced topology and demands | Permitted terminals / possible constraints | Hard rejection |
|---|---|---|---|
| ordinary adjacent downward | source/target in next layer; departure, one band through rail, arrival | source bottom, target top; band/lower-layer Y extent | obstacle collision, invalid terminal, no orthogonal transition |
| skipped-layer downward | source/target separated by layers; ordered through rails for every crossed band | bottom/top; each crossed band may raise lower-suffix Y | missing continuous rail sequence or invalid boundary transition |
| same-layer | same visual layer; side/below/above bypass sequence | compatible source/target sides; X/Y sibling/subtree extent | unavoidable node intersection or reversal |
| upward/return | target above source; exterior return side and band crossings | compatible sides; project/subtree X and band Y extent | no viable exterior side/topology |
| terminal fan-in/out | incident semantic links and remote ordering | allocated node side; node minimum width | insufficient width after monotonic sizing or incompatible terminal direction |
| obstacle bypass | route segment and obstacle rectangle | viable left/right/top/bottom rail; smallest coherent X/Y movement scope | no orthogonal viable side under current fixed scopes |
| canonical shared endpoint | multiple parents targeting one stable canonical placement | allocated independent terminal claims; node width and local rail extent | moving/recentring canonical node or branch-local-only approach |
| cross-project | semantic endpoints in different projects | project-owned terminal pieces plus root rails; project suffix/size | route topology cannot reconstruct across ownership boundaries |

## Exact deletion gates

No listed subsystem is deleted in this tranche.

| Existing subsystem | Measurable deletion gate |
|---|---|
| `SeparateOverlappingCorners` | all supported topology/turn producers emit distinct common turn demands; overlapping-corner fixtures pass with zero final diagonals and no cleanup invocation |
| corridor observation | every current corridor fixture maps losslessly to revisioned `RailDemand`/transition records; common discovery reproduces geometry and diagnostics |
| Stage B observation | band diagnostics project from the same common demands used by production, with matching memberships/extents on all four graphs |
| corridor lane allocation | all corridor lane fixtures compile through `AssignedRail` with identical spacing, deterministic IDs and XML geometry |
| Stage C lane assignment | grouped fixtures use the same `AssignedRail` solver as ordinary corridors and retain complete-group deterministic output |
| capacity requests | every current request becomes a persistent X/Y/size constraint and all capacity fixtures converge without legacy expansion |
| band missing extent | required band extent is derived from common rail components and persistent constraints with matching telemetry |
| regional optimiser | small/large fixtures demonstrate the same candidate comparison contract; partition/index/budget changes advisory quality only |
| global selector | common component selector reproduces accepted small fixtures and enforces hard validity independently of budget |
| `RouteRepairCoordinator` | zero expected final findings are repair-owned across focused fixtures and four real graphs; expected conflicts became demands; unexpected defects regenerate/fail |
| traversal fallback | every supported rail sequence has an orthogonal transition producer; unsupported topology rejects/regenerates rather than restoring malformed geometry |
| branch-specific normalization | both paths produce one route representation and identical canonicalization fixtures through a single boundary |
| branch-specific validation | one canonical contact/traceability validator owns all hard rules and both paths emit the same finding semantics |
| whole-graph `Supports` | every required topology producer emits common demands and closed components can be validated/committed under one authority |
| empty legacy compatibility models | consumers accept a branch-neutral route/rail/validation result and no diagnostic expects placeholder corridors/lanes/traversals |

## Current implementation boundary

Active production changes in this tranche are limited to node/terminal measurement and allocation, plus the parity adapter from Stage C contact policy to canonical contact facts. The whole-graph support gate, legacy/grouped authority selection, candidate construction, lanes, capacity, repair, fallback and final route selection are unchanged.

`RailDemand`, `AssignedRail`, `RailTransition`, persistent constraints, invalidation and defect contracts are currently exercised by focused tests and optional diagnostics only. They do not assign live rails or move production nodes. Canonical all-route contact classification is optional diagnostic work only. No VSIX is packaged.

## Canonical facts and consumer policy integration

Canonical geometry and consumer policy are separate authorities. `CanonicalRouteContactDiscovery` supplies route-pair facts using `CanonicalContactClassifier`; `TraceabilityValidator`, component evidence and advisory scoring decide independently what each fact means.

| Canonical fact | Final hard validation/component policy |
|---|---|
| `Disjoint` | no edge |
| `CleanPerpendicularCrossover` | permitted; advisory scoring input only; no component edge |
| `BendInvolvedPerpendicularContact` | hard ambiguous contact and component edge |
| `EndpointToInterior` | hard ambiguous contact and component edge unless a future explicit terminal/junction owner permits it |
| `NearParallelSpacingConflict` | edge where the routes compete in the same final allocation region |
| `PositiveCollinearOverlap` | hard shared geometry and component edge |
| `SharedBend` | hard shared turn and component edge |
| `StraightContinuation` | no edge without genuine shared traceability ownership |
| `EndpointToEndpoint` | no generic edge; requires terminal/junction classification first |
| `TerminalContact` | unresolved terminal competition or identical assigned coordinate only |
| `IntentionalSemanticJunction` | explicit junction ownership; never inferred as an ordinary conflict |

The legacy validator's `PerpendicularCrossing` predicate used strict-interior crossing and therefore reported clean crossovers. It now preserves that public code only for bend-involved or endpoint-to-interior ambiguity. Clean crossovers remain counted by candidate/junction advisory scoring and are not hard findings.

### Rail-conflict distinction

Overlapping occupied intervals of unassigned demands whose allowed axis ranges overlap compete for lane assignment and create an allocation edge. Assigned rails do not conflict merely because their projected occupied intervals overlap: their actual axes must coincide or violate configured separation, or they must share an explicit transition/movement consequence. Current Stage B evidence consists of unassigned band demands, so all observed interval edges remain legitimate provisional allocation edges; no current closure edge was an assigned-rail projection edge, and the measured assigned-rail removal count is zero.

### Revised four-graph closure

Each entry is `component count / largest routes / median routes / singleton routes`. “Legacy broad” reconstructs the old broad semantics by including clean crossovers. “Canonical factual” includes every observed non-disjoint fact. “Policy” applies the table above with unresolved terminal allocation. “Resolved” retains only identical assigned terminal coordinates.

| Graph | Legacy broad | Canonical factual | Policy | Resolved policy |
|---|---|---|---|---|
| StandardIo duplicated | 16/3/1/12 | 16/3/1/12 | 16/3/1/12 | 20/2/1/19 |
| StandardIo deduplicated | 5/8/1/4 | 5/8/1/4 | 5/8/1/4 | 7/6/1/6 |
| cCoder duplicated | 630/42/1/423 | 628/42/1/421 | 641/32/1/425 | 976/20/1/950 |
| cCoder deduplicated | 1/294/294/0 | 1/294/294/0 | 23/271/1/21 | 25/270/1/24 |

Deduplicated cCoder therefore no longer forms one policy-valid component. Its largest resolved component has 270 routes. Leading retained edges are 1,569 bend-involved contacts, 1,232 unassigned rail-demand conflicts, 895 spacing conflicts, 815 positive overlaps, 193 endpoint-to-interior contacts, 20 shared bends and nine obstacle-bypass relations. Two explicit deficient-band movement scopes connect 145 and 81 routes respectively. The broad one-component result depended on 3,400 clean crossovers. The compact reason/route map is `docs/evidence/ccoder-deduplicated-contact-component-map.csv`; full closure evidence is `docs/evidence/canonical-contact-policy-evidence.csv`.

There is no single policy-valid chain joining all 294 routes after filtering, so no all-graph minimal cut exists. Within the 270-route largest component, the leading transitive backbone is bend-involved contact → unassigned band demand → spacing/positive overlap, reinforced by the two deficient-band movement scopes. Those movement-scope hyperedges are the smallest authority-level cuts joining their respective lower-layer regions; route-by-route cuts would misrepresent their coherent movement ownership.

### Consumer inventory after integration

| Consumer | Current factual geometry status | Policy status |
|---|---|---|
| `TraceabilityValidator` | canonical route-contact discovery directly | explicit hard-finding projection; clean crossings permitted |
| diagnostic closure | canonical diagnostic facts directly | explicit unresolved/resolved component projection |
| `BandConflictGrouper` | canonical classifier through parity adapter | Stage C compatibility policy retained |
| global selector/candidate scoring | duplicates overlap, spacing and strict-interior crossing calculations | advisory lexicographic scoring remains consumer-specific |
| regional interaction discovery | consumes global score summaries | interaction-reason policy remains regional |
| junction metrics | duplicates overlap, spacing and strict-interior crossing calculations | metrics/advisory policy remains specialised |
| obstacle/crossing helpers | rectangle/route predicates, not general route-contact classification | obstacle rejection remains specialised |

Global/regional scoring and junction metrics are not migrated in this tranche because their route representations omit terminal/bend context required for exact canonical parity. Their strict-interior crossing calculation already represents the permitted advisory clean-crossover cost, but their duplicated factual helpers remain a future consolidation target.

## Observational adjacent-downward common demand integration

`AdjacentDownwardRailDemandObserver` is the first route-local common-demand producer. It has diagnostic and test consumers only; no production route, selector, lane, placement, repair, fallback or XML consumer reads its result. For an ordinary orthogonal route from depth `d` to `d + 1`, using bottom source and top target terminals and exactly one distinct inter-layer band, it emits:

```text
assigned source terminal
-> TerminalDeparture vertical demand
-> departure-to-through transition
-> Through horizontal demand in the crossed band
-> through-to-arrival transition
-> TerminalArrival vertical demand
-> assigned target terminal
```

The observer preserves existing terminal coordinates and the authoritative through coordinate. Each demand carries deterministic route/demand identity, orientation, occupied and allowed intervals, preferred axis, semantic role, terminal/lane order, placement and route revisions, and movement scope. Reconstruction receives only terminals, selected `AssignedRail` records and ordered `RailTransition` records. It has no original-point fallback.

Eligibility is route-local. A rejected route does not suppress an independent eligible route. Explicit reasons are same layer, skipped layer, upward/return, cross-project, duplicated exposure-tree-specific, non-orthogonal, multiple distinct bands, unsupported terminal topology and revision mismatch. Multiple membership records for the same band remain eligible; observation revision is the band-generation revision and is independent of a logical route state's local history counter.

### Existing assignment adapters

The common `AssignedRail` fields are rail ID, logical route/demand IDs, orientation, actual axis, lane index, occupied interval, role, placement revision and route revision.

| Existing source | Specialised metadata |
|---|---|
| legacy corridor lane | `corridorId`, `role`, `regionKey`, `obstacleBoundaryKey` |
| Stage B hypothetical lane | `bandId`, `direction`, `membershipRole` |
| Stage C grouped lane | `groupId`, `movementScope`, `currentExtent`, `requiredExtent` |

Adapter priority is deterministic: legacy corridor, then Stage B, then Stage C. This observes an existing result rather than inventing another allocator. Focused tests prove all three adapters map to the same common fields. Current real graphs exercise legacy and Stage B mappings; no eligible real route carried an active Stage C grouped assignment in this run.

### Four-graph parity and component evidence

| Graph | Eligible / rejected | Rejection summary | Demands D/T/A | Exact / topology-only / unable | Unassigned / assigned components | Largest assigned | Removed edges / involved routes |
|---|---:|---|---:|---:|---:|---:|---:|
| StandardIo duplicated | 0 / 21 | exposure tree 21 | 0/0/0 | 0/0/0 | 0/0 | 0 | 0/0 |
| StandardIo deduplicated | 7 / 5 | skipped 4; multiple band 1 | 7/7/7 | 7/0/0 | 7/7 | 1 | 0/0 |
| cCoder duplicated | 0 / 1,072 | exposure tree 1,072 | 0/0/0 | 0/0/0 | 0/0 | 0 | 0/0 |
| cCoder deduplicated | 45 / 249 | same 20; skipped 25; upward 40; non-orthogonal 27; multiple band 112; terminal topology 25 | 45/45/45 | 45/0/0 | 29/42 | 3 | 27/21 |

In the deduplicated cCoder adjacent-downward family, representing the existing distinct lanes removes 27 provisional competition edges involving 21 routes. This identifies the lane-assignment-attributable portion inside the previously reported 270-route resolved-policy region: 21 routes (7.8% of 270) participate in those disappearing relations. It does not claim that removing those edges alone dissolves the 270-route transitive component. Two assigned-rail conflicts and one shared-turn transition conflict remain in this family, and the larger component also contains bend, spacing, overlap, obstacle and movement-scope relations.

Detailed evidence is in `docs/evidence/adjacent-downward-rail-observation.csv`; route-level demands, assignments, transitions, authoritative points and reconstructed points are retained in `artifacts/adjacent-downward-observation/*.json`. Diagnostic and normal cCoder deduplicated Draw.io output have identical SHA-256 `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.

Observed diagnostic costs for deduplicated cCoder were 5.839 ms demand production, 6.258 ms existing-lane adaptation, 2.215 ms reconstruction, 0.267 ms parity comparison and 10.659 ms component projection. The controlled normal benchmark used one excluded warm-up and five fresh Release processes: minimum 14,692 ms, median 14,911 ms, maximum 15,005 ms, with one repeated output hash. This sits alongside the 14.30-second foundation and 14.18-second earlier baselines; the observer is not invoked in normal generation.

### Gates before common rail authority

Common rail assignment remains observational until skipped-layer, same-layer, return, cross-project, exposure-tree and other rejected topologies have explicit producers; specialised metadata has a branch-neutral owner or deliberate extension boundary; common assignment reproduces current spacing and deterministic geometry; revisioned constraint/invalidation cycles converge; component-local mixed support is validated; and full traceability, byte parity and performance gates pass under production use. No graph-wide `Supports` gate is introduced by this tranche.

## Shared deterministic rail assignment consolidation

`DeterministicRailAllocator` is now the shared interval-assignment algorithm for Stage B inter-layer bands and Stage C grouped bands. It was extracted from Stage C's `BandConflictGrouper`, which already supplied deterministic interval ordering, transitive component construction, unbounded greedy interval colouring and required-extent calculation. Stage B's embedded active/free-lane sweep was removed. A legacy-corridor wrapper was trialled and proved XML-equivalent, but it imposed a repeatable normal-generation cost because repair repeatedly represents each corridor as a complete full-envelope conflict graph. That production redirection was removed to preserve the explicit observational-authority boundary.

The shared flow is:

```text
stable RailDemand records
-> ordered interval sweep
-> complete transitive components
-> deterministic lowest-available lane
-> AssignedRail records
-> component and region required extent
-> optional persistent constraint proposal
```

Positive overlap conflicts. A gap smaller than configured separation conflicts. Endpoint-only contact is independent when separation is zero unless a wrapper explicitly selects the historical Stage C endpoint-component policy; positive configured separation separates it. Perpendicular contacts do not enter same-orientation colouring. Stable interval start/end, terminal order, logical route ID, turn order and demand ID determine ordering. There is no lane, component or graph-size limit.

### Allocation-region identity and wrappers

The branch-neutral identity consists of orientation, permitted axis range, geometric envelope identity, movement-scope owner and placement revision. It deliberately contains no route-family label.

| Wrapper | Region mapping | Remaining specialised responsibility |
|---|---|---|
| legacy corridor | observational adjacent routes map corridor orientation, cross-axis bounds, corridor ID and current placement | production `CorridorLaneAllocator` remains the one duplicate allocator: its capacity-first, all-routes-distinct, centred-coordinate contract is not yet a lossless interval-demand wrapper at acceptable repair-loop cost |
| adjacent inter-layer band / Stage B | horizontal orientation, band bounds/ID, `LayerAndLowerSuffix`, band layout revision | return/downward role separation and return-region extent composition |
| Stage C grouped band | same band geometry and movement scope | grouped constraint metadata and historical endpoint-component grouping |
| terminal side | not activated | terminal ordering/width allocation remains separate |
| return region | only Stage B's existing proven role wrapper | exterior-side topology remains specialised |
| obstacle bypass | not activated | no common region mapping has fixture evidence yet |

No normal route consumes a newly calculated common coordinate. Existing Stage B, Stage C and legacy callers use the extracted algorithm under parity-preserving wrappers. The adjacent dual run remains diagnostic and reconstructs from the common allocator's own through rail plus terminal coordinates and new transitions.

### Four-graph assignment parity

| Graph | Regions | Components | Demands | Largest | Existing parity | Reconstruction | Failures / hard regressions |
|---|---:|---:|---:|---:|---|---|---:|
| StandardIo duplicated | 0 | 0 | 0 | 0 | n/a | n/a | 0/0 |
| StandardIo deduplicated | 5 | 7 | 7 | 1 | 7 legacy and 7 Stage B equivalent/different coordinate | 7 valid/different | 0/0 |
| cCoder duplicated | 0 | 0 | 0 | 0 | n/a | n/a | 0/0 |
| cCoder deduplicated | 7 | 39 | 45 | 4 | 45 legacy and 39 Stage B equivalent/different coordinate | 45 valid/different | 0/0 |

No real eligible route required extra extent, so no live or observational constraint was needed for these graphs. The common formula is `2 * padding + laneCount * separation`; missing extent is `max(0, required - available)`. When positive, adjacent vertical expansion proposes `MinimumHeight` for the `LayerAndLowerSuffix` owner. `GenerationConstraintStore` retains the maximum by key, rejects later reductions and materialises from immutable base placement; existing foundation tests protect those iteration semantics. Stage C's wrapper has exact lane/extent parity in focused fixtures. Legacy and Stage B real-route coordinate differences arise because the observational common region uses a band-origin coordinate, while production wrappers retain corridor-centred or authoritative existing coordinates.

Detailed evidence is `docs/evidence/common-rail-assignment-evidence.csv`. All four production Draw.io hashes match the preceding accepted tranche. Deduplicated cCoder remains `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.

## Development-only full-diagram common-authority trial

The next tranche adds an explicit CLI-only authority trial. It is not represented in `DiagramSettings`, is not exposed by the Visual Studio extension, and is never consulted by ordinary generation. `--development-common-authority-trial <directory>` starts with the completed production layout, observes adjacent-downward routes, assigns common rails and deterministic shared turns, classifies closed components, attempts only wholly supported components, validates each candidate against the complete mixed route set, rolls back failed components, and serializes the accepted mixed result separately.

Authority closure uses final rail/contact conflicts and terminal-side competition. Positive overlap, near spacing, endpoint-to-interior, shared bend and bend-involved perpendicular contact couple routes; clean perpendicular crossovers remain advisory. A component containing both eligible and unsupported routes is `MixedBoundaryUnsafe` and is rejected whole. A candidate that introduces a hard common/common or common/legacy finding is rejected after validation. Common candidates do not enter `RouteRepairCoordinator`, `SeparateOverlappingCorners`, traversal fallback, legacy capacity expansion, or post-generation nudging.

`DeterministicSharedTurnAllocator` constructs the two orthogonal transitions from the assigned departure, through and arrival rails in stable route-id order. It rejects incomplete, revision-incompatible, coincident or reused turn allocations. `LayerSuffixConstraintMaterializer` is the persistent placement foundation: it converts missing common extent to an absolute `MinimumY` for `LayerAndLowerSuffix`, merges constraints monotonically, rematerializes from immutable base placement, shifts only the lower suffix by the exact delta, preserves X coordinates, and invalidates incident and crossed-boundary routes rather than translating stale points.

### cCoder result and production blocker

The full deduplicated cCoder trial is in `docs/evidence/common-authority-full-trial`. Production `before.drawio` retains SHA-256 `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`; accepted mixed `after.drawio` is `88C539710C2387BF5ECD54C0DF6AEDC1712CA63AA9816356C27F06A7CC8E5DF1`.

Forty-five routes are route-locally eligible. Closure yields two wholly eligible components: one one-route component is accepted, while the other is rolled back after its candidate creates a shared segment with an untouched legacy route. One large mixed component is rejected before execution, as are 26 wholly unsupported components. The accepted route removes one immediate reversal, changes length from 516px to 536px, reduces bends from three to two, and introduces no hard internal or boundary finding. The remaining full-diagram hard defects stay in legacy-owned geometry.

The required real deficient-band proof cannot be truthfully produced for this bounded family. All seven eligible common regions have zero missing extent; the largest common requirement is 56px in 140px available. The two real whole-band deficits (132px at depth 0→1 and 168px at depth 4→5) are caused by sets containing unsupported families. Moving either as an eligible independent common component would violate movement closure and the explicit prohibition on expanding route-family support. Consequently the persistent constraint mechanism is proven by focused deterministic tests, but there are no `deficient-band-before.drawio` or `deficient-band-after.drawio` artefacts for cCoder.

Before production authority can be considered, the next integration must close the unsupported routes that interact with the adjacent-downward rails or add the next route family deliberately, then repeat this trial. The review gate is not the number of route-local eligible routes; it is the number of wholly supported, movement-closed interaction components that survive full mixed validation.

## Mixed-boundary attribution and general downward integration

The follow-up attribution separates semantic topology from current geometry. Deduplicated cCoder contains 189 semantically adjacent-downward routes, 31 skipped/multi-band downward routes, 32 same-layer routes and 42 upward/return routes. Secondary reasons—including 123 multi-band current traversals, 27 non-orthogonal routes and 25 unsupported terminal topologies—are recorded independently and do not replace the semantic family.

Observational unlock simulation ranks a general one-or-more-band downward capability first: it newly represents 150 routes, yields 25 fully supported components and unlocks 26 adjacent routes. Terminal-topology support yields five components; same-layer, upward/return and diagonal-only regeneration each yield two. The evidence is `docs/evidence/multi-band-downward-integration/trial-report.json`.

`DownwardRailDemandFactory` is now the single demand topology for ordinary downward routes. The accepted adjacent observer calls it with one band; `GeneralDownwardRailDemandProducer` calls it with every semantic crossed band in upper-to-lower order. Each band receives an independent `Through` demand and uses `DeterministicRailAllocator`. Terminal departure and arrival demands remain vertical. Consecutive assigned horizontal rails are connected by deterministic vertical transitions at monotonic interpolated X coordinates. Current diagonal waypoints are not accepted as topology input. An intermediate vertical run intersecting a non-terminal node returns `ObstacleBypassRequired` and rejects the route/component; there is no diagonal cleanup or waypoint nudge.

Each common region calculates its own extent and, when deficient, proposes an absolute `MinimumY` for the correct `LayerAndLowerSuffix`. Proposals use the current lower-layer origin plus exact missing extent. Immutable-base materialisation, monotonic retention, upper-to-lower application and incident/crossed-band invalidation remain shared with the prior constraint foundation. A rejected hypothetical movement closure does not suppress unrelated no-move components, but every route whose rail requires that movement is blocked from authority.

The repeated cCoder trial supports 201 routes locally and identifies 28 fully supported route components. Twenty-six components containing 27 routes validate and are accepted, compared with one component/route previously. One ordinary mixed component and one mixed movement component are rejected before execution; one candidate is rolled back after validation. Accepted geometry uses no repair coordinator, corner separation, traversal fallback or legacy capacity expansion.

The two original deficient bands remain unsafe. Band 0→1 is 140/272/132px and contains 49 adjacent, 11 multi-band, three same-layer and one upward route. Band 4→5 is 140/308/168px and contains 24 adjacent, nine multi-band, 16 same-layer and 23 upward routes. A new depth-2 common proposal requires 132px, but would move five layers/120 nodes and invalidate 249 routes, including unsupported families. All three movement opportunities are therefore rejected without fabricated artefacts.

For the 27 accepted routes, total length changes from 4,954px to 5,286px and bends from 58 to six. Maximum route increase is 116px, maximum envelope expansion is 58px and no route is flagged unreasonable. The only full hard-count improvement is one removed immediate reversal. The accepted common/legacy boundary contains 287 clean crossovers and no hard finding.

The next production-authority blocker is movement closure through same-layer and upward/return routes, plus the remaining post-validation obstacle/contact candidate. Those families must be evaluated in a separate measured tranche; they are not added here.

The final authority-safe normal benchmark used one excluded warm-up and five fresh Release processes: minimum 14,619 ms, median 14,723 ms and maximum 14,863 ms, with one repeated output hash. The attempted legacy production wrapper measured 16,387 ms median before lookup correction and 15,663 ms after a complete-overlap optimization, so it was removed. The final 14,723 ms result returns to the preceding 14.63-14.78 second verification range and improves on the immediately preceding 14,911 ms observational-tranche sample. One instrumented phase sample measured 590 ms workspace acquisition, 1,756 ms semantic analysis, 17,527 ms layout/routing including 4,503 ms repair, 13 ms ownership compilation, 257 ms XML serialization and 322 ms file writing; telemetry itself raises that run to 20.58 seconds and is not a normal benchmark.

### Three retained existing-assignment conflicts

1. `AppController -> AppOrchestrationService` and `AppManager -> AppOrchestrationService` reuse `(1451,200)`. This is a transition-allocation gap: separate existing corridor results place both through rails at Y=200 and transition ownership is outside those local solvers.
2. `AppController -> AppOrchestrationService` spans X=1451..12432 at Y=200 and contains `MetadataCache -> IMetadataTypeCache` X=4384..4396 at Y=200. Their legacy lane indices differ but independent corridor identities project them to the same axis. This is an adapter/allocation-region deficiency, not insufficient band extent.
3. `PageRenderCoordinationService -> PageInfoOrchestrationService` spans X=19857..20540 at Y=200 while `PageController -> ILogger` spans X=19352..19923 at Y=206. Six pixels violates the configured 12-pixel separation. This is another independently allocated corridor-region deficiency.

The common band region places interacting intervals together, so these are not intrinsic semantic junctions or malformed routes.

### Real-component visual proof

The deterministic development fixture uses the first two retained relations as one real three-route component. It preserves the real node dimensions, X/Y placement, terminal coordinates, route spans and 12-pixel spacing. The before file is `docs/evidence/common-rail-real-component/before.drawio`; the after file is `docs/evidence/common-rail-real-component/after.drawio`.

| Metric | Before | After |
|---|---:|---:|
| shared segment | 1 | 0 |
| parallel-spacing defect | 1 | 0 |
| reused bend | 1 | 0 |
| node collision | 0 | 0 |
| immediate reversal | 0 | 0 |
| non-orthogonal segment | 0 | 0 |
| bend-involved perpendicular contact | 0 | 0 |
| endpoint-to-interior contact | 0 | 0 |

The fixture assigns three rails, creates six turns, regenerates three routes and records three `AssignedRailChanged` invalidations. It moves zero nodes and zero layers and adds zero extent because the existing 140-pixel band already contains the required two lanes. `RouteRepairCoordinator`, `SeparateOverlappingCorners` and traversal fallback are all explicitly absent. Initial semantics retain all three dependencies; the after topology is fully orthogonal and deterministic.

### Remaining authority gates

Normal common-coordinate authority still requires production mappings for terminal, return and obstacle-bypass regions; shared transition/junction allocation; exact or explicitly accepted coordinate migration; persistent-constraint convergence on real positive-missing-extent components; mixed supported/unsupported component handling; and full hard-validity, visual and performance acceptance. The development fixture does not grant normal production authority.

### Positional alternative closure

Development authority now retains hierarchy-preserving destination, blocker, sibling-prefix/suffix and
project-prefix/suffix choices until the complete connected positional component is analysed. Selected inequalities form
a directed graph; strongly connected components containing positive weight are rejected without coordinate iteration.
Search branches only on alternative-bearing conflicts contributing to positive SCCs and preserves deterministic local
choices elsewhere.

The deduplicated cCoder component exposes 16 multiple-choice conflicts and 131 alternative edges. Cycle-focused search
evaluates and rejects 52 selections and finds no complete acyclic solution. The outcome is an atomic pre-execution
rollback: no node placement or regenerated path escapes. Return slot allocation remains independently complete at 74/74.
Normal production remains outside this analysis and retains its accepted byte-identical output.

### Corrected node-width diagnostics and performance

Diagnostics now report every winning requirement (`Current`, `Text`, `Incoming`, `Outgoing`), whether the node actually changed from the preceding production formula, and a single or multiple resize cause. Current inputs still resize only `ICoreContextFactory`, `AuthorizationBroker`, `ICoreAuthInfo` and `IEventHub`, all in deduplicated cCoder and all because incoming demand wins. Counts remain: StandardIo duplicated 22 text; StandardIo deduplicated 11 text; cCoder duplicated 427 current and 667 text; cCoder deduplicated 21 current, 155 text and four incoming. No ties occur.

The indexed diagnostic contact sweep takes 9.8–33.9 ms across the four graphs; policy projection takes 0.1–1.2 ms. The four-graph PowerShell evidence projection takes approximately 9.2 seconds. Across every validation and repair trial in one instrumented cCoder deduplicated generation, validator contact discovery plus finding projection consumed 3.04 seconds. Normal cCoder deduplicated generation retained the accepted output hash; the first five-run median was 14,629 ms and a subsequent three-run verification median was 14,775 ms versus the 14,297 ms foundation median. Validator timing uses one enclosing timer per validation only when a performance session is requested, so normal generation does not pay per-pair stopwatch overhead.
