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

The target planner pipeline is:

1. semantic analysis / semantic input;
2. semantic-to-physical projection;
3. shared reserved-depth reconciliation across the complete selected diagram;
4. project-local dependency-tree placement;
5. fixed project-surround construction;
6. common diagram-grid assembly;
7. logical placement freeze;
8. capability-driven cell-by-cell relationship routing;
9. logical route freeze;
10. collective lane, terminal, bend and crossing allocation;
11. physical track sizing;
12. relative geometry;
13. absolute physical scene compilation;
14. final authoritative validation;
15. mechanical Draw.io rendering;
16. renderer-fidelity validation/accounting.

Placement owns logical node positions, spans, project regions and the final logical grid. Routing owns complete relationship cell paths. After routing, all stages are post-processing arithmetic over frozen logical decisions.

A later stage MUST NOT mutate an earlier authority's decision. If a contradiction is discovered, the owning stage MUST report it as an explicit planning/allocation/validation failure. A later stage MUST NOT trigger convergence, rebuild an earlier stage, reroute, or repair the contradiction locally within the same planner execution.

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

Every selected semantic relationship MUST remain accounted for and MUST map to a resolved physical source/target relationship or to an explicit failed-route diagnostic. Deduplication may change the physical instance targeted, but MUST NOT make the semantic relationship disappear.

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

An ordered, case-insensitive suffix list establishes reserved layers first. Matching is performed against the configured semantic node/name value using first-match semantics.

Matching uses **first-match semantics**.

The default effective reserved category order is:

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

### Pre-construction reservation constraint inspection

Before constructing the final reservation table or any project tree, the planner MUST inspect every selected positional/semantic top-level tree for the minimum natural node depth required by each reserved-group occurrence and each External occurrence. This inspection MAY execute in parallel. It calculates depth constraints only; it MUST NOT construct final placement geometry.

The planner MUST deterministically reduce all inspection results into shared minimum-depth requirements. Those requirements are then used to reconcile the initial reservation table, propagate downstream shifts in increments of `2`, resolve the External maximum required depth, and freeze the result. Actual tree construction begins only after that freeze.

The initial shared reservation table MUST be constructed before any project tree construction:

1. scan the complete selected diagram input and count matches for every configured reserved suffix group;
2. remove configured groups whose match count is zero;
3. preserve the configured first-match and ordering semantics for the remaining groups;
4. append `External` after the remaining configured groups;
5. assign the active reservations to node rows beginning at `1` and increasing by `2`: `1, 3, 5, 7, ...`;
6. treat the rows between those odd-numbered reservations as routing rows.

The resulting initial table MUST then be reconciled against the deterministically reduced requirements from the complete selected diagram input. Required downward shifts MUST preserve reservation order and node/routing parity: when a reservation moves down, every subsequent/lower reservation MUST move as necessary in increments of `2`. For External specifically, if any External node requires a deeper node row than the currently proposed External reservation, the External reservation MUST move to the required parity-aligned depth. External is the final reservation and therefore establishes the bottom shared hierarchy row.

All selected projects MUST use the same frozen reconciled reservation table. Project-local grids MAY use their own local row/column coordinates during construction; this shared table MUST NOT be confused with final diagram-grid coordinates. Tree construction MAY insert padding against the frozen reservations, but MUST NOT mutate the reservation table or trigger another reservation/tree pass.

Reserved-role ordering is a placement constraint, not a reason to alter semantic tree membership. If a dependency's reserved placement is equal to or above its parent and therefore cannot be placed below the parent, its complete dependency subtree MUST be constructed as a detached placement unit and composed to the right/outside of the completed main tree. The semantic relationship remains a normal routing relationship.

## 8. External layer

External dependencies MUST occupy a dedicated bottom layer.

External nodes MUST use the established display form:

- line 1: `[External]`
- line 2: simple name

External FQNs remain metadata, not visible labels.

A sole-owner External dependency SHOULD be horizontally aligned beneath its authoritative owner where unobstructed.

If blocked, the planner MUST choose the nearest deterministic free position and SHOULD record blocker, preferred X, final X, and displacement.

External placement MUST NOT reposition the owning ordinary tree.

External nodes occupy the final shared reserved node layer across the selected diagram.

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

The standalone region MUST sit below External with one routing row between External and the first standalone row. It MUST be packed approximately square, with one logical separation column between adjacent standalone nodes and one routing row between standalone rows. Normal odd-span sizing applies to standalone nodes.

Standalone layout MUST NOT influence ordinary tree placement.

The complete standalone grid is an atomic placement block. Its internal node
positions are frozen before project/diagram composition; dependency-tree placement and
standalone-grid placement MUST NOT mutate one another internally. Any collision
is resolved by translating complete regions/blocks, never by reassigning one
standalone node through the hierarchy placement authority.

### Fixed project surround structure

Every project region MUST have exactly two logical tracks on each side:

```text
project interior
-> inner added track: project boundary/header track
-> outer added track: general routing track
-> outside
```

This means two rows above, two rows below, two columns left and two columns right. The inner added track is the project boundary/header surface; it is not an additional general-routing track. The outer added track is general routing space and permits bends. This surround is fixed structural topology and MUST NOT be expanded by routing demand.

Project border cells MUST permit straight passthrough only and MUST NOT contain bends. Header-text cells MUST be blocked. Non-text header cells MUST permit straight passthrough only and MUST NOT contain bends. The outer surrounding routing track MUST permit general routing, including bends.

## 10. Recursive tree-grid depth construction

Temporary tree grids MUST construct dependency depth recursively. There is no separate global ordinary-Y solver or lowest-valid-layer solver that assigns every ordinary node before tree construction.

Temporary tree grids use alternating logical rows with explicit local parity:

```text
local row 0 = node row
local row 1 = routing row
local row 2 = node row
local row 3 = routing row
...
```

The root begins on local row 0. When a tree is composed into its project grid, the project-local parity is offset explicitly:

```text
project row 0 = routing row
project row 1 = node row
project row 2 = routing row
project row 3 = node row
...
```

Tree-local, project-local and final diagram-grid row identifiers are different coordinate domains. They MUST NOT be conflated. Once project grids are composed into the common diagram grid, routing uses diagram-grid coordinates only.

An ordinary node's depth is the recursive depth implied by its dependency-tree construction. Independent trees retain independent depths and MUST NOT be promoted to match unrelated deeper trees.

## 11. Reserved-depth padding during tree construction

The shared reserved-depth table is reconciled before project tree construction. Every selected project uses the same frozen reserved depths.

When a node's recursive tree depth would place it above its reserved depth, tree construction MUST insert node/routing-row padding so the node lands on its frozen reserved depth. Reservation movement and downstream `+2` propagation are completed by the pre-construction reconciliation phase in Section 7; construction MUST NOT move a reservation, propagate changes to later reservations, trigger another reservation pass or rebuild.

External MUST remain the final shared reserved node layer. Ordinary nodes retain their recursive tree depth and MUST NOT be assigned to reserved rows by a separate global layer solver.

## 12. Reserved-order detached placement

If a dependency's reserved placement is equal to or above its parent and therefore cannot be placed below the parent, the semantic dependency tree MUST remain unchanged.

The dependency subtree MUST instead be recursively constructed as a detached placement unit. It MUST be excluded from direct-child centring and normal sibling composition, then composed to the right/outside of the completed main tree with one routing/separation column between placement units. Multiple detached units MUST be appended in analyser/FIFO order.

## 13. Parent/child Y ordering

For every authoritative positional parent edge that remains within the recursively composed main placement tree:

- parent MUST be visually above child;
- child MUST be visually below parent;
- direct-child placement MUST follow the recursive tree-grid row structure;
- the normal visual invariant is evaluated using the final node rows in that composed tree.

For a dependency relationship whose subtree was detached because its reserved placement is equal to or above its semantic parent:

- the normal parent-above-child visual invariant does not apply;
- semantic ownership and the dependency relationship remain unchanged;
- the detached subtree remains at its correct reserved placement;
- routing later connects the two physical placements.

Reserved ordering is handled by padding or detached placement units. It MUST NOT be resolved by changing reserved depth, changing semantic parentage, forcing detached Y placement below the source, or mutating the semantic dependency tree.

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

Ordinary routing demand MUST NOT inflate the authoritative logical node footprint. Physical clearance MAY require larger row/column extents after routing, but any physical-only clearance envelope has no logical routing authority, MUST NOT block cells, MUST NOT reserve endpoint corridors, and MUST NOT alter pathfinding or node topology.

## 16. Odd logical-span sizing

Node horizontal logical span MUST be odd and MUST be calculated before tree construction from a fixed configured base cell width, not from post-routing physical track extents.

Nodes begin at logical span 3. The planner MUST choose the smallest odd span satisfying:

`span * configuredBaseCellWidth >= max(visible label/text requirement, maximum top/bottom terminal capacity requirement, configured minimum node width/margins)`

When a physical/terminal width requires an even logical span, round up to the next odd span.

Examples:

- required 4 cells → allocate 5;
- required 5 cells → allocate 5;
- required 6 cells → allocate 7.

Expansion MUST be symmetric around the node centre and preserve the authoritative centre cell/centreline.

`configuredBaseCellWidth` is a fixed pre-routing sizing assumption. Later routing pressure MAY enlarge physical row/column extents, but MUST NOT change logical node spans, centre cells or positions.

Node sizing MUST be finalized before local tree construction and project-grid composition.

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

After shared reserved depths and pre-routing logical node spans are frozen, recursive tree-grid construction MUST determine local tree X/Y geometry. X placement MUST be performed **tree by tree**.

The planner MUST NOT globally pack all nodes on each visual row before tree construction.

For each placement root:

1. build the entire tree in local X coordinates;
2. recursively construct child subtrees;
3. calculate the completed tree's recursive subtree contours;
4. freeze internal tree geometry;
5. return the completed temporary top-level tree grid as an atomic placement unit.

Independent top-level trees MAY be constructed in parallel against the same frozen reserved-depth table. The planner MUST wait for all tree construction results, then pass them to project-grid composition in analyser/FIFO order. There is no sequential find-free-space, whole-tree translation, or place-next-tree authority in this stage; atomic project-grid composition is defined by Section 25.

Unrelated trees MUST NOT influence the local geometry of the tree currently being built.

## 19. Recursive subtree construction

For each parent:

1. recursively construct every immediate child subtree;
2. determine the width/contour required by each child subtree;
3. pack immediate child subtree placement units contiguously with exactly one routing/separation column between adjacent units;
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

Project-grid or diagram-grid composition MUST NOT break this alignment.

## 21. Multiple-child parent centring

For a parent with multiple immediate children, the parent MUST be centred over the immediate-child group.

The centring reference is the immediate child group, not the entire descendant envelope. For one direct child, `ParentCentreX == ChildCentreX`. For multiple direct children, `ParentCentreX` is the midpoint of the leftmost and rightmost direct-child centres. Descendant/subtree bounds determine required placement space only; they MUST NOT determine the parent centreline.

Child-subtree widths determine sibling-subtree separation, but do not redefine parent-centre semantics.

## 22. Sibling and subtree locality

Immediate siblings MUST remain a contiguous visual group.

Unrelated nodes MUST NOT be inserted between siblings.

Unrelated trees MUST NOT be inserted between sibling subtrees.

Unused X space inside one tree MUST NOT be filled by extracting unrelated individual nodes from another tree.

Tree readability takes priority over maximum compactness.

## 23. Tree atomicity

Once a local tree has been constructed, its internal X geometry is frozen.

Project-grid composition MUST place the completed temporary tree grid as one
atomic unit. It MUST NOT interleave or independently reposition nodes inside
that unit.

There MUST NOT be any unexplained per-node X delta after tree construction.

## 24. Recursive subtree contours

Complete child subtree width and contour information MUST be used during recursive construction to reserve the horizontal space required by each child placement unit.

This information is local to recursive tree construction. It MUST NOT become a global per-layer interlocking authority and MUST NOT permit unrelated top-level trees to use unused space inside one another.

## 25. Atomic top-level tree composition

Once a top-level temporary tree grid has been constructed, it is an atomic rectangular placement unit. Its internal node rows, routing rows, columns, spans and relative positions are frozen.

Completed top-level tree grids MUST be inserted into their project grid in analyser/FIFO order with exactly one routing/separation column between adjacent units. Unrelated top-level trees MUST NOT interleave, interlock by shared-layer contour, or extract individual nodes into another tree's unused space.

Independent tree construction MAY execute in parallel, but composition order MUST remain analyser/FIFO order.

## 26. Grid construction from the beginning

The diagram grid MUST be created before project tree construction as the overall working grid.

Temporary tree grids and project-local grids MUST then be created and populated directly with logical node and routing rows/columns. Completed tree grids are composed into project grids, and completed project grids are composed into the diagram grid with the fixed project surrounds.

After project composition, the completed diagram grid becomes the authoritative frozen routing surface. This is not a layout-then-derived-grid process. No later stage may create structural routing rows/columns or alter logical node spans/positions.

## 27. Logical placement and route freeze

Once placement completes, logical node positions, spans, project regions and the diagram grid are frozen. Once routing completes, every relationship's ordered logical route-cell sequence is frozen.

No post-routing stage MAY add, remove, replace, reorder or otherwise alter a relationship's logical route-cell sequence. Lane allocation, terminal allocation, bend/crossing calculation, physical sizing and materialisation MAY derive physical requirements from the frozen paths, but MUST NOT request rerouting.

Lane allocation MAY increase physical row/column requirements, but MUST NOT add or remove logical rows/columns, change logical node spans/positions, or request another logical route. If a frozen path cannot be physically represented, the result is an explicit planning/validation failure.

## 28. One authoritative route topology

There MUST be one authority for abstract route topology: the capability-driven cell-by-cell route planner over the frozen common diagram grid.

A later helper MUST NOT independently select another row/column route after topology has been chosen.

Destination approaches, endpoint handoffs and project-boundary crossings MUST be represented by the authoritative cell path and its provenance rather than by a second hidden router or later repair stage.

## 29. Abstract route requirements

Every selected semantic relationship route MUST be deterministic, orthogonal, complete, and represented as a full ordered sequence of traversed cells/steps between its resolved physical source and target placements.

Source first movement MUST be downward.

Destination must ultimately approach the target from above and enter downward.

All traversed intermediate cells MUST remain represented in route provenance.

Every consecutive route cell MUST be orthogonally adjacent. No cell skipping or implicit cell jump is allowed.

### Authoritative cell-capability matrix

Cell capability is the sole logical topology obstacle authority. Capability is evaluated against the requested traversal, not as a single `IsRoutable` Boolean. For each candidate step the router MUST evaluate the cell together with its intended entry direction and intended exit direction.

| Cell kind | Capability | Permitted traversal |
|---|---|---|
| Occupied node-footprint cell | Blocked | No unrelated route traversal. A source or destination node cell may appear only as the corresponding route endpoint; it is not a pass-through cell. |
| Normal routing-row cell | General routing | Horizontal traversal, vertical traversal and bends. |
| Empty node-placement-row cell | Vertical passthrough only | Vertical straight traversal only; horizontal traversal and bends are prohibited. |
| Project boundary cell | Straight passthrough only | Permitted straight traversal; bends are prohibited. |
| Project header non-text cell | Straight passthrough only | Permitted straight traversal; bends are prohibited. |
| Project header text cell | Blocked | No route traversal. |

Examples are normative:

- an empty node-row cell with vertical entry and vertical exit is legal;
- an empty node-row cell with horizontal entry and horizontal exit is illegal;
- an empty node-row cell with a vertical-to-horizontal turn is illegal;
- a project boundary or non-text header cell permits its allowed straight traversal but rejects a turn;
- a header-text cell blocks traversal;
- a normal routing-row cell permits turns.

The route MUST use cells whose capabilities permit the requested traversal. Pathfinding MUST fundamentally care about cell capability; route occupancy, project ownership and endpoint ownership MUST NOT become additional obstacle systems.

For an ordinary immediate-child relationship, when the target centre cell is exactly `[source centre column, source node row + 2]`, the authoritative route MUST use the direct vertical path:

```text
source node cell
-> intervening routing-row cell
-> target node cell
```

The source and target node cells in this representation are endpoint cells, not pass-through traversal. The intervening routing-row cell is the only ordinary traversed cell. This three-cell path is subject to the same capability and orthogonal-adjacency rules. The general obstacle-routing process MUST NOT be invoked for this direct immediate-child case.

### General downward route construction

For a target below the source, after the direct immediate-child shortcut has been considered, the authoritative route MUST be constructed as:

```text
source
-> leave downward into the routing row below the source
-> move horizontally as required toward the intended vertical line
-> descend cell-by-cell
-> if the next vertical cell is unavailable, remain on the current routing row and use the deterministic +/-2 continuation search
-> repeat until the routing row immediately above the target
-> move horizontally to the target centre column
-> enter the target downward
```

Every horizontal and vertical movement in this sequence MUST obey the requested-traversal capability rules and retain every traversed cell.

### General upward route construction

For a target above the source, the authoritative route MUST be constructed as:

```text
source
-> leave downward into the routing row below the source
-> move horizontally outside the source logical footprint
-> ascend from that escaped column
-> use the same capability checks and deterministic +/-2 continuation search while ascending
-> remain close to the current/source vertical line rather than taking an arbitrary global detour
-> reach the routing row immediately above the target
-> move horizontally to the target centre column
-> enter the target downward
```

An upward route MUST NOT immediately reverse through the source node or its footprint. The mandatory horizontal escape MUST occur on a general routing row before ascent begins. The direct immediate-child shortcut remains the special case handled before this general process.

## 30. Blocked preferred routes

The generator MUST still account for a relationship when a preferred corridor is blocked.

The route planner SHOULD deterministically route around blockers using the authoritative routing grid/cells. For descending and ascending travel, a blocked vertical continuation MUST NOT enter the blocked/node row. It remains on the current general-routing row and searches both horizontal directions in repeated two-column logical-ordinal candidate increments. The side requiring fewer increments to obtain a legal vertical continuation wins; equal distances use left-first.

The `+/-2` value selects candidate continuation columns; it does not permit a route-cell jump. Every horizontal movement between the current column and a selected candidate column MUST contain each intervening orthogonally adjacent routing cell in route provenance, for example `[x] -> [x + 1] -> [x + 2]`.

A route MUST NOT be dropped simply because the preferred corridor is unavailable.

If no legal path exists on the frozen grid, retain the failed relationship attempt, emit a hard route-planning diagnostic, and do not mutate placement, add rows/columns or invoke compiler repair. Normal/best-effort output retains the invalid relationship and sufficient attempted geometry for visible accounting; strict output rejects the hard-invalid result.

## 31. Cross-project routing

Cross-project relationships MUST use exactly the same common diagram-grid routing algorithm as same-project relationships.

The logical route for a cross-project relationship is entirely expressed in common diagram-grid coordinates. Project-local grids are composed into that diagram grid before routing; project-relative and absolute project transforms are later mechanical geometry transforms only.

Cross-project boundary crossings MAY be recorded as provenance and ownership metadata, but MUST NOT invoke a separate transition router, transition-row selector or compiler-created transition geometry. Cross-project routing MUST remain orthogonal and subject to the same final validation as same-project routes.

Project movement/translation MUST carry project-owned geometry consistently.

## 32. Source/destination endpoint geometry and component representation

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

Endpoint components are derived only after logical route freeze from the frozen route-cell sequence, terminal assignments, lane assignments and final physical row/column dimensions. They are renderer-independent physical representations, not a second topology authority. They MUST NOT select, replace, reorder or otherwise modify the logical route-cell sequence.

## 33. Terminal slot ownership

The terminal slot owns the endpoint-local vertical X coordinate.

`SourceEndpointVertical.X == SourceTerminal.X`

`DestinationEndpointVertical.X == DestinationTerminal.X`

The ordinary route lane MAY use a different X.

If so, an explicit orthogonal planner-owned handoff connects the endpoint-local vertical to the ordinary route.

An endpoint-local handoff MAY connect the assigned terminal X to the physical lane represented by the endpoint-adjacent authoritative route cells, but it MUST remain inside those cells' authorised physical routing envelope. It MUST NOT create another logical route around an obstacle. Endpoint handoffs solve physical alignment only; they do not solve topology.

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

Relationship-to-slot assignment is determined by route geometry with this precedence:

1. primary ordering authority: directional group, ordered left-to-right as links travelling left, then links travelling down, then links travelling right;
2. secondary ordering within each directional group: deterministic positional/geometric order;
3. stable deterministic identity/order tie-break.

The resulting vertical offsets at the first direction change MUST preserve that grouping and MUST avoid link crossings. The established source/destination X-order statements are secondary ordering within a directional group, not an alternative global ordering authority: outgoing source terminals use downstream target/departure-route X and incoming destination terminals use source/approach-route X.

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

If adjacent components cannot reconcile on the frozen topology, the result MUST be an explicit planning, allocation or validation failure. Boundary/component construction MUST NOT request a rebuilt route or invoke a later topology repair.

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

Placement evidence SHOULD expose final layer reason, tree ID/root, positional parent, local tree X, atomic project-placement offset, final X, and recursive subtree contour data. Contour diagnostics are evidence from recursive construction only; they MUST NOT be interpreted as permission for a global helper to interlock completed top-level trees by layer.

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
- recursive single-child depth with alternating node/routing rows;
- recursive multi-level tree depth;
- shallow and deep independent trees retain independent depths;
- zero-match configured reserved groups are removed;
- initial active reservations are exactly odd node rows `1, 3, 5, ...` after zero-match removal and External append;
- shared reserved depths across selected projects;
- pre-construction depth inspection calculates constraints without constructing placement geometry;
- reserved-depth padding in alternating node/routing increments;
- when an earlier reservation moves down, every downstream reservation shifts by `2` as required;
- External moves down when an External dependency requires a deeper node row;
- ordinary nodes remain on their recursive tree depth;
- reserved-order conflict creates a detached placement unit;
- External occupies the final shared reserved node layer;
- standalone region sits below External with a routing row between.

### Trees
- one parent/one child exact X alignment;
- deep single-child chain;
- parent with multiple children;
- uneven child-subtree widths;
- two independent trees;
- direct-child-centre formula with uneven widths;
- exactly one separation column between adjacent sibling units;
- completed top-level tree grids compose atomically in analyser/FIFO order;
- parallel tree completion order cannot change FIFO project composition;
- no top-level interleave or contour interlocking;
- completed tree grids are placed only as atomic project-grid units;
- no post-freeze individual-node X mutation.

### External
- owner alignment when unobstructed;
- nearest deterministic displacement when blocked;
- External final-layer placement;
- standalone approximate-square placement with one row/column separation.

### Routing-grid assembly
- diagram grid exists before project/tree construction;
- temporary tree and project grids compose into the diagram grid;
- fixed two-track project surround on every side;
- no route-created logical rows or columns after placement freeze.

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
- pre-routing span requirements consumed before tree packing;
- post-routing lane pressure cannot change logical spans or trigger placement rebuilds;
- physical sizing may enlarge track extents without changing logical topology.

## 69. Required routing regression scenarios

Coverage SHOULD include:

- one authoritative complete path;
- empty node-row cell permits vertical passthrough;
- empty node-row cell rejects horizontal traversal;
- empty node-row cell rejects bends;
- project boundary permits straight passthrough and rejects bends;
- non-text header cell permits straight passthrough and rejects bends;
- header-text cell blocks traversal;
- normal routing row permits turns;
- normal downward route construction follows routing-row departure, horizontal alignment, cell-by-cell descent and final target alignment;
- final horizontal target alignment occurs on the routing row immediately above the target;
- upward routes leave downward, escape outside the source footprint, then ascend;
- upward routes do not immediately reverse through the source node/footprint;
- source first movement down;
- destination final movement down;
- direct immediate child uses the three-cell vertical path;
- destination approach in topology;
- no second routing in boundary/corridor completion;
- deterministic obstacle bypass;
- `+/-2` candidate search retains every intervening horizontal cell;
- obstacle bypass works identically for upward and downward vertical travel;
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
- no reserved-depth padding violations;
- no independent-root promotion;
- no single-child or parent-centering violations;
- no tree interleave/atomicity violations;
- no unresolved pre-routing span requirement;
- no post-freeze logical row, column, span or position mutation;
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
3. whether a future explicit shared-port/shared-routing semantic model should exist — current contract assumes neither;
4. whether the internal pathfinding algorithm remains priority-queue Manhattan search forever — the contract governs behaviour and authority, not one search implementation.

These MUST NOT be silently invented.

## 75. Concise authority map

| Concern | Authoritative owner |
|---|---|
| Semantic dependency graph | Semantic analysis/model |
| Canonical/duplicate projection | Projection |
| Positional parent/tree ownership | Positional ownership stage |
| Shared reserved depths | Reservation reconciliation |
| Pre-routing node span | Label, terminal-capacity and configured-base-cell sizing |
| Recursive tree-grid X/Y geometry | Recursive tree-grid construction |
| Detached placement units | Tree placement/composition |
| Project composition | Deterministic project-grid assembly |
| Diagram logical grid | Diagram-grid assembly during placement |
| Route topology | Single capability-driven cell router |
| Run/lane/turn/crossing allocation | Collective post-route allocator |
| Terminal assignment | Post-route terminal allocator |
| Physical track extents | Track sizing planner |
| Absolute geometry | Mechanical physical compiler |
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

- The new planner algorithm supersedes prior V6 behaviour wherever the two differ.
- Semantic relationships remain accounted for through resolved physical source and target instances.
- Reserved groups are counted across the complete selected diagram, zero-match groups are removed, remaining groups preserve configured order, and active reservations begin at odd node rows `1, 3, 5, ...` with External appended last.
- Reservation constraints are inspected from every selected top-level tree before geometry construction; inspection may run in parallel, but requirements are reduced deterministically before the table is reconciled and frozen.
- Reserved depth shifts are reconciled across the complete selected diagram in increments of `2`; External moves to the required parity-aligned maximum depth and establishes the shared bottom reservation.
- The reconciled reservation table is frozen before parallel project/tree construction and is shared by every selected project.
- The project surround is fixed at exactly two logical tracks on every side: inner boundary/header track, then outer general-routing track.
- Logical node spans are calculated before tree construction from a fixed configured base cell width and remain odd, symmetric and frozen.
- Tree construction is analyser/FIFO ordered; independent trees may execute in parallel and must merge in analyser/FIFO order.
- Independent tree completion order cannot affect atomic FIFO project-grid composition.
- Parent centring uses direct-child centres; child subtree bounds determine required space but never the parent centreline.
- Reserved-order conflicts use detached placement units, not inserted same-role sublayers.
- External occupies the final shared reserved layer; standalone nodes occupy a separate approximately square region below External with explicit routing separation.
- The common diagram grid is authoritative for both same-project and cross-project routing.
- Route pathfinding is capability-driven. Route occupancy, project ownership and endpoint ownership are not additional pathfinding obstacles.
- Cell capabilities are traversal-specific: node footprints and header text block; empty node rows permit vertical passthrough only; routing rows permit general traversal; boundaries and non-text headers permit straight passthrough only.
- Tree-local row parity is node/routing from local row 0, project composition applies an explicit offset, and final routing uses only common diagram-grid coordinates.
- An immediate child on the next node row and the same centre column uses the direct three-cell vertical path through the intervening routing row.
- General downward routes align on the routing row above the target before entering downward; upward routes depart downward, escape outside the source footprint, then ascend without immediate reversal.
- Every consecutive logical route cell is orthogonally adjacent; no cell skipping is permitted.
- Obstacle bypass searches both logical `+/-2` directions on general routing rows; nearest valid continuation wins and left is the equal-distance tie-break.
- Failed paths remain accounted for with explicit hard diagnostics; normal mode retains attempted invalid output and strict mode rejects it.
- Logical route-cell sequences freeze after routing. Lane allocation, terminal allocation, sizing and materialisation may never request rerouting or alter logical topology.
- Terminal groups are centred and evenly spaced using configured edge inset and port spacing; directional grouping is left, down, right, then positional order within each group, then stable tie-break.
- Endpoint-local handoffs may solve physical terminal/lane alignment only within the authorised endpoint-cell envelope and may not create topology.
- Lane sizing uses the same geometry formula as lane coordinates.
- Final physical geometry is validated before safe monotonic collinear simplification.
- Invalid relationships remain visible in best-effort output.
- Strict mode rejects hard findings; normal mode is best-effort with explicit diagnostics.
- Connector styling is planner-owned; the renderer emits the resolved style mechanically.
- Real projects are integration targets only; permanent regressions use reduced analyser-shaped synthetic scenarios.
