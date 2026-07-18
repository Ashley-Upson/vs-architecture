# Consolidated routing authority design

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

### Corrected node-width diagnostics and performance

Diagnostics now report every winning requirement (`Current`, `Text`, `Incoming`, `Outgoing`), whether the node actually changed from the preceding production formula, and a single or multiple resize cause. Current inputs still resize only `ICoreContextFactory`, `AuthorizationBroker`, `ICoreAuthInfo` and `IEventHub`, all in deduplicated cCoder and all because incoming demand wins. Counts remain: StandardIo duplicated 22 text; StandardIo deduplicated 11 text; cCoder duplicated 427 current and 667 text; cCoder deduplicated 21 current, 155 text and four incoming. No ties occur.

The indexed diagnostic contact sweep takes 9.8–33.9 ms across the four graphs; policy projection takes 0.1–1.2 ms. The four-graph PowerShell evidence projection takes approximately 9.2 seconds. Across every validation and repair trial in one instrumented cCoder deduplicated generation, validator contact discovery plus finding projection consumed 3.04 seconds. Normal cCoder deduplicated generation retained the accepted output hash; the first five-run median was 14,629 ms and a subsequent three-run verification median was 14,775 ms versus the 14,297 ms foundation median. Validator timing uses one enclosing timer per validation only when a performance session is requested, so normal generation does not pay per-pair stopwatch overhead.
