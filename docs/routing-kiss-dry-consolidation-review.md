# Routing KISS/DRY consolidation review

Date: 2026-07-18. Baseline: `fadf66b42671d22689b36fbc24f0f0ec240a1fdb` on `feature/decuplicate-node-option`.

This review is observational. It does not change geometry, placement, support, fallback, performance, or packaging. Its purpose is to separate genuinely different geometry from multiple implementations of the same geometry and identify viable consolidation choices. It deliberately stops short of an implementation plan.

## Executive finding

Most of the subsystem solves four recurring problems:

1. place rectangles without overlap using monotonic X/Y displacement;
2. place axis-aligned route intervals on spaced rails;
3. construct an orthogonal route through terminals, rails, turns and obstacle bypasses;
4. invalidate and regenerate the interaction closure after placement or rail assignments change.

The current distinction between corridors, bands, terminal fan-out, junctions, regional interactions and repairs is often metadata or scale rather than different geometry. A common `RailDemand`/`AssignedRail`, one deterministic conflict-component builder, and one generation-long monotonic constraint store are suitable for most of it. Specialisation is still required in **demand production**: terminal semantics, obstacle-side choice, skipped/upward route topology, canonical shared-node endpoints and cross-project ownership are not interchangeable. They need not own separate grouping, spacing, selection, or repair algorithms.

The strongest caution is interaction closure. With conservative current relations, StandardIo duplicated, StandardIo deduplicated and cCoder deduplicated each become one component. cCoder duplicated yields 22 components, largest 271 of 1,072 routes. Component-level migration is therefore not automatically finer than whole-graph fallback; closure relations and movement scopes need semantic precision.

## 1. Production operation inventory

The rows are grouped by geometric behaviour, not class name.

| Operation | Input -> output | Role/scope | Geometric problem and intended invariant | Substantially duplicated by / why separate today |
|---|---|---|---|---|
| hierarchy analysis | render graph -> parent/depth/component order | observes; tree/graph | establish stable layout ownership and ordering | exposure/canonical construction also derives graph roles; separate because semantic topology precedes geometry |
| base node placement | hierarchy, node sizes -> `NodeLayout` rectangles | mutates; subtree/project/graph | rectangle packing with sibling/subtree X and layer Y spacing; no node overlap | later layer/capacity/grouped movement; base pass assumes routes do not yet demand space |
| project placement | owned rectangles -> `ProjectLayout` bounds/origins | mutates; project/graph | contain owned elements and order projects | ownership bounds compiler/rebase; separate because final routes and anchors do not yet exist |
| provisional route construction (`PositionLinks`/`BuildRoute`) | nodes, links, settings -> candidate `Point[]` | regenerates; route/graph | orthogonal terminal-to-terminal construction, obstacle avoidance, candidate choice | grouped regeneration, repair candidate generation, canonical builder; accumulated for route families and later fixes |
| terminal fan-out grouping/allocation | incident routes, terminal side -> ports/transitions | decides/mutates; terminal group | monotonic ports and minimum lane spacing | corridor terminal transitions and grouped endpoint reconstruction; separate due semantic terminal order |
| canonical candidate construction | placed canonical target and source branch -> candidate routes | regenerates; route | bypass branch-local assumptions while retaining first canonical placement | ordinary candidate builder; separate after canonical-node defects exposed missing candidates |
| corridor discovery | provisional axis segments -> corridors, junctions, mappings, usage | observes; segment/route/graph | identify reusable rails and turns | Stage B band observation; corridor model is general/local while bands are layer-specific |
| corridor lane allocation | corridor usage -> lane indices/capacity requests | decides; corridor group | separate overlapping interval occupancy | Stage B hypothetical lanes, Stage C assigned lanes, terminal lanes; independently introduced at different phases |
| corridor geometry compilation | routes + lanes -> shifted points | mutates; route/corridor | materialise rail coordinates while retaining topology | grouped regeneration and repair-time geometry; legacy compilation preserves prior route topology |
| traversal compilation | corridor segments/junctions -> ordered traversals/transitions | regenerates; route/junction | make adjacent rail assignments connect coherently | simple junction allocation and corner separation; introduced when lane shifts required explicit turns |
| simple junction allocation | junction traversals -> distinct bend coordinates | decides; junction group | prevent shared/ambiguous turns | corner separation and grouped bend classification; bounded supported topology forced its own allocator |
| corner separation | route bends + used-corner set -> displaced bend | mutates; point/route | avoid exact reused corners | junction allocation and future rail turns; legacy post-hoc heuristic predates coherent turn allocation |
| obstacle checking/bypass | candidates + node rectangles -> accept/reject/alternative | observes/regenerates; segment/route | no segment through a non-terminal rectangle | repair node-collision trials and canonical exterior routes; repeated because each candidate producer validates locally |
| legacy capacity expansion | capacity requests + layers -> moved lower nodes + reroutes | mutates/regenerates; layer/graph | create Y extent for corridor lanes | grouped band missing extent and base layer spacing; retained as legacy compilation feedback |
| lane-demand layer expansion | validation + routes -> moved layers | mutates/regenerates; layer/graph | create spacing after compiled conflicts | capacity expansion/grouped constraints; driven by findings rather than rail demand |
| Stage B band observation | placement + routes -> memberships/demands/missing extent | observes; band/graph | project route occupancy into inter-layer rail demand | corridor observation and validation scans; added as diagnostic, later reused as Stage C input |
| band conflict grouping | horizontal demands -> connected components/lane assignment | decides; band/group | interval conflict colouring and required extent | corridor lane allocator/regional groups; new complete-group model |
| grouped constraint calculation | group extents -> minimum Y proposals | decides; layer suffix | monotonic lower-layer movement | legacy capacity/lane expansion; Stage C alternative authority |
| grouped regeneration | revised placement + assigned lanes -> new routes | regenerates; group/graph | reconstruct route from semantic endpoints and rails | provisional builder and repair; narrow adjacent-downward topology only |
| local candidate reduction | route candidates -> viable bounded alternatives | decides; route | remove dominated/invalid candidates | global and regional selection; local step owns route-local costs |
| global route selection | all candidate sets -> selected set | decides; graph | lexicographic traceability/congestion optimisation | regional optimiser; bounded exhaustive/iterative strategy for smaller interaction space |
| regional optimisation | route interactions + candidates -> component decisions | decides; region | same global scoring within bounded interaction regions | global selector; introduced for scale/exposure locality, not a different output invariant |
| repair coordination | validated routes -> trial candidates/accepted replacements | mutates/regenerates; route pair/region/graph | correct node collision, sharing, spacing and reversal under budget | demand generation, selectors and grouped regeneration; exists because earlier phases may emit defects |
| normalization | `Point[]` -> canonical `Point[]` | mutates; route | remove duplicates/collinearity/reversals where safely reducible | construction helpers each simplify too; final canonical boundary remains valid |
| traceability validation | nodes/routes -> findings | observes; segment pairs/route/node/graph | assert orthogonality, obstacles, sharing, spacing, bends, reversals and traceability | Stage B correlations and candidate checks duplicate some predicates; validator is final authority only for detection |
| ownership compilation | logical route -> physical segments/anchors | reconstructs representation; route/project/root | preserve exact absolute route while assigning movement ownership | not a routing duplicate; genuine serialization concern |
| ownership bounds/rebase | owned vertices/segments -> expanded containers/relative coordinates | mutates representation; project | container movement coherence and containment | initial project placement; necessarily repeated after routes exist |

Separate implementations are justified when inputs or semantics differ—semantic hierarchy versus physical ownership, terminal identity versus ordinary rail occupancy, or logical routing versus Draw.io parentage. They are not justified solely because the item count is larger or a route has a different label.

## 2. Geometric-problem taxonomy

| Current label | Fundamental categories | Smallest resolving operation |
|---|---|---|
| overlapping collinear route segments | interval conflict; rail allocation | create distinct rail demands and colour the interval component |
| close parallel segments | interval conflict; rail allocation | same as overlap, with spacing-inflated intervals |
| shared route endpoints | point/contact conflict; transition allocation | classify semantic terminal contact, then allocate ordered terminal rails |
| shared bends | point/contact conflict; rail allocation | allocate distinct turn/transition coordinates before generation |
| terminal fan-in/out | rail allocation; ordering constraint | terminal demand producer plus ordinary interval/lane solver |
| route/node intersection | rectangle conflict; obstacle bypass; movement-scope choice | produce left/right/top/bottom bypass rail alternatives; choose viable minimum demand |
| node/node spacing | rectangle conflict; minimum X/Y | monotonic constraint between movement scopes |
| subtree/subtree spacing | rectangle conflict; minimum X | same rectangle constraint with subtree/sibling-suffix scope |
| layer/layer spacing | rail capacity; minimum Y | maximum required band extent -> lower-layer-suffix constraint |
| project/project spacing | rectangle conflict; minimum X/Y | ordered project-suffix constraint |
| skipped-layer routing | route reconstruction; multi-band rail sequence | demand producer traversing multiple bands; shared rail solver afterward |
| upward/return routing | route reconstruction; obstacle bypass; external rail | choose return-side rail sequence and issue capacity demands |
| cross-project routing | route reconstruction; ownership metadata | root/inter-project rails plus later ownership segmentation |
| canonical shared-node routing | route reconstruction; movement scope | route to stable canonical endpoint; include all semantic endpoint interactions |
| perpendicular route crossing | point/contact classification | retain strict-interior clean crossover with cost; otherwise conflict demand |
| malformed diagonal | invalid geometry; route reconstruction | reject topology and regenerate orthogonal rail sequence |
| immediate reversal | invalid geometry; route reconstruction | reject candidate/route; regenerate, not assign another lane to the reversal |

Route-family names mainly select **which rail demands and topology are produced**. Once demands are axis-aligned intervals with ownership and ordering, conflict grouping, spacing and materialisation should be shared.

## 3. Differences that are only scale

| Current pair | Same invariant/input? | Consolidation assessment | Evidence/semantic risk |
|---|---|---|---|
| pairwise route repair vs complete interval groups | same goal: eliminate overlap/spacing ambiguity; repair starts from final defects, group starts from demands | one conflict graph with indexed discovery and batched assignment can serve both; defects should become demands before materialisation | replacing post-hoc repair changes tie-breaking unless scoring/order is explicitly preserved |
| local corridor capacity vs band extent | both count simultaneous rail occupancy and request axis extent; corridor supports both orientations/roles, band is a scoped projection | common rail-group required extent plus movement-scope policy | terminal/junction metadata must not be erased |
| regional interactions vs band conflict groups | both are connected components over route relations | one deterministic component builder with pluggable conflict relations | regional scoring includes crossings/candidate coupling beyond interval overlap |
| node vs subtree vs project displacement | all apply monotonic X/Y translations; movement closure differs | one constraint solver, specialised scope membership/dependency graph | moving a node alone can violate hierarchy; scope is semantic, not just rectangle size |
| global selector vs regional optimiser | same lexicographic traceability score and candidate semantics | one selection algorithm over independent components; index/partition/batch by size | current bounded strategies may produce different choices, so merge must define one deterministic semantic result |
| small vs large repair budgets | same findings and candidates, different work limits | performance policy should stop/defer work, not select different geometry semantics silently | current output can differ by graph size; no geometric justification |
| candidate obstacle checking in construction vs repair | same segment/rectangle predicate | one revision-keyed obstacle index and one predicate | no invariant difference |
| validator route-pair scan vs interaction discovery | largely same contact predicates | one canonical contact engine can feed both selection and validation | validation must remain exhaustive/hard even if optimisation prunes candidates |

The measurable justification for regional optimisation was combinatorial candidate search on cCoder, not a different visibility contract. The correct direction is one scoring semantics plus spatial indexing, connected-component partitioning, bounded candidate sets and deterministic pure parallel analysis. If a budget prevents completion, the result should say so; graph size should not redefine correctness.

## 4. Lane and rail concepts

| Representation | Axis/extent/owner/lifetime | Authority, spacing/contact | Consumer/final XML |
|---|---|---|---|
| `RoutingCorridor` | H/V axis, bounded interval/envelope; graph observation; legacy generation | observed shared rail; role-specific identity | allocator, traversal and diagnostics; reaches XML after compilation |
| `AllocatedCorridorLane` | offset/index within corridor; per compilation | legacy authoritative lane, configured parallel spacing | geometry compiler; yes |
| `BandRouteDemand` | horizontal interval in an inter-layer band; route/band revision | observational hypothetical lane; inclusive overlap | Stage B report/Stage C grouper; only reaches XML on grouped path |
| `BandConflictGroup.AssignedLanes` | horizontal band lanes for a complete interval component | Stage C authoritative on supported graph | grouped regeneration; yes only there |
| `TerminalFanoutMembership/Group` | source/target side, port order and overlapping horizontal/vertical departure intervals | provisional but route-construction authoritative; endpoint contact is semantic | `BuildRoute`; yes through chosen route |
| `TerminalTransition` | short rail from terminal to corridor boundary | legacy observed topology | corridor/traversal compiler; yes |
| return regions/lanes | exterior side plus long interval in a band | Stage B observational; legacy route already owns actual rail | diagnostics/gating; grouped rejects these |
| `CorridorTraversal` | ordered occupancy of a corridor lane | legacy authority bridge | `EdgeTraversalCompiler`; yes |
| `JunctionTraversal` / allocation | point joining adjacent corridor rails | bounded junction authority, minimum bend separation | traversal apply; yes or fallback |
| candidate bypass/exterior rails | explicit candidate `Point[]` segments | provisional route-local authority after selection | selector then legacy compilation; yes if preserved |
| shared-corner offset | point displacement only, no interval extent/owner | post-hoc mutable heuristic | `BuildRoute`; yes, including diagonals |

All except the last are variations of **axis-aligned rail with interval occupancy**, plus metadata: orientation, semantic role, owner/movement scope, terminal permissions, topology predecessor/successor and revision. Junctions are intersections/transitions between rails, not another lane system. Terminal ports require a semantic ordering policy, return routes require side-choice/topology, and ownership remains a serialization property; these are genuine behaviours but can decorate common rail demand/assignment geometry.

A common shape is viable:

```text
RailDemand
  stable route/demand identity
  orientation and preferred coordinate/range
  occupied interval (spacing-inflated for conflict)
  semantic role (terminal, through, return, bypass, turn transition)
  owner/movement scope
  allowed side/range and ordering constraints

AssignedRail
  rail group identity
  axis coordinate / lane index
  occupied interval
  predecessor/successor transition identity
```

This could subsume `AllocatedCorridorLane`, hypothetical band lane fields, Stage C assigned-lane dictionaries, terminal lane offsets and most return-lane metadata. `RoutingCorridor` can become a discovered rail-group envelope; `CorridorTraversal` can become an ordered sequence of assigned rails. Physical ownership models should remain separate.

## 5. Hard defect -> provisional demand

| Defect | Detector | Provisional requirement | Movement scope / regeneration closure | Shared mechanism and genuine special case |
|---|---|---|---|---|
| route through node | canonical segment/rectangle classifier (currently validator/candidate checks) | route R requires bypass rail on viable side(s) of N, with inflated node interval | nearest movable sibling/subtree/project or band/lane scope; route, obstacle-related routes and moved-scope incident routes | rail conflicts/constraints shared; side feasibility and cost are obstacle-specific |
| shared collinear geometry | canonical collinear interval classifier | one distinct parallel rail demand per semantic route | rail group and any scope moved to fit its lanes | ordinary grouping/lane colouring; intentional semantic merge must be marked |
| shared bend/ambiguous endpoint | canonical point/contact classifier | distinct turn-coordinate or short transition-rail demands | all routes sharing turn/terminal and affected rail groups | grouping shared; semantic terminal/junction contacts may intentionally coincide |
| diagonal geometry | orthogonality invariant detector | discard route and request an orthogonal sequence through equivalent terminal/bypass rails | route's full interaction component | construction/regeneration shared; never “straighten” a diagonal without clearance/topology evidence |
| immediate reversal | construction invariant detector and normalizer assertion | no rail demand: candidate invalid; regenerate from semantic endpoints/assigned rails | route plus shared terminal/turn component | special rejection rule only; allocating space to a reversal preserves invalid topology |

This model is suitable. Detection must produce requirements before final placement; it should not move the offending waypoint. When no viable bypass rail exists, the unresolved requirement is an explicit hard failure or capability result, not permission to emit a collision.

## 6. Canonical contact classification

One segment/contact engine should return:

| Kind | Definition / policy |
|---|---|
| disjoint | no intersection and separation >= required spacing |
| close non-contact | no intersection but parallel/separation envelope violates spacing |
| positive collinear overlap | same axis and interval intersection has positive length; conflict unless explicit semantic merge |
| endpoint-endpoint | both segment endpoints; classify further as terminal, bend, continuation or semantic junction |
| endpoint-interior | exactly one endpoint; ambiguous unless explicitly terminal/junction topology permits it |
| shared bend | both routes turn at the contact; conflict |
| shared straight continuation | neither turns and collinear contact has no positive overlap; permitted only if traceability remains unambiguous |
| strict-interior perpendicular crossover | intersection is strictly inside both segments, neither route bends/starts/ends/merges/branches; permitted with advisory cost |
| perpendicular involving bend | intersection at a turn or endpoint; not clean, therefore conflict |
| terminal contact | contact at semantic source/target with compatible direction and terminal allocation; intentional |
| semantic junction | explicitly modelled merge/branch identity; intentional, otherwise shared geometry is ambiguous |

The required crossover rule is confirmed exactly: strict interior to both straight segments, with no bend, start, end, merge or branch.

Duplication exists in `TraceabilityValidator`, `BandConflictGrouper.ClassifyContact`, `OrthogonalGeometry` helpers, global/regional interaction discovery, candidate obstacle/crossing scoring and junction metrics. Stage C's classifier distinguishes only a subset (`None`, `StraightContinuation`, `AmbiguousBend`, `CleanCrossover`); legacy validation reports perpendicular crossings without proving they are clean. The common geometry classifier should be authoritative; consumers decide whether a classified contact is a hard conflict, semantic permission or advisory cost.

## 7. One conflict-group mechanism

The generic process works cleanly for horizontal/vertical route intervals, bypass rails, node rectangles in one ordered layer, sibling subtree bounds and project bounds:

```text
produce occupancy items
-> indexed conflict relation
-> deterministic connected components
-> per-component ordering/lane solution
-> required axis extent
-> monotonic movement constraints
```

Reusable parts are stable item ordering, union-find/component enumeration, interval/rectangle indexing, deterministic merge and telemetry. Specialised inputs supply conflict predicates, fixed/ordered relations, legal lane ranges and movement scopes.

It becomes artificial if every item is forced to be a “rail”: hierarchy dependencies, semantic ownership, candidate topology and project-relative serialization are not occupancy conflicts. Rectangle packing is still compatible with the same component/constraint machinery, but not necessarily the same lane-colouring solver. Therefore share the **conflict graph and constraint output protocol**, with interval-lane and ordered-rectangle solvers as two small algorithms.

## 8. One persistent X/Y constraint system

| Current movement | Expressible constraint |
|---|---|
| base node placement | base position plus minimum X/Y between sibling/subtree and layer scopes |
| layer spacing / grouped expansion | minimum Y for layer and all lower layers |
| legacy capacity/lane expansion | same minimum Y, or X for vertical rail capacity |
| subtree movement | minimum X/Y for layout subtree or ordered sibling suffix |
| project placement | minimum X/Y for ordered project suffix/root |
| project bound reflow | minimum width/height of project root; may induce suffix displacement |
| route-driven adjustment | minimum X/Y issued by rail or obstacle demand for its chosen movement scope |
| repair-time displacement | should become a demand/constraint before final generation, not a separate movement API |

Required scopes are: node (only when hierarchy permits independent movement), layout subtree, ordered sibling suffix, layer-and-lower suffix, project root, and ordered project suffix. “Band” and “conflict group” are demand owners, not movement scopes. A root-project-group scope is only needed if projects share one ordered placement sequence; naming symmetry alone is insufficient.

The store must live for the entire generation, keyed by stable base scope identity and axis. Merge is `max(existing, proposed)`. Materialisation always starts from immutable base positions plus the complete constraint closure, never from already translated coordinates as a new base. New route demands can increase minima; none can lower them. Revision IDs bind observations and routes to the materialized constraint revision.

## 9. Origin-to-extent sweep

A stable top-left to bottom-right sweep naturally handles ordered siblings, subtree bounds, layers, projects and positive space expansion. Process hierarchy dependencies before coordinate ties; within a dependency level use stable Y, X, scope kind and identity. A scope's final origin is `max(base origin, predecessor extent + gap, stored minimum)`.

Simple coordinate order is insufficient for parent-before-child containment, sibling suffix movement, canonical nodes shared by multiple branches and projects whose bounds expand after owned routes. Those require an explicit dependency DAG. Canonical shared nodes must have one stable owner/base placement while routes from every branch contribute demands; they must not be visited/moved once per parent tree.

Upward/return routes do not require moving established upper geometry backward. They produce exterior/side rail capacity or additional forward X/Y extent. A new exterior demand, changed project width, or a rail assignment that alters obstacle envelopes can require another convergence iteration. Monotonic minima guarantee termination only if demand production is finite and assignments are deterministic; a guard should diagnose a cycle, not silently switch authority.

## 10. Route invalidation and regeneration

After a constraint revision, begin with moved scopes and calculate a fixed point over:

1. routes incident to moved nodes;
2. routes crossing a moved layer/project boundary;
3. routes occupying a changed rail group;
4. routes whose indexed obstacle set/envelope changed;
5. routes sharing terminals or assigned turns with an invalidated route;
6. routes crossing resized project containers;
7. routes connected through a canonical node whose terminal allocation changed;
8. further movement scopes demanded by regenerating those routes.

This is the same interaction-component relation used for authority. Exact old points are safe only when terminals, all assigned rail coordinates, intervening obstacle relationships, project crossings and shared terminal/turn assignments are revision-identical. Otherwise retain the semantic dependency, discard stale points, and regenerate from current nodes and assigned rails. Complete component regeneration is simpler and safer when rail assignments or terminal ordering change; incremental retention is valuable for components proven untouched.

Current point mutation that would become regeneration includes `SeparateOverlappingCorners`, corridor lane point shifts, traversal fallback application and repair candidate replacement. Normalization remains canonicalization, not topology repair.

## 11. Interaction-component closure and evidence

Deterministic closure algorithm:

1. create one vertex per logical route, stable ID order;
2. union routes in the same rail conflict component;
3. union routes sharing a semantic terminal or assigned turn;
4. for route/node conflicts, link the route to routes incident to that obstacle when moving/bypassing it can affect them;
5. add all bands and projects crossed by member routes;
6. add nodes in every candidate movement scope;
7. union every route invalidated by moving those scopes;
8. union components whose movement scopes overlap;
9. repeat until no route/scope is added; enumerate roots by stable minimum ID.

With union-find, known relations cost `O((R + E) alpha(R))`; relation discovery dominates. Interval sweep is `O(S log S + C)`, endpoint lookup is `O(R)`, obstacle relations should be `O((S + N) log N + K)` with a spatial index, and movement invalidation should use revision-keyed scope/route indexes rather than scanning all routes each iteration.

The current Release CLI regenerated all four combinations to `artifacts/current-state-audit/routing-consolidation` using `artifacts/routing-regression/effective-vsix-settings.json` for duplicated mode and `artifacts/node-duplication/real-project-deduplicated-settings.json` for deduplicated mode. `scripts/report-routing-consolidation-evidence.ps1` applies the available relations to those current diagnostics. “Direct” closure includes semantic endpoints, validator route pairs, route/obstacle-terminal coupling and interval-overlap band groups. “Movement-closed” additionally closes deficient bands over their lower/deeper observed band memberships. It does not claim project/movement relations unavailable in the diagnostic schema. The complete post-repair validator set—not only focused strict findings—supplies counts.

| Graph | Nodes | Routes | Segments | Bands | Projects | Current band conflict groups | Direct components / largest routes | Movement-closed components / largest routes,nodes,bands,projects |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| StandardIo duplicated | 22 | 21 | 39 | 6 | 1 | 8 | 1 / 21 | 1 / 21,22,6,1 |
| StandardIo deduplicated | 11 | 12 | 53 | 6 | 1 | 9 | 1 / 12 | 1 / 12,11,5,1 |
| cCoder duplicated | 1,094 | 1,072 | 2,394 | 8 | 1 | 266 | 22 / 271 | 22 / 271,272,4,1 |
| cCoder deduplicated | 180 | 294 | 1,305 | 6 | 1 | 8 | 1 / 294 | 1 / 294,180,3,1 |

Largest components merge primarily through shared semantic endpoints in deduplicated mode, then transitively through broad interval overlap groups. Duplicated cCoder breaks terminal sharing and therefore remains partitioned. Treating every route in a lower-layer movement suffix as interacting would be unnecessarily broad; the evidence script uses observed deeper-band membership as a conservative proxy, yet deficient bands do not further merge these already-formed results. A production closure needs exact scope membership and obstacle indexes before component migration can be judged safe/useful.

Current legacy interaction-region count is not emitted by these diagnostics, and current diagnostics do not distinguish clean from bend-involved perpendicular crossings. Those are evidence gaps, reported rather than guessed.

## 12. Four-graph behavioural evidence

| Graph | Hard-defect occurrences convertible to demand | X opportunities (parallel findings) | Y opportunities (deficient bands) | perpendicular advisories needing canonical reclassification | ambiguous bends | candidate independent regions |
|---|---:|---:|---:|---:|---:|---:|
| StandardIo duplicated | 0 | 0 | 0 | 0 | 0 | 1 |
| StandardIo deduplicated | 3 | 3 | 0 | 4 | 0 | 1 |
| cCoder duplicated | 53 | 44 | 0 | 320 | 0 | 22 |
| cCoder deduplicated | 776 | 887 | 2 | 2,866 | 29 | 1 |

“Hard-defect occurrences” sums post-repair node collisions, shared segments, reused bends, immediate reversals and unsupported shapes; one demand may resolve several occurrences, so this is an opportunity count, not required rail count. Likewise perpendicular validator advisories cannot be called clean crossovers until the canonical classifier proves strict-interior/no-bend semantics. Current grouped conflict groups are observational Stage B overlap groups; none activates Stage C on cCoder deduplicated. Committed evidence: `docs/evidence/routing-consolidation-evidence.csv`.

## 13. Parallelism around one semantic algorithm

| Work | Immutable input / partition | Result and deterministic merge | Current blocker / approximate cCoder share |
|---|---|---|---|
| conflict discovery | revision-fixed segments; spatial tiles/orientation/axis | sorted contact records deduplicated by stable pair key | repeated scans; part of validation/interaction cost |
| independent component analysis | fixed conflict graph; component root | rail/constraint proposals merged by stable key and `max` | cCoder deduplicated has only one component under current closure |
| obstacle checks | fixed node R-tree and candidate segments; route/candidate batches | ordered candidate validity/cost records | candidate builders repeatedly scan/check; candidate construction is a major part of 11.4 s |
| rail-demand generation | fixed placement/routes; route or band partition | stable demand IDs, globally sorted | some builders mutate used-corner/fan-out state |
| candidate construction | fixed semantic endpoints/rails/obstacles; route | sorted candidate set by semantic score/signature | shared mutable occupancy/used-corner choices |
| validation | fixed final nodes/routes; spatial tiles/pair ranges | unique findings sorted by code/route/pair/location | exhaustive pair scans; several seconds within repair confirmation/validation |
| diagnostic projection | immutable generation result; category/route partitions | JSON records sorted by stable IDs | already logically pure; lower work share |

Constraint materialisation, lane ordering within a conflict component and stable component commit remain sequential. Pure results merge by `(scope axis, scope identity, demand/route ID)`, constraints by maximum minimum, contacts by unique canonical pair/location, and candidate scores by the existing complete lexicographic tuple plus stable signature. No alternative routing algorithm is needed for parallelism.

## 14. Data structures and indexing

| Operation | Current shape | Consolidated/indexed choice |
|---|---|---|
| route/node obstacle checks | repeated candidate segment against node collections; some local dictionaries | revision-keyed rectangle/R-tree or uniform grid; query segment envelope |
| route/route interaction | route/segment pair scans in validation/scoring/discovery | orientation-specific interval indexes and spatial tiles; canonical contact engine |
| band conflict grouping | sorted intervals with bounded forward comparisons | retain interval sweep; share generic component builder |
| validation | full route/node and route-pair checking at several confirmations | reuse immutable contact/obstacle index per route revision; exhaustive indexed queries |
| repair candidate scoring | repeated global validation/corridor rebuild for trials | eliminate most repair through pre-generation demands; score affected component using cached unaffected relationships |
| corridor/junction lookup | dictionaries after corridor observation; junction scans during construction | retain stable dictionary IDs; use coordinate/orientation key for transitions |
| component closure | not a first-class production index | union-find plus endpoint, rail, obstacle, scope and project reverse indexes |
| candidate selection | global/regional collections and repeated pair scoring | one component algorithm with cached pair contribution matrix and stable bounded candidates |

The 19.97-second instrumented cCoder run attributed roughly 11.4 seconds to candidate construction/selection, 6.17 seconds within regional optimisation, and 4.17 seconds to repair passes (inclusive phases overlap). Much of the cost is repeated relationship discovery and confirmation, not inherently different geometry.

## 15. Target conceptual phase reduction

The target can be understood as eight authoritative phases:

1. semantic/render graph;
2. immutable stable base placement and hierarchy;
3. provisional route topology plus node/rail/obstacle demands;
4. canonical conflict grouping and persistent X/Y/size constraint solving;
5. materialized placement revision;
6. assigned-rail route generation for invalidated components;
7. invariant validation;
8. coordinate ownership and serialization.

Node spacing should be solved from hierarchy/rectangles first, but route spacing can enlarge it. Therefore provisional routing and node spacing participate in one monotonic convergence loop: materialize base node constraints, produce route demands, raise constraints, rematerialize and regenerate invalidated components until stable. Final route generation happens only after assigned rails at the stable revision.

Potential dispositions, subject to product choices:

| Current phase/subsystem | Target role |
|---|---|
| legacy candidate construction | specialised provisional route/demand producer plus shared candidate helper |
| corridor observation and Stage B observation | merged rail-demand projection; diagnostics become views over the same records |
| corridor/band lane allocators | one rail conflict/assignment algorithm |
| capacity expansion and grouped constraints | one persistent axis/size constraint solver |
| global/regional selection | one component selection algorithm with indexing/batching |
| traversal compiler/simple junction allocator | assigned-rail transition generator helper |
| `SeparateOverlappingCorners` | deleted once turn demands generate distinct bends |
| traversal fallback | invariant rejection/regeneration, not an alternate authoritative geometry |
| `RouteRepairCoordinator` | mostly deleted; unexpected failures trigger component regeneration/hard failure |
| normalization | small invariant/canonicalization helper |
| Stage C support gate | migration-only adapter until one authority covers all route-demand producers |
| ownership/serialization | retained as separate final concern |

## 16. Validation versus prevention

| Finding | Preventing owner | Final response |
|---|---|---|
| `NodeCollision` | obstacle bypass demand producer + rail/constraint solver | expected conflict becomes demand; unexpected final collision rejects component |
| `SharedSegment` | rail conflict grouping/assignment | expected conflict becomes distinct rails; intentional semantic junction explicitly marked |
| `ReusedBend` | turn/transition demand allocation | expected conflict becomes turn demands; unexpected reuse rejects component |
| `SpacingDeficit` | spacing-inflated conflict relation and X/Y constraint solver | expected conflict raises extent before final generation |
| `ImmediateReversal` | route topology constructor | candidate invalid; regenerate component |
| `NonOrthogonalSegment` | assigned-rail route/transition generator | invariant failure; reject/regenerate, never repair a waypoint |
| traceability/serialization mismatch | logical-route revision and ownership reconstruction | hard generation failure |

A general post-generation `RouteRepairCoordinator` should not be necessary in the target. Expected geometry conflicts belong in demand generation. A small retry/regeneration coordinator may remain for candidate topology failure, but it should discard and regenerate a component under the same authority, not run a second geometry algorithm.

## 17. Graph size as a concern

| Step scales with | Recommendation |
|---|---|
| hierarchy/base placement: nodes + hierarchy edges | same implementation with subtree/project indexes |
| provisional topology: routes x bounded candidates | same implementation, parallel per route where pure |
| obstacle discovery: segments/nodes + hits | same implementation with spatial index |
| rail conflicts: demands + conflicts | same interval sweep/component builder |
| contact validation: segments + actual contacts | same canonical classifier with indexing and parallel observation |
| selection: candidates + interaction-component size | same semantics partitioned by components; cache pair scores |
| constraint solving: scopes + dependency edges | same sequential monotonic DAG sweep |
| regeneration: invalidated routes/components | same implementation partitioned by independent components |
| project ownership: physical segments/projects | same implementation unchanged |

No observed category requires a distinct small/large semantic algorithm. Very large components may require bounded candidate enumeration or incremental deterministic optimisation, but the score, acceptance rule and resulting invariants must remain the same. A timeout/budget is an explicit incomplete result, not a new route meaning.

## 18. Migration scaffolding

| Scaffold | Needed until / deletion condition | Permanent risk |
|---|---|---|
| whole-graph `Supports` gate | all route families produce common demands and one authority validates closed components | permanent exemptions from universal rules |
| empty corridor/lane/traversal models on grouped result | result consumers accept common rail/component result | false impression both models are meaningful |
| `LegacyRoutingResult` carrying grouped fields | one branch-neutral generation result exists | branch condition leaks through diagnostics/serialization |
| duplicate corridor/band/group telemetry | diagnostics project common demand/constraint records | disagreement and repeated expensive observation |
| branch-specific normalization/validation | one final route authority reaches shared boundaries | divergent invariant semantics |
| `legacy route generation` phase wrapping both | phase model names provisional/classification/authority explicitly | unusable performance evidence |
| experimental corner branch | demand-based turn allocation proves equivalent/better fixtures | hidden alternative behaviour and merge drift |
| incidental compatibility predicates | explicit capability records and rejection reasons exist | canonical/cross-project cases admitted/rejected accidentally |
| Stage B hypothetical lanes followed by Stage C reassignment | observation output is the actual common demand input | duplicated lane calculations |

## 19. Consolidation map

| Existing concepts | Underlying concern | Candidate target | Evidence needed | Likely disposition |
|---|---|---|---|---|
| `RoutingCorridor`, band, return region, bypass segment | rail envelope/role | rail group + role metadata | topology/consumer field comparison | merge geometry; retain role producers |
| `AllocatedCorridorLane`, band lane index, Stage C assigned lane, terminal lane | interval occupancy | `AssignedRail` | terminal ordering and transition fixtures | merge |
| `CapacityRequest`, `MissingExtent`, `MinimumSpacingConstraint`, layer expansion | required axis extent | persistent axis/size constraint | X/Y and movement-scope fixtures | merge |
| regional interaction, global pair relations, `BandConflictGroup` | interacting geometry | canonical conflict graph/component | closure evidence with precise scopes | merge builder; specialise relation producers |
| node/subtree/layer/project translations | monotonic movement | scope constraint + materializer | hierarchy/canonical/project proofs | merge API |
| `CorridorTraversal`, junction traversal, terminal transition | route through assigned rails | ordered rail sequence + transition | supported/fallback topology catalogue | merge representation |
| candidate builders by ordinary/canonical/return | route topology requirements | specialised demand producers | route-family fixtures | retain small specialisations |
| global selector/regional optimiser | candidate-set optimisation | component path selector | exact semantic parity at scale | merge |
| corner separator/simple junction repair | turn allocation | turn/transition demands | overlapping-corner fixtures | replace/delete |
| repair coordinator/capacity repair | late defect correction | demand conversion + component regeneration | all finding classes prevented | delete most; retain retry assertion helper if needed |
| Stage B diagnostic and production calculation | band occupancy | diagnostic projection over rail demands | output parity | merge calculation |
| generated/normalized/validated wrappers | revision authority | typed route revision boundaries | stale-state tests | retain/simplify |
| ownership segments/anchors | Draw.io movement ownership | ownership compiler | reconstruction/manual movement tests | retain |

## 20. Complexity budget

Measured scope (`Models/Drawios` plus `Services/Processings/Drawios`, including layout and geometry):

```text
production files: 41
production lines: 8,146
declared types: 144 (82 records, 0 interfaces in this internal subsystem)
routing authorities: 2 post-gate, plus common provisional construction
conceptual phases: roughly 15-20 named construction/observation/allocation/
                   compilation/selection/repair/validation steps
distinct lane/rail representations: at least 10
distinct grouping representations: at least 4
distinct spacing/capacity representations: at least 5
validation/repair: repeated compile-normalize-validate confirmations plus final validation
normal-generation authority branch: whole-graph grouped/legacy split, followed by numerous
                                    capacity, expansion, duplicate-mode, selector and repair gates
```

The target conceptual budget is not a line target:

```text
core geometry concepts: point, segment, rectangle, contact, rail demand,
                        assigned rail, movement scope, axis constraint, interaction component
core algorithms: canonical contact classification, indexed conflict-component construction,
                 rail assignment, monotonic constraint materialisation,
                 component invalidation/regeneration, invariant validation
specialised demand producers: terminal, obstacle bypass, adjacent/skipped/downward,
                              return/upward, cross-project, canonical shared endpoint
authoritative phases: 8
temporary adapters: one old/new result adapter and explicit capability gate during migration only
```

## 21. Risks and unresolved choices

Risks of consolidation:

- an over-generic “rail” model could hide terminal/topology semantics;
- broad closure can collapse deduplicated graphs to whole-graph work;
- changing tie-breaking while merging selectors can change accepted output;
- a monotonic solver can over-expand diagrams if movement scopes are coarse;
- caching contact/obstacle relations without strict revisions can reintroduce stale geometry;
- removing repair before demand producers cover every defect would regress visibility.

Risks of retaining the current architecture:

- validators continue to document rather than prevent universal-rule violations;
- branch- and size-specific semantics remain hard to reason about;
- every new route family can add another candidate, lane, grouping and repair path;
- repeated scans/confirmations dominate large-graph runtime;
- migration scaffolding becomes permanent product architecture;
- changes in one authority do not improve the other.

Questions requiring product decision:

1. Which final findings are hard generation failures versus permitted advisories?
2. Are clean crossovers merely permitted or should they be minimised after every hard invariant?
3. May a canonical node's shared terminal couple all branches into one authority component, or can terminal-side allocations partition safely?
4. Which objects may move independently: individual nodes, only subtrees, or ordered suffixes?
5. Is diagram growth always preferable to an explicit unsatisfied-capability result?
6. Must one deterministic selection optimum be invariant across graph size, or is a declared bounded approximation acceptable?
7. Should exact output compatibility be preserved while consolidating, or only the visibility/semantic contract?

Recommended consolidation choices, without an implementation plan:

- Standardise contact classification first and make every observer/validator consume it.
- Use one rail-demand/assignment geometry with explicit semantic-role metadata; retain specialised demand producers.
- Use one generic conflict-component builder, with interval-lane and ordered-rectangle solvers rather than one artificial universal solver.
- Make X/Y/width/height constraints persistent from immutable base placement for an entire generation.
- Define exact movement scopes and revision-keyed invalidation closure before adopting component-level authority.
- Use one route-selection semantics at all scales; improve indexing, partitioning, batching and pure parallel analysis around it.
- Treat expected final defects as missing demands and unexpected defects as regeneration/hard failure; do not retain general waypoint repair as target architecture.
- Keep whole-graph fallback during consolidation until precise closure evidence proves smaller authority safe; do not interpret this as a permanent route-family exemption.

## Verification scope

Evidence source files:

- `artifacts/current-state-audit/routing-consolidation/standard-duplicated.json`
- `artifacts/current-state-audit/routing-consolidation/standard-deduplicated.json`
- `artifacts/current-state-audit/routing-consolidation/ccoder-duplicated.json`
- `artifacts/current-state-audit/routing-consolidation/ccoder-deduplicated.json`
- `docs/evidence/routing-consolidation-evidence.csv`

The review script is deterministic and observational. It writes only the CSV evidence file and does not invoke or modify route generation.
