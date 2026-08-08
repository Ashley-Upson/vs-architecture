# Architecture Diagram V6 — Normative Diagramming Contract

> **Purpose**
>
> This document is the developer-facing source of truth for the expected behaviour of the Architecture Diagram V6 planner and renderer.
>
> The implementation MUST be checked against this document before and after every V6 change. If intended behaviour changes, this document MUST be updated deliberately in the same tranche. Implementation convenience, existing helper behaviour, or passing tests MUST NOT silently override this contract.

## 1. Contract language and authority

The terms **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

This document describes expected behaviour, not the current implementation. Where the current implementation differs from this contract, the implementation is wrong unless the contract is explicitly changed.

The planner MUST have one authoritative owner for each class of decision. Later stages MUST consume earlier authoritative decisions rather than reinterpret them.

The renderer MUST be mechanical. It MUST NOT make semantic placement, routing, or styling decisions.

## 2. Global priorities

When goals conflict, use this priority order:

1. Semantic correctness and complete relationship accounting.
2. Final geometry correctness and route validity.
3. Readable dependency-tree structure.
4. Deterministic output.
5. Configured styling and visual fidelity.
6. Compactness.

A wider diagram is preferable to a compact diagram whose parent/child structure is unclear.

A route MUST NOT be made invalid merely to keep the diagram small.

A node MUST NOT be moved away from its dependency-tree structure merely to improve global row packing.

## 3. Configuration authority

Integration generation MUST use the preserved user configuration unless an explicit settings argument overrides it.

Current preserved integration configuration:

`C:\Users\Ash\Documents\codex-artifacts\architecture-user-settings.json`

Previously recorded authoritative configuration source:

- source: `preserved-user-config`
- SHA-256: `9af9d5a469147d3f07b5805581f5ab14d57131e49efa725dc1ae41c9db740d1c`
- source schema: 1
- effective schema: 2

An integration review run MUST NOT silently fall back to repository defaults.

Configuration precedence MUST be explicit and deterministic:

1. explicit `--settings` / equivalent explicit configuration;
2. preserved user configuration;
3. repository defaults only when neither of the above applies.

The effective configuration source SHOULD be reported in diagnostics.

Current preserved-config routing values observed during the V6 work include:

- `ParallelLaneSpacing = 12 px`
- `EdgePortSpacing = 25 px`
- `LinkPadding = 10 px`
- `NodeToRouteClearance = 35 px`
- `RoutingRowMinimum = 20 px`

These are configuration values, not hard-coded V6 constants. The planner MUST consume the effective configuration.

## 4. Authoritative planner-stage order

The intended authority order is:

1. semantic analysis / semantic input;
2. semantic-to-physical projection;
3. positional ownership;
4. reserved layer planning;
5. ordinary bottom-up layer solving;
6. terminal-capacity-aware node sizing;
7. local dependency-tree construction;
8. whole-tree global packing;
9. final logical grid construction;
10. authoritative abstract routing;
11. lane / crossing / terminal-demand allocation;
12. footprint-expansion convergence;
13. full rebuild when footprint demand changes;
14. route boundary/component construction;
15. physical track sizing;
16. relative geometry;
17. absolute physical scene compilation;
18. final authoritative validation;
19. mechanical Draw.io rendering;
20. renderer-fidelity validation/accounting.

A later stage MUST NOT mutate an earlier authority's decision unless the pipeline explicitly loops back and rebuilds every downstream authority that depends on it.

Examples:

- routing MUST NOT move nodes;
- materialisation MUST NOT reroute;
- renderer MUST NOT choose connector colours;
- point reduction MUST NOT hide an invalid topology;
- a later global row pack MUST NOT move one node inside a completed tree.

## 5. Semantic and physical projection

Every semantic node and relationship that remains in the selected scope MUST be accounted for.

Projection MUST be deterministic.

Canonical physical projection is the default.

Configured duplication is an explicit exception, not a general rule to duplicate every reused dependency.

A reused semantic node MAY be duplicated only according to configured duplication rules.

Every physical node SHOULD retain provenance including semantic identity, physical identity, project ownership, duplication provenance where applicable, and External/standalone classification where applicable.

Every semantic relationship in scope MUST map to a physical relationship or to an explicit diagnostic explaining why it cannot.

Relationships MUST NOT silently disappear.

## 6. Positional ownership and tree membership

Each physical node MUST have at most one authoritative positional parent.

Rules:

- no eligible positional parent → the node is a placement-tree root;
- exactly one eligible parent → that parent owns placement;
- multiple eligible parents → choose one deterministic primary positional parent;
- all other semantic parents remain routing relationships only.

Multiple-parent nodes MUST NOT be horizontally averaged between parents.

Placement ownership MUST be resolved before final X placement.

The planner SHOULD retain positional parent, positional children, tree root, tree identifier, and multi-parent count/provenance.

Tree membership MUST remain stable through placement.

## 7. Reserved role/type layers

Configured role/name matching rules establish reserved layers first.

Matching uses **first-match semantics**.

The currently established effective reserved category order is:

1. Aggregation
2. Coordination
3. Orchestration
4. Processing
5. Service
6. Broker

These categories MUST remain distinct when the configuration makes them distinct.

An ordinary/non-matching node MUST NOT be placed onto a reserved role layer.

A node matching a more-specific configured category MUST NOT fall through into a broader later category.

Reserved layers are fixed ordering constraints. They are not the complete hierarchy.

When two nodes with the same resolved reserved role form a positional
parent/child chain, the role band remains the styling and ordering authority,
but the child MUST be placed on a deterministic inserted sublayer below the
parent. Unrelated nodes in that role continue to share the base role layer.
This prevents a same-role dependency from being rendered as a zero-height
hierarchy edge without changing first-match role resolution.

## 8. External layer

External dependencies MUST occupy a dedicated bottom layer.

External nodes MUST use the established display form:

- line 1: `[External]`
- line 2: simple name

External FQNs remain metadata, not visible labels.

A sole-owner External dependency SHOULD be horizontally aligned beneath its authoritative owner where unobstructed.

If blocked, the planner MUST choose the nearest deterministic free position and SHOULD record blocker, preferred X, final X, and displacement.

External placement MUST NOT reposition the owning ordinary tree.

## 9. Standalone nodes

Standalone nodes are nodes with no parent and no child relationship under the established semantic definition.

Placement regions are authoritative and mutually exclusive:

- `External` for external dependencies;
- `Standalone` for standalone nodes;
- `DependencyHierarchy` for all remaining dependency-connected nodes.

Standalone status overrides normal role/type hierarchy-layer placement. A
standalone Service, ProcessingService, AggregationService, CoordinationService,
OrchestrationService, Broker, or unclassified node MUST belong to the dedicated
standalone grid rather than its corresponding dependency-hierarchy band.

Role/type classification MAY still control style, metadata, and deterministic
ordering within the standalone grid, but MUST NOT move a standalone node onto a
reserved or ordinary dependency layer. Standalone nodes do not participate in
dependency-depth, root, reserved-band, or tree-solving calculations.

Standalone nodes SHOULD live in a dedicated compact region rather than being mixed into dependency trees.

If multiple standalone rows/layers are required, there MUST be proper routing/layer clearance between them.

Standalone layout MUST NOT influence ordinary tree placement.

The complete standalone grid is an atomic placement block. Its internal node
positions are frozen before global packing; dependency-tree placement and
standalone-grid placement MUST NOT mutate one another internally. Any collision
is resolved by translating complete regions/blocks, never by reassigning one
standalone node through the hierarchy solver.

## 10. Ordinary Y-layer solving — bottom-up dependency depth

Ordinary/non-reserved layers MUST be solved from actual dependency constraints.

External is the bottom anchor.

Conceptually:

- External = base depth 0;
- an ordinary node depending only on External requires one ordinary layer above External;
- its parent requires one layer above that;
- and so on.

For an unconstrained ordinary node:

`required structural depth = 1 + max(depth of dependency children)`

A placement-tree root is **not** automatically a top-layer node.

A root depending only on External MUST occupy the lowest valid ordinary layer immediately above External.

Independent shallow roots MUST NOT be globally promoted to the depth of unrelated deeper trees.

## 11. Ordinary layers around reserved layers

Reserved layers are anchors inside the solved hierarchy.

Ordinary layers MAY and MUST be inserted wherever dependency constraints require them:

- above the highest reserved layer;
- between reserved layers;
- below the lowest reserved layer and above External.

If a dependency chain needs one or more ordinary layers between two reserved bands, create those layers.

Do not collapse ordinary nodes onto reserved bands.

## 12. Lowest-valid-layer rule

For every ordinary node, the final Y layer MUST be the **lowest valid non-reserved layer** satisfying all applicable constraints.

Applicable constraints include:

- dependency children must be below;
- positional parent must be above;
- reserved-band ordering;
- ordinary nodes cannot occupy reserved layers;
- special-region constraints where explicitly applicable.

Global analysed depth, stale structural depth, root status, or fallback row selection MUST NOT override the solved lowest-valid layer.

At the end of layer solving, there MUST be one authoritative final layer ordinal per node.

No later X-placement or routing stage may change Y.

## 13. Parent/child Y ordering

For every authoritative placement-parent edge:

- parent MUST be visually above child;
- child MUST be visually below parent.

If reserved-band constraints make a relationship impossible, the planner MUST surface the contradiction rather than silently violating dependency order.

## 14. Node labels

Visible node labels are compact.

Established label rules:

- no interface: `<Name>`
- exactly one relevant interface: `<Name>:<Interface>`
- multiple implementations represented by interface: `<Interface> (x implementations)`
- External: `[External]` plus simple name on the second line

Fully-qualified names remain metadata unless explicitly required by a future contract change.

Relationship-kind labels are hidden by default.

## 15. Visible node sizing

Visible node width MUST be sufficient for both visible label/content and edge terminal capacity.

`VisibleWidth = max(LabelRequiredWidth, PortCapacityRequiredWidth, ConfiguredMinimumWidth)`

Terminal capacity MUST expand the visible node when the actual visible edge would otherwise be unable to contain its terminal slots cleanly.

Ordinary routing demand MUST NOT arbitrarily inflate visible nodes merely because a routing corridor needs more internal capacity.

The planner MAY maintain a larger invisible routing footprint around the visible node for routing clearance, approach/departure corridors, lane capacity, and obstacle avoidance.

## 16. Odd logical-span sizing

Node horizontal logical span MUST be odd.

When a physical/terminal width requires an even logical span, round up to the next odd span.

Examples:

- required 4 cells → allocate 5;
- required 5 cells → allocate 5;
- required 6 cells → allocate 7.

Expansion MUST be symmetric around the node centre and preserve the authoritative centre cell/centreline.

Node sizing MUST be finalized before local tree construction and global tree packing.

## 17. Terminal-capacity width

For an edge with `n` terminal slots, required usable span MUST account for:

`leading inset + (n - 1) * configured port spacing + trailing inset`

The visible node width MUST be large enough for the larger of top-edge and bottom-edge demand.

If configured terminal spacing cannot fit, the planner MUST NOT duplicate ports, use exact corners, place terminals outside the visible edge, or silently compress spacing unless an explicit policy permits it.

Terminal demand MUST be calculated independently for the top and bottom edges;
visible width uses the larger demand and MUST NOT use total graph degree as a
proxy. The shared edge formula is `leading inset + (n - 1) * port spacing +
trailing inset`, and the same resolved inset/spacing values MUST be used by
logical span sizing, terminal allocation, and final validation.

The final visible node edge and the terminal allocation authority MUST remain
coordinate-compatible. If a terminal is allocated on the boundary of the
authoritative node footprint, the final visible geometry MUST preserve that
boundary (or an explicitly planned equivalent edge); a smaller centred label
rectangle MUST NOT be emitted in a way that makes Draw.io reconstruct the
terminal on a different X coordinate.

## 18. Tree-by-tree X placement

After Y layers and node widths are final, X placement MUST be performed **tree by tree**.

The planner MUST NOT globally pack all nodes on each visual row before tree construction.

For each placement root:

1. build the entire tree in local X coordinates;
2. recursively construct child subtrees;
3. calculate the completed tree's per-layer contour;
4. freeze internal tree geometry;
5. globally translate the whole tree into free space;
6. then place the next tree.

Unrelated trees MUST NOT influence the local geometry of the tree currently being built.

## 19. Recursive subtree construction

For each parent:

1. recursively construct every immediate child subtree;
2. determine the width/contour required by each child subtree;
3. pack immediate child subtrees contiguously with the required gap;
4. place the parent relative to its immediate children;
5. return the completed subtree contour.

Descendant width controls spacing between child subtrees.

Descendants MUST NOT redefine the parent-centering relationship to its immediate children.

## 20. Single-child placement

For a parent with exactly one authoritative placement child:

`ParentCentreX == ChildCentreX`

This is a hard final-placement invariant.

Unrelated same-layer nodes MUST NOT displace the child.

An unrelated tree MUST move around the completed parent/child structure.

A later global row-pack MUST NOT break this alignment.

## 21. Multiple-child parent centring

For a parent with multiple immediate children, the parent MUST be centred over the immediate-child group.

The centring reference is the immediate child group, not the entire descendant envelope.

Child-subtree widths determine sibling-subtree separation, but do not redefine parent-centre semantics.

## 22. Sibling and subtree locality

Immediate siblings MUST remain a contiguous visual group.

Unrelated nodes MUST NOT be inserted between siblings.

Unrelated trees MUST NOT be inserted between sibling subtrees.

Unused X space inside one tree MUST NOT be filled by extracting unrelated individual nodes from another tree.

Tree readability takes priority over maximum compactness.

## 23. Tree atomicity

Once a local tree has been constructed, its internal X geometry is frozen.

Global packing may apply only a whole-tree translation.

For each ordinary tree node:

`FinalX = LocalTreeX + WholeTreeTranslationX`

There MUST NOT be any unexplained per-node X delta after tree construction.

## 24. Per-layer tree contours

Each completed tree MUST expose occupancy per final visual layer:

`layer -> [minX, maxX]`

Contours SHOULD include final visible node widths and all placement-owned footprint/clearance needed to keep neighbouring trees distinct.

Global packing SHOULD use per-layer contours, not one giant rectangular tree width.

## 25. Whole-tree global packing

When placing a new tree beside already-placed trees, determine the minimum whole-tree translation required across every shared layer.

For each overlapping layer:

`existing.maxX(layer) + requiredTreeGap <= new.minX(layer)`

Use the maximum required translation across shared layers, then translate the entire new tree.

Do not mutate its internal coordinates.

## 26. Grid construction timing

The authoritative logical routing grid MUST be constructed from final node geometry.

That means after final Y solving, final visible width/odd-span expansion, local tree construction, whole-tree packing, and project placement.

Once the routing grid is authoritative, node placement is immutable.

If later footprint convergence changes node spans, the pipeline MUST loop back and rebuild placement/grid/routing coherently.

## 27. Footprint-expansion convergence

Footprint expansion MUST converge.

A fixed arbitrary two-pass limit is not acceptable.

Required model:

1. place/size;
2. route;
3. allocate lanes/endpoints;
4. determine required footprint spans;
5. if spans changed, rebuild all dependent stages;
6. repeat until requirements stop changing.

A defensive maximum iteration count MAY exist only as a failure guard.

If convergence is not reached, generation MUST report a hard planner failure rather than render geometry based on unconsumed requirements.

## 28. One authoritative route topology

There MUST be one authority for abstract route topology.

The current intended same-project authority is `FindOrthogonalPath` or its future replacement.

A later helper MUST NOT independently select another row/column route after topology has been chosen.

Destination-approach requirements, return corridors, and project transitions MUST be represented as constraints in the authoritative route topology rather than as a second hidden router.

## 29. Abstract route requirements

Every abstract relationship route MUST be deterministic, orthogonal, complete, and represented as a full ordered sequence of traversed cells/steps.

Source first movement MUST be downward.

Destination must ultimately approach the target from above and enter downward.

All traversed intermediate cells MUST remain represented in route provenance.

No implicit cell jump is allowed.

The route MUST avoid unrelated node footprints and use routing-capable cells only.

## 30. Blocked preferred routes

The generator MUST still generate diagrams when a preferred corridor is blocked.

The route planner SHOULD deterministically route around blockers using the authoritative routing grid/cells.

A route MUST NOT be dropped simply because the preferred corridor is unavailable.

If no valid route can be found, retain the relationship and attempted invalid geometry for diagnosis rather than silently omitting it.

## 31. Cross-project routing

Cross-project relationships MUST use explicit project/diagram transition topology.

Project transforms MUST be accounted for explicitly.

Cross-project routing MUST remain orthogonal and subject to the same final validation as same-project routes.

Project movement/translation MUST carry project-owned geometry consistently.

## 32. Source/destination endpoint topology

The endpoint chain is symmetric in intent.

Source side:

1. SourceTerminal
2. SourceNodeAnchor
3. source-local vertical / SourceExteriorDeparture
4. explicit endpoint-local handoff where required
5. ordinary route

Destination side:

1. ordinary route
2. explicit endpoint-local handoff where required
3. DestinationExteriorApproach / destination-local vertical
4. DestinationNodeAnchor
5. DestinationTerminal

Endpoint components are planned geometry, not renderer repairs.

## 33. Terminal slot ownership

The terminal slot owns the endpoint-local vertical X coordinate.

`SourceEndpointVertical.X == SourceTerminal.X`

`DestinationEndpointVertical.X == DestinationTerminal.X`

The ordinary route lane MAY use a different X.

If so, an explicit orthogonal planner-owned handoff connects the endpoint-local vertical to the ordinary route.

## 34. Terminal group placement

For each populated source-bottom or destination-top node edge:

1. determine terminal count;
2. determine usable visible edge after inset;
3. construct an evenly spaced slot group;
4. centre the complete slot group around the node centre;
5. assign relationships to slots using geometric route order.

One terminal MUST be centred.

Two terminals MUST be symmetric around centre.

Odd and even groups MUST remain centred and evenly spaced where capacity permits.

Terminal coordinates MUST be unique unless a future explicit shared-port semantic model is introduced.

The terminal allocator owns the centred edge slot positions. The ordinary
route lane may use a different X only when the accepted endpoint contract
contains an explicit orthogonal handoff; a renderer MUST NOT invent that
handoff or clamp a terminal into the node edge.

## 35. Terminal ordering

Terminal slot positions are determined by node-edge geometry.

Relationship-to-slot assignment is determined by route geometry.

Outgoing source terminals SHOULD be ordered left-to-right by downstream target/departure-route X.

Incoming destination terminals SHOULD be ordered left-to-right by source/approach-route X.

Stable deterministic tie-breaking is required.

## 36. Terminal inset and corners

Top/bottom terminal relative X MUST satisfy:

`0 < relativeX < 1`

subject to configured inset.

Exact corner positions are invalid.

## 37. Endpoint direction

Source:

- attach on bottom edge;
- first non-zero segment MUST be vertical;
- first non-zero segment MUST travel downward.

Destination:

- attach on top edge;
- final non-zero segment MUST be vertical;
- final non-zero segment MUST travel downward into the destination.

Terminal-to-first-waypoint and last-waypoint-to-terminal joins MUST be orthogonal.

## 38. Lane allocation

Lane allocation occurs after authoritative route topology.

Within each routing domain:

- overlapping collinear intervals MUST receive distinct lanes;
- lane ordering MUST be deterministic;
- allocated lane positions MUST satisfy configured parallel spacing;
- terminal-local approach/departure lanes MUST also respect required spacing.

Lane demand is authoritative input to physical track sizing.

## 39. Crossing model

Every route use of a dense routing cell MUST remain represented.

The crossing/lane model MUST distinguish horizontal pass-through, vertical pass-through, turn, and legitimate point crossing as appropriate.

Crossing information MUST NOT be discarded merely because multiple routes use the same cell.

A clean perpendicular point crossing is allowed.

A shared bend is a hard diagnostic when two unrelated routes use the same bend
coordinate. The finding MUST retain both route IDs, the exact coordinate, the
horizontal and vertical lane identities, and the owning turn cell. A clean
crossing is not valid when it shares a turn cell or endpoint.

A shared non-zero collinear segment between unrelated routes is not.

## 40. Shared segments and overlaps

Unrelated relationships MUST NOT share a non-zero collinear physical interval.

Final rendered geometry MUST be checked for:

- exact shared collinear intervals;
- under-spaced parallel intervals;
- endpoint fan-in/fan-out approaches that become visually indistinguishable.

Point crossings are allowed where clean.

Parallel routes MUST maintain configured minimum spacing over overlapping intervals.

Parallel-clearance findings MUST identify both route IDs, the overlapping
interval, measured separation, and the configured minimum. They are not to be
silently resolved by moving a rendered waypoint.

## 41. Track sizing

Track sizing is downstream of final lane demand.

`final route topology -> lane allocation -> lane demand -> track sizing -> physical coordinates`

Lane coordinates and required track extent MUST use the same geometry formula.

Established formula:

`last lane offset = port spacing + (lane count - 1) * parallel spacing`

`required track extent = last lane offset + trailing port spacing`

For the current preserved configuration:

`required extent = 25 + (n - 1) * 12 + 25`

The same model applies to horizontal rows and vertical columns.

No allocated lane may lie outside its final track.

## 42. Node-to-route clearance

Routing tracks MUST provide configured clearance from adjacent visible/routing node footprints.

Track capacity and node clearance are distinct concepts.

A track may fit its lanes yet still be invalid if a lane is too close to a node.

Both MUST be validated separately.

## 43. Boundary/component authority

Boundary/component construction represents the already-selected route topology.

It MAY identify component boundaries, split the authoritative path into runs/turns/endpoints/transitions, and assign explicit shared boundaries.

It MUST NOT choose another corridor, choose another route, overshoot and return, invent an unrepresented detour, or alter topology.

If adjacent components cannot reconcile on the authoritative topology, the route must be rebuilt by the route authority.

## 44. Shared component boundaries

Adjacent components MUST agree on one exact authoritative shared boundary.

Components MUST NOT independently choose incompatible endpoints and rely on a later repair step.

For boundaries `B0, B1, ... Bn`, component `i` is bounded by `Bi -> B(i+1)` subject to lane/cell geometry.

## 45. Turn geometry

A turn between horizontal and vertical runs MUST be located inside the authoritative turn cell.

The horizontal lane coordinate and vertical lane coordinate MUST intersect inside that cell's routing envelope.

The turn MUST touch the expected entry/exit sides from the authoritative route steps.

## 46. Run geometry

A horizontal or vertical run across its ordered authoritative cells MUST be monotonic from first boundary to last boundary.

A run MUST NOT overshoot its final boundary and return.

If allocated geometry cannot connect monotonically, report a topology/allocation contradiction rather than adding an unplanned bend.

## 47. Corridor provenance

Every physical segment MUST retain the exact route-cell/corridor provenance authorising it.

A broad track is not sufficient justification if the segment leaves the specific cells owned by the component.

## 48. Physical scene compilation

Physical scene compilation MUST be mechanical.

It MAY apply transforms, calculate final lane coordinates, concatenate authoritative component geometry, generate raw points, and remove safe redundant collinear points.

It MUST NOT choose routes, choose boundaries, reconcile mismatched topology, add repair L-shapes, move terminals, or move nodes.

Invalid attempted geometry MUST be retained for diagnostics/rendering in best-effort mode.

## 49. Collinear point reduction

A middle point `B` from `A -> B -> C` may be removed only when A/B/C are collinear, travel through B is monotonic, topology/provenance is preserved, and no required boundary is hidden.

`100 -> 180 -> 120` MUST NOT be reduced to `100 -> 120`.

## 50. Backtracking definition

Redundant backtracking is invalid.

This includes immediate reversals Right→Left, Left→Right, Up→Down, and Down→Up where the route retraces an interval unnecessarily.

It also includes overshoot-and-return where an intermediate coordinate lies beyond the final interval and the route immediately travels back across it.

Detection MUST work across individual components, component boundaries, endpoint handoffs, raw geometry, reduced/final geometry, and terminal joins.

Legitimate obstacle detours are allowed, but immediate retracing of the same interval is not.

## 51. Route validity and retained invalid routes

Every relationship remains renderer-available.

A route that fails validation MUST NOT silently disappear.

Normal/best-effort mode MUST retain the relationship and attempted geometry with invalid-route diagnostics.

Strict mode MUST reject hard-invalid output according to the generation contract.

## 52. Final physical polyline is an authoritative validation surface

The final route used for validation is:

`source attachment + planned/emitted waypoints + destination attachment`

Validation MUST cover the exact geometry intended for Draw.io.

At minimum validate:

- orthogonality;
- continuity;
- source direction;
- destination direction;
- terminal joins;
- corridor containment;
- lane containment;
- unrelated node intersections;
- exact shared segments;
- under-spaced parallel segments;
- redundant backtracking;
- overshoot/return;
- duplicate terminals;
- terminal group centring;
- terminal spacing;
- corner inset.

Component-local validation alone is insufficient.

## 53. Final placement validation

Final-pipeline placement validation MUST include:

- ordinary nodes on reserved layers = 0;
- dependency layer-order violations = 0;
- unexplained high/shallow-root promotion = 0;
- single-child X alignment violations = 0;
- multi-child parent-centering violations = 0;
- sibling interleave = 0;
- subtree interleave = 0;
- cross-tree interleave = 0;
- tree atomicity violations = 0;
- node overlaps = 0;
- unexplained External-owner displacement = 0 where unobstructed.

These checks MUST run after all placement passes.

## 54. Renderer contract

The Draw.io renderer is mechanical.

It MAY create Draw.io cells, transform final coordinates to required XML coordinates, emit source/target IDs, terminal ratios, final waypoints, final resolved styles, metadata, and invalid routes in best-effort mode.

It MUST NOT choose placement, route topology, repair geometry, move terminals, choose semantic connector style, derive connector colour from destination-node fill, or omit a planned relationship without a hard fidelity/accounting finding.

## 55. Connector styling

Connector style is planner-owned.

The renderer MUST emit the final resolved planned connector style unchanged.

The emitted `mxCell/@style` string is the fidelity surface. Every planned
connector MUST contain an explicit `strokeColor`; renderer defaults and theme
inheritance MUST NOT supply a missing colour. If a relationship reaches the
renderer without a complete resolved connector style, the renderer MUST emit a
hard fidelity finding and MUST NOT create a fallback connector style.

Raw connector extra-style tokens MAY provide non-authoritative Draw.io
properties. They MUST NOT override planner-owned structured properties such as
stroke colour, stroke width, opacity, arrow settings, dash settings, font
colour or label visibility. The final emitted style MUST contain one effective
value for each planner-owned property, and reconstruction validation MUST parse
the emitted style string rather than relying only on planner metadata.

The planner MUST resolve each connector stroke colour from the resolved fill
colour of its destination node. This produces a per-relationship final
connector style. The renderer MUST emit that planner-owned result mechanically
and MUST NOT perform its own destination lookup or recolouring.

Architecture pages MUST set Draw.io page `adaptiveColors=none` unless an
explicit product setting opts into adaptive colour remapping. Connector and
node colours must not be visually remapped while their stored style values
remain unchanged.

The preserved connector configuration supplies the non-colour connector
properties. The final stroke colour is supplied by the destination node style.
Current preserved configuration has been observed to supply:

- width `1`
- opacity `100`
- end arrow `block`
- no dash pattern

These are configured values, not universal V6 constants.

## 56. Node style and style precedence

Style precedence MUST be resolved once by the planner.

The renderer MUST NOT apply a competing precedence order.

### Still explicitly unspecified

The exact precedence between all node-style override classes, particularly External style versus exact user override, was identified as inconsistent but was not finally decided. It MUST NOT be silently invented or changed.

## 57. Draw.io fidelity

Rendered node geometry MUST reconstruct to planned visible node bounds.

Rendered edges MUST reconstruct to planned source/target, terminals, intermediate waypoints, and styles.

Project-relative and absolute coordinate conversions MUST be reconstruction-tested.

Whether an edge is XML-parented under the root or project container is an implementation detail only if reconstructed geometry remains identical.

## 58. Diagram/project bounds

Final diagram bounds MUST derive from actual transformed scene extents.

The implementation MUST NOT assume `(0,0,width,height)` is always correct unless a deliberate final normalization transform moved the complete scene there.

Translated and negative project geometry MUST be handled correctly.

## 59. Renderer accounting

Every planned physical node and relationship MUST be accounted for in the rendered page.

Omissions are validation failures, not merely informational diagnostics.

Generated IDs MUST have collision detection/guarding.

## 60. Strict and normal validation modes

Strict validation MUST actually be enforced.

Normal/best-effort mode MAY write output with hard-invalid retained relationships but MUST expose findings clearly.

Strict mode MUST make hard-invalid generation ineligible/rejected according to the generation contract, while diagnostics/artifacts may still be written.

## 61. Validation must not be weakened to recover metrics

A validator exposing a real visible/geometric defect MUST NOT be narrowed or disabled merely to restore a previous zero count.

The planner should be changed to satisfy the contract.

## 62. Diagnostics and evidence

Evidence SHOULD make it possible to identify the first stage at which a route or placement becomes invalid.

For routes, retain where practical:

1. abstract cell path;
2. normalized path;
3. runs/turns;
4. lane allocations;
5. boundaries/components;
6. raw physical points;
7. reduced points;
8. final emitted-equivalent polyline;
9. validation findings;
10. `FirstInvalidStage`.

Placement evidence SHOULD expose final layer reason, tree ID/root, positional parent, local tree X, whole-tree translation, final X, and per-layer tree contour.

## 63. Diagnostics must be truthful

Metrics MUST describe the authoritative final plan.

Unacceptable examples include multi-parent count hard-coded to zero, empty sizing provenance where it exists, zero backtracking when final geometry visibly backtracks, or zero shared-segment findings when final geometry shares an interval.

## 64. Determinism

All ordering decisions MUST be deterministic, including parent selection, root/tree order, sibling order, tree packing order, route order, lane order, terminal tie-breaking, External collision resolution, and IDs.

Equivalent input/configuration SHOULD produce equivalent output.

## 65. Tests — final-pipeline philosophy

Tests MUST assert established semantic/geometric rules at the latest meaningful pipeline stage.

Helper-level tests are useful but insufficient when a later pass can undo the invariant.

## 66. Synthetic regression policy

Content Management and the five-project solution are integration/probing targets, not permanent golden fixtures.

Every real-project bug SHOULD be reduced to the smallest deterministic synthetic scenario proving the general rule.

Do not permanently assert mutable live-project IDs/counts/coordinates.

Generated Draw.io files SHOULD NOT be automated visual goldens. XML/geometry/style fidelity tests are appropriate.

## 67. Required placement regression scenarios

Coverage SHOULD include:

### Layers
- ordinary root -> External;
- root -> ordinary -> External;
- shallow and deep independent roots;
- ordinary node above/below/between reserved layers;
- multiple inserted ordinary layers;
- ordinary node never on reserved layer;
- lowest-valid-layer selection;
- root status does not imply top layer.

### Trees
- one parent/one child exact X alignment;
- deep single-child chain;
- parent with multiple children;
- uneven child-subtree widths;
- two independent trees;
- partial shared layers;
- no unrelated node between siblings;
- no cross-tree interleave;
- whole-tree packing adds only whole-tree translation;
- no post-pack individual-node X mutation.

### External
- owner alignment when unobstructed;
- nearest deterministic displacement when blocked.

## 68. Required sizing/terminal regression scenarios

Coverage SHOULD include:

- label-driven width;
- high fan-in/out visible-width expansion;
- odd-span rounding;
- symmetric expansion;
- one/two/multi terminal group centring;
- terminal uniqueness;
- corner inset;
- route-order assignment within centred group;
- endpoint-local vertical X equals terminal X;
- expansion known before tree packing;
- convergence beyond two passes;
- explicit non-convergence failure.

## 69. Required routing regression scenarios

Coverage SHOULD include:

- one authoritative complete path;
- source first movement down;
- destination final movement down;
- destination approach in topology;
- no second routing in boundary/corridor completion;
- deterministic obstacle bypass;
- all intermediate cells retained;
- component construction cannot change topology;
- turn inside authoritative cell;
- no run overshoot;
- A-B-A reversal rejected;
- component-boundary reversal detected;
- final overshoot-return detected;
- legitimate obstacle detour allowed;
- dense crossing represented;
- overlapping intervals receive separate lanes;
- exact shared interval detected;
- under-spaced parallel interval detected;
- node intersection rejected;
- materialisation cannot change endpoints;
- reduction cannot hide reversal.

## 70. Required renderer/validation regression scenarios

Coverage SHOULD include:

- strict mode rejection;
- normal best-effort output;
- missing node/link detection;
- Draw.io node-bound reconstruction;
- terminal ratio reconstruction;
- waypoint fidelity;
- connector-style fidelity;
- destination fill cannot override connector colour;
- project-relative node reconstruction;
- multi-project edge reconstruction;
- non-zero/negative bounds;
- deterministic ID collision detection.

## 71. Current integration acceptance expectations

Current integration expectations are semantic/geometric, not hard-coded counts.

Desired result:

- all relationships represented;
- all routes valid;
- no diagonals;
- no corridor escapes;
- no continuity failures;
- no route-node intersections;
- no shared non-zero segments;
- no under-spaced parallel overlaps;
- no redundant backtracking;
- no endpoint direction failures;
- no duplicate/corner/mis-centred terminals;
- no ordinary-on-reserved violations;
- no lowest-valid-layer violations;
- no shallow-root promotion;
- no single-child or parent-centering violations;
- no tree interleave/atomicity violations;
- no unresolved expansion;
- no renderer omissions or planner/render mismatches.

Historically Content Management has had 340 relationships and the five-project integration 383, but those exact counts are not permanent contract requirements.

## 72. Manual visual review

Automated diagnostics and tests are necessary but do not replace manual review of generated Draw.io diagrams.

A report saying tests pass or all metrics are zero does not prove acceptability if visible output contradicts those metrics.

When manual review exposes a defect:

1. trace/reproduce it;
2. identify the violated contract rule;
3. fix planner/validator behaviour;
4. reduce it to a deterministic regression;
5. regenerate for manual review.

## 73. Change discipline

Before every V6 tranche:

1. read this contract;
2. identify affected sections;
3. identify relevant known violations.

During implementation:

- do not silently alter normative behaviour;
- update this contract deliberately if intended rules change;
- do not add undocumented fallbacks.

After implementation:

1. run tests/build;
2. run integration generation with preserved config;
3. reconcile final artifacts/evidence against this contract;
4. include `V6 Contract Compliance` in the report;
5. list sections affected, rules preserved, rules changed/clarified, and known remaining violations.

Do not automatically begin another tranche after reporting.

## 74. Deliberately unresolved / open decisions

The following have been identified but not fully settled:

1. exact node-style precedence between all override classes, particularly External style versus exact user override;
2. whether Draw.io edges are parented under project containers or root cell `1` — fidelity matters, not a specific XML parent choice;
3. exact defensive maximum iteration count for convergence;
4. whether a future explicit shared-port/shared-routing semantic model should exist — current contract assumes neither;
5. whether the internal pathfinding algorithm remains priority-queue Manhattan search forever — the contract governs behaviour and authority, not one search implementation.

These MUST NOT be silently invented.

## 75. Concise authority map

| Concern | Authoritative owner |
|---|---|
| Semantic dependency graph | Semantic analysis/model |
| Canonical/duplicate projection | Projection |
| Positional parent/tree ownership | Positional ownership stage |
| Reserved role classification | Reserved-layer solver |
| Ordinary Y placement | Bottom-up final-layer solver |
| Visible node sizing | Label + terminal-capacity sizing |
| Local tree geometry | Tree layout |
| Global X placement | Whole-tree contour packing |
| Logical routing cells/grid | Final grid builder |
| Route topology | One abstract route planner |
| Run/lane/crossing allocation | Lane allocator |
| Terminal slot placement | Collective node-edge terminal allocator |
| Footprint expansion | Convergence loop |
| Boundary/component representation | Boundary contract builder |
| Track sizes | Track sizing planner |
| Absolute coordinates | Physical scene compiler |
| Final validity | Final planner/scene validator |
| Final connector/node style | Planner-resolved style |
| Draw.io XML projection | Mechanical renderer |
| Acceptance | Contract + tests + integration diagnostics + manual visual review |

No two stages should independently own the same semantic/geometric decision.

## 76. Historical rationale / recurring failure patterns

Recurring V6 defects that this contract exists to prevent include:

- shallow roots placed on top rows despite only depending on External/lower nodes;
- reserved service types mixed onto the same layer;
- single-child nodes offset by global row packing;
- sibling/subtree interleaving;
- visible width exploding from ordinary routing demand;
- insufficient visible width for actual terminal capacity;
- ports bunched to one side;
- duplicate/corner terminals;
- terminal-to-waypoint diagonals;
- undersized routing tracks;
- boundary reconciliation creating overshoot/backtracking;
- validators reporting zero while visible routes still fail;
- exact/near link overlaps missed by diagnostics;
- renderer recolouring links from destination fill;
- strict validation not actually gating output;
- fixed-pass expansion leaving unconsumed requirements;
- renderer output differing from planner assumptions.

Any future implementation reintroducing these behaviours is almost certainly violating the normative sections above.

# Decision log

This section SHOULD remain concise. Normative sections above are authoritative.

- Grid is authoritative for logical routing; renderer is mechanical.
- Reserved role bands use first-match ordered category semantics.
- External is a dedicated bottom layer.
- Ordinary layers are solved bottom-up around reserved bands; root does not mean top.
- X placement is tree-by-tree; completed trees are globally packed atomically.
- Single-child parent/child X equality is required.
- Visible node width includes terminal capacity and uses odd logical spans.
- Terminal groups are centred/evenly spaced; route geometry assigns relationships to slots.
- Terminal slot owns endpoint-local vertical X.
- Lane sizing uses the same geometry formula as lane coordinates.
- Invalid relationships remain visible in best-effort output.
- Final physical/rendered-equivalent geometry is authoritative for validation.
- Redundant backtracking and shared non-zero intervals are invalid.
- Strict mode rejects hard findings; normal mode is best-effort with explicit diagnostics.
- Connector styling is planner-owned; renderer must not derive colour from destination fill.
- Real projects are integration targets only; permanent regressions use reduced synthetic scenarios.
- Terminal-bearing visible geometry preserves the authoritative terminal edge; renderer reconstruction is the final orthogonality check.
- Same-role parent/child chains use inserted role sublayers; role identity remains unchanged for style and diagnostics.
- Terminal capacity is edge-local and calculated per source/destination side; ordinary route demand never uses total node degree to inflate a visible node.
- Shared bends, invalid endpoint/perpendicular contacts, and under-spaced parallel intervals are diagnostics owned by route/lane allocation and final physical validation, not renderer repairs.
- External affinity diagnostics distinguish genuinely blocked owner-centred placement from stale or avoidable displacement and retain the blocking ownership evidence.
