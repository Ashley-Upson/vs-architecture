# Architecture V7: As-Built Technical Specification

Status: implementation description at committed HEAD `10f1997789c9a5139a5259c6966683d021325e3e`.

This document describes the implementation as it exists. “AS IMPLEMENTED” is authoritative for this document. “CONTRACT EXPECTATION” records the comparison point from `docs/architecture-v6-contract.md`; it is not a restatement of the implementation.

The worked example is the audited CMS run using five source projects and the preserved configuration at `C:\Users\Ash\Documents\codex-artifacts\architecture-user-settings.json`.

## 1. End-to-end authority map

The production orchestration is `ArchitectureV7ProductionGenerationService.GenerateAsync`. After semantic analysis, its actual sequence is:

| Stage | Authority | Input | Output / freeze |
|---|---|---|---|
| Semantic analysis | `IArchitectureAnalyser.AnalyseAsync` | selected Roslyn projects and analysis settings | `ArchitectureDiagram` |
| Projection | `ArchitectureV7PhysicalProjectionStage.Project` | semantic diagram, `ArchitectureV7ProjectionPolicy` | `ArchitectureV7PhysicalProjectionResult`; projection fingerprint |
| Positional ownership | `ArchitectureV7PositionalOwnershipStage.Resolve` | projection | `ArchitectureV7PositionalOwnershipResult`; ownership fingerprint |
| Pre-placement sizing | `ArchitectureV7PreRoutingNodeSpanSizer.Size` | ownership, `ArchitectureV7PrePlacementConfiguration` | immutable node span requirements; sizing fingerprint |
| Reservation inspection | `ArchitectureV7ReservedRoleConstraintInspector.Inspect` | ownership, pre-placement configuration | natural depths and reservation requirements |
| Reservation reconciliation | `ArchitectureV7ReservationReconciliationStage.Reconcile` | inspection | frozen reservation table and fingerprint |
| Recursive trees | `ArchitectureV7RecursiveTreeGridStage.Build` | sizing, frozen reservations | immutable top-level `ArchitectureV7TreeGrid` values |
| Project/common composition | `ArchitectureV7ProjectCompositionStage.Compose` | trees, projection, ownership, sizing | `ArchitectureV7PlacementFreeze`; placement accounting occurs here |
| Logical routing | `ArchitectureV7LogicalRelationshipRoutingStage.Route` | placement freeze, projection | `ArchitectureV7LogicalRouteFreeze`; route fingerprint |
| Maximal runs and allocation | `ArchitectureV7CollectivePostRoutingAllocationStage.Allocate` | placement, logical routes, allocation configuration | immutable runs, lanes, terminals, approaches, handoffs, bends, crossings, demands |
| Physical sizing and compilation | `ArchitectureV7PhysicalSceneCompilationStage.Compile` | placement, routes, allocation, sizing requirements, physical configuration | physical scene freeze, or incomplete scene with diagnostics |
| Acceptance | `ArchitectureV7FinalAcceptanceValidationStage.Validate` | all frozen products and scene | `ArchitectureV7AcceptanceReport` |
| Draw.io rendering | `ArchitectureV7MechanicalDrawioRenderer.Render` through the production service/composer | accepted physical scene and diagram metadata | `DrawioPage` |
| Renderer fidelity | `ArchitectureV7ProductionGenerationService.ValidateRendererFidelity` | Draw.io page and physical scene | fidelity diagnostics |
| Evidence export | production result export factory and `ArchitectureV7RoutingEvidenceStage` | frozen products and diagnostics | JSON/evidence files |

Freeze boundaries are the immutable result objects returned by projection, ownership, sizing, reservation reconciliation, recursive placement/composition, routing, allocation, and scene compilation. Later stages receive those products; there is no public API in later stages to change an earlier freeze. The physical compiler does, however, rediscover some frozen allocation facts instead of indexing them.

## 2. Semantic input

`IArchitectureAnalyser` emits an `ArchitectureDiagram` containing:

- `Projects`, each with stable project `Id`, name, nodes, and semantic links;
- semantic nodes with identity, name/full name, kind, project ownership, standalone/external classification, and interface/type metadata where supplied by Roslyn;
- semantic relationships with stable semantic IDs, source/target semantic node IDs, source/target project IDs, and relationship kind;
- `ExternalNodes`, represented separately from project-owned nodes;
- the selected relationship set after analyser selection/omission rules.

The V7 stages do not infer a second semantic graph. Projection consumes the analyser's node and link collections. Projection sorts its own inputs by stable IDs; no placement stage is allowed to repair semantic omission.

In the audited CMS input:

| Semantic domain | Count |
|---|---:|
| Source projects | 5 |
| Project semantic nodes | 387 |
| External semantic nodes | 25 |
| Semantic nodes total | 412 |
| Semantic relationships | 383 |

“Source project” means a selected project returned by the analysis boundary, not a project discovered later by V7. The five projects are the five projects represented in the audited solution's analysed diagram; the source solution contains additional project context, but only these five enter the selected diagram model.

## 3. Physical projection and duplication

`ArchitectureV7PhysicalProjectionStage.Project` first reads project nodes and then external nodes. A canonical physical node is created for each semantic node with a `physical:` ID derived from the semantic ID. Physical nodes retain `SemanticNodeId`, `ProjectId`, name/full name, kind, `IsExternal`, `IsStandalone`, and `ProjectionMode`.

When configured duplicate branches are enabled, `ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches` is selected. Duplicate candidates are created only when the configured duplicate pattern rules match repeated target use. A duplicate ID has the form:

```text
physical:<semantic-id>:duplicate:<ordinal>
```

`DuplicationProvenance` records the semantic node, semantic link, branch source physical node, branch ordinal, and reason. Physical relationships reference the selected physical endpoints and retain the semantic relationship ID. Ordering is deterministic by physical IDs and configured branch order.

The current run has:

| Projection product | Count |
|---|---:|
| Canonical physical nodes | 412 |
| Configured duplicate physical nodes | 31 |
| Final physical nodes | 443 |
| Physical links | 383 |

All 31 duplicates are `IEventHub` external branches. The configured high-noise pattern list includes `*Hub`, which matches `IEventHub`; repeated target uses therefore produce 31 duplicate external physical nodes. The 25 canonical External semantic nodes become 25 canonical External physical nodes. The 31 additional `IEventHub` branches make 56 External physical nodes.

Projection has no placement knowledge: it does not select rows, columns, spans, positional parents, or routing cells.

## 4. Positional ownership

`ArchitectureV7PositionalOwnershipStage.Resolve` converts projected physical links into at most one positional parent per physical node. It chooses among incoming semantic parents using deterministic ownership rules, preferring same-project relationships and then the stable ordering established by the projection/link IDs. A node with no selected parent is a positional root. Additional semantic parents remain physical routing relationships and are not removed.

The result contains one `ArchitectureV7PositionalOwnershipDecision` for every physical node. The decision carries the physical node ID, optional positional parent physical ID, and provenance/fingerprint data. A physical node therefore has zero or one positional parent, while it may still have any number of semantic/routing parents.

Standalone and External classification comes from the semantic/projection model. Ownership does not pack standalone nodes and does not place External nodes.

The distinction is:

- semantic parent: relationship endpoint implied by the analyser;
- positional parent: the single parent used to construct a recursive tree;
- routing relationship: every projected physical link, including links whose source/target are not positional parent/child.

## 5. Pre-placement node sizing

`ArchitectureV7PreRoutingNodeSpanSizer.Size` independently calculates each physical node's requirements before any row, column, or coordinate exists.

For a node:

```text
visibleLabel = node.IsExternal ? "[External]\n" + node.Name : node.Name
labelRequirement = max(line.length) * LabelCharacterWidth + LabelHorizontalMargin
topRequirement = topCount == 0 ? 0 : 2 * TerminalInset + (topCount - 1) * TerminalPortSpacing
bottomRequirement = bottomCount == 0 ? 0 : 2 * TerminalInset + (bottomCount - 1) * TerminalPortSpacing
requiredWidth = max(ConfiguredMinimumWidth, labelRequirement, topRequirement, bottomRequirement)
span = max(3, ceil(requiredWidth / ConfiguredBaseCellWidth))
if span is even: span++
```

Current effective values include `BaseCellWidth=100`, `NodeHeight=80`, minimum node width 200, label character width 8, label horizontal margin 20, terminal port spacing 25, and terminal inset 20 as supplied to the V7 configuration mapping.

The implementation independently groups physical links by destination and source physical ID. Duplicate physical nodes therefore receive only the link demand attached to that physical duplicate. Routing distance, lane count, crossing count, physical column width, and later allocation demand cannot affect the logical span.

The centre is `column + (span - 1) / 2` when placement later freezes the node. Sizing itself chooses no coordinates.

## 6. Reserved-layer reconciliation

`ArchitectureV7ReservedRoleConstraintInspector` resolves configured reserved patterns in order using case-insensitive suffix matching. The first matching configured role wins. Each selected physical node contributes a natural positional depth; required node row in reservation coordinates is:

```text
requiredNodeRow = naturalDepth * 2 + 1
```

External nodes are inspected separately using the same depth-to-node-row conversion and an External reservation requirement.

`ArchitectureV7ReservationReconciliationStage` removes zero-match non-External groups, preserves configured order, appends External last, and assigns odd frozen node rows. A group is shifted downward in increments of two when its required row or a prior group's propagated row requires it. The table is diagram-wide: one table is shared by every selected project.

Current reservation table:

| Pattern | Matches | Frozen reservation node row |
|---|---:|---:|
| `*AggregationService` | 1 | 5 |
| `*CoordinationService` | 6 | 7 |
| `*OrchestrationService` | 20 | 11 |
| `*ProcessingService` | 36 | 13 |
| `*Service` | 39 | 15 |
| External | 56 | 17 |

`*ManagementService` matched zero and is absent from the frozen table. Reservation coordinates are node-layer coordinates, not final common-grid coordinates. For ordinary project trees, composition applies:

```text
finalRow = project.InteriorOriginRow + localTreeRow + 1
```

The project interior begins at row 2. The recursive tree's local rows are converted through the project interior origin and one interior offset. External placement is different:

```text
externalFinalRow = frozenExternalNodeRow * 2 + 3
                 = 17 * 2 + 3
                 = 37
```

Thus “External frozen row 17” becomes final row 37 because the composition stage maps the reservation node-layer row into the alternating common-grid row domain and adds the project/common-grid offset.

Rows 17, 25, 29, and 33 are not additional reserved groups. They are recursive continuation rows produced by ordinary trees whose local natural depth or detached/descendant geometry reaches those depths. Row 17 is also the project-local node-bearing row that corresponds to the deepest ordinary continuation before the External row; row 25, 29, and 33 are deeper project-tree continuations. External itself is placed at row 37.

## 7. Recursive tree-grid placement

`ArchitectureV7RecursiveTreeGridStage.Build` constructs each positional tree independently. It recursively builds child units before the parent. Child order is the deterministic analyser/projection FIFO order preserved by the child map. A child result is a complete placement unit containing node placements, local bounds, width/height, and detached units.

Sibling units are packed left-to-right with exactly one logical separation column. The parent is then placed using direct-child node centres:

```text
one child:     parentCentre = childCentre
multiple:      parentCentre = midpoint(leftmostDirectChildCentre,
                                      rightmostDirectChildCentre)
```

The midpoint is the integer logical centre used by the implementation. Descendant bounds increase the required subtree width and therefore affect where sibling units can be placed. Descendant bounds do not replace the direct-child-centre calculation.

Rows are local alternating rows:

```text
local node row  = depth * 2
local routing row = odd row between node rows
```

Reserved nodes are padded with alternating node/routing rows until their frozen reservation row is reached. Node spans are read from the frozen sizing result and are not changed by tree construction.

Worked placement behavior:

- one child: the parent centre equals the child's node centre, even if the child subtree has wide descendants;
- multiple children: leftmost and rightmost direct child centres define the parent centre; intermediate descendants only consume width;
- unequal subtree widths: each child unit retains its full width, and one separator column is inserted between units; the parent is still centred on direct child node centres, not on the outer subtree bounds.

The stage asserts tree-local non-overlap and required separation before composition. It does not run a later free-space or collision-repair pass.

## 8. Detached units

A dependency becomes detached when its frozen reserved row is equal to or above the row at which ordinary downward placement would put it relative to its positional parent. The semantic relationship and positional ownership remain unchanged. The dependency subtree is recursively built as a detached unit, excluded from direct-child centring and ordinary sibling composition, then appended to the right/outside of the main unit in deterministic order.

The internal build result distinguishes:

- `Complete`: the main completed tree unit;
- `Detached`: detached placement units collected during recursive construction.

Nested detached units are aggregated into the enclosing completed result. Commit `9582e4e` repaired the prior nested-detached duplication defect: the enclosing result now carries the nested detached units once, rather than both re-emitting them through an inner and outer aggregation path.

The current CMS run has 52 detached units. They remain on their reserved/local node rows; they are not moved below the source merely to make dependency arrows visually downward. Detached units expand horizontal tree width. They can increase project height only when their reserved/padded local row is deeper than the main tree height; composition then uses the maximum completed tree height for the project.

## 9. Top-level tree and project composition

`ArchitectureV7ProjectCompositionStage.Compose` orders project IDs ordinally by project ID. Trees are grouped by project and composed atomically from their frozen tree-local widths. Tree units are separated by one logical column.

Each project has a two-track surround on every side:

- outer track: `RoutingAllowed | GeneralRouting`;
- inner boundary track: `RoutingAllowed | ProjectBoundary | StraightPassthroughOnly`, with header-blocked capability on the header row;
- interior rows alternate node-capable and routing-capable rows;
- the project header is the first inner boundary row.

The transform uses `InteriorOriginRow=2` and `InteriorOriginColumn=projectCursor+2`. Project width is `interiorWidth + 4`; project height is `interiorHeight + 4`. Project regions are composed side-by-side with one logical column between regions.

## 10. External placement

External nodes are placed after ordinary project trees and before standalone packing. They are ordered by physical ID. The row is diagram-wide, computed from the frozen External reservation as described above; the current value is row 37.

The owner-alignment preference is the positional owner's centre where that owner is already placed. `FindExternalCentre` searches deterministic increasing distances around the preferred centre and requires the entire span plus an empty column on each side. It never moves ordinary completed trees. Cross-project duplicate External nodes are separate physical nodes with their own IDs and placements; they share the same frozen External row.

There is one shared External region for the composed diagram, not one independent External row per project.

## 11. Standalone placement

Standalone nodes are projected nodes marked `IsStandalone` and not External. They are sorted by physical ID. The packing target is:

```text
targetWidth = max(1, ceil(sqrt(sum(span + 1 for each standalone node))))
```

The first standalone node row is `externalRow + 2` (row 39). Nodes are placed left-to-right with `span + 1` column consumption. If adding the next node would exceed `targetWidth`, the stage increments the row by two and resets the column to zero. Thus routing rows are inserted between standalone node rows. The common width is expanded to the greatest occupied standalone right edge.

This is a width heuristic, not a bin-packing optimizer. It uses actual frozen spans and one logical gap, but it does not use project width or route demand. In the audited run, 168 standalone nodes occupy 27 odd node rows, rows 39 through 91. The exact node counts per row are:

```text
39:6  41:6  43:7  45:7  47:7  49:7  51:7  53:7  55:7
57:6  59:7  61:6  63:6  65:6  67:7  69:6  71:6  73:6
75:7  77:6  79:6  81:6  83:6  85:6  87:6  89:7  91:1
```

A row breaks only when the next `column + span` would exceed `targetWidth`; it is not broken by route relationships or project boundaries.

## 12. Common-grid construction

`BuildDiagramGrid` first creates every cell in the final rectangle with `RoutingAllowed | GeneralRouting`. It then overlays project cells, then overlays node footprints by adding `NodeAllowed` and the occupant ID. Project capabilities therefore take precedence over the common default, while node occupancy is added to the existing cell capability.

Project IDs are ordered ordinally and placed side-by-side. The final row count is the maximum of External extent, project region bottoms, and standalone region bottoms. The final column count is the maximum of the accumulated project cursor, project region extents, and standalone right edges.

The audited common grid is `92 × 857`. `ArchitectureV7ProjectCompositionStage.Compose` freezes these dimensions inside `ArchitectureV7PlacementFreeze`; logical routing cannot resize or recompose the grid.

## 13. Placement accounting/freeze

Commit `10f1997` added the explicit `ArchitectureV7PlacementAccounting.Validate` boundary. It checks that:

- every projected physical ID is placed exactly once;
- no placed ID is unprojected;
- External membership agrees with the External region;
- standalone membership agrees with the standalone region;
- no physical ID is duplicated in placement;
- configured duplicate IDs are retained and accounted for like canonical IDs.

Diagnostics include the physical ID, semantic ID, project, External/standalone flags, and placement description. The resulting placement freeze carries projection, ownership, sizing, reservation, and placement fingerprints. Later stages may consume but not alter those products.

## 14. Logical routing

The router is `ArchitectureV7LogicalRelationshipRoutingStage.Route`. It orders physical links by physical link ID and stores every traversed logical cell in the route. It reads the frozen grid's cell capability and occupant ID.

Traversal is delegated to `ArchitectureV7CellTraversalPolicy.Allows`, which is entry/exit specific, not merely a cell-bit test:

- `GeneralRouting`: ordinary horizontal/vertical movement and bends;
- `RoutingAllowed`: the base routing marker; the policy permits the intended straight traversal represented by the capability;
- `NodeAllowed`: vertical straight passthrough only where the cell is empty;
- `StraightPassthroughOnly`: straight movement only;
- `HeaderBlocked`: rejects routing through header text;
- unrelated occupied node cells are rejected by occupancy checks, except source/target endpoint occupancy where the endpoint is allowed.

Direct-child routing attempts the three-cell sequence source, intervening routing cell, target. General routing begins by leaving the source downward. If the target is below, it continues by two logical rows at a time, where both the intervening node-capable row and next routing row are explicitly represented. On the row above the target it makes one monotonic horizontal alignment and enters the target.

For upward routes, the source first exits down, selects an escape column outside the source footprint, ascends using the same continuation logic, then aligns to the destination and enters it. Same-layer relationships use the general path because they do not satisfy the direct-child row test.

Continuation selection is decision-before-append:

1. test the current column;
2. test candidates at deterministic `-2`, `+2`, `-4`, `+4`, ... ordinal positions;
3. nearest legal candidate wins; the left candidate is tested first on ties;
4. only after selection is the final monotonic horizontal segment and the two-cell vertical continuation appended;
5. failed probes are not inserted into the frozen route.

The corrected upward footprint-relative arithmetic from `6adac05` computes the source footprint left and right boundaries and only considers candidates outside the required separation. Upward evidence records the authoritative candidate sequence; it does not independently reconstruct a different route.

Routes freeze when `ArchitectureV7LogicalRouteFreeze` is constructed. The route freeze includes cells, completeness, diagnostics, attempt evidence, operation metrics, and fingerprints.

## 15. Route-size characteristics

The audited run has 383 complete routes and 45,708 stored logical route cells. Median length is 52, p95 is 468, and maximum is 756. Every horizontal and vertical cell is stored because later run extraction, allocation, evidence, and mechanical compilation consume the exact cell sequence.

The 756-cell example is:

```text
PageRenderCoordinationService -> LayoutOrchestrationService
747 horizontal transitions
8 vertical transitions
```

It spans from source centre column 10 on row 17 to target centre column 757 on row 25. The width is caused by side-by-side project/tree composition and the large common-grid column domain, not by a logical path search over alternate histories.

## 16. Maximal straight runs

`BuildRuns` in `ArchitectureV7CollectivePostRoutingAllocationStage` walks each frozen route and emits maximal consecutive same-orientation segments. A run records:

- deterministic run ID based on physical link and ordinal;
- physical link ownership;
- route start/end indices;
- orientation;
- every included logical cell;
- fixed row for horizontal runs or fixed column for vertical runs.

The audited conversion is 45,708 route cells into 1,299 runs. A run never changes lane internally; a turn creates separate incoming and outgoing runs.

## 17. Lane allocation

`AllocateLanes` compares runs on the same fixed axis and assigns the first available ordinal not overlapping an already assigned run. Lane IDs encode orientation and fixed coordinate, for example `lane:H:<row>:<ordinal>` or `lane:V:<column>:<ordinal>`. Assignment order is deterministic by run ordering and coordinate/link keys. `parallelLaneSpacing=12` is the physical spacing authority.

The current implementation performs repeated collection scans for overlap checks. For the CMS input it took approximately 107 ms and produced 2,202 lane/assignment records. Its practical cost is pairwise in the number of runs sharing an axis, rather than global route-pair allocation.

## 18. Terminal allocation

`AllocateTerminals` creates source-bottom and destination-top terminal assignments. Endpoint directions are grouped and ordered using the implementation's left/down/right direction ordering. Terminal slots use configured spacing and inset. The pre-routing sizing formula reserves the maximum of incoming and outgoing terminal capacity independently; post-routing allocation assigns actual endpoint slots from the frozen route/run set.

The implementation's terminal capacity formula is:

```text
count == 0 ? 0 : 2 * TerminalInset + (count - 1) * TerminalPortSpacing
```

The allocation configuration currently maps terminal inset from `LinkNodeWidthPadding`, and terminal port spacing from `EdgePortSpacing`. If actual assignment demand exceeds the pre-routing physical width, allocation/acceptance diagnostics can expose the mismatch; terminal allocation does not change logical spans.

## 19. Endpoint approaches and handoffs

Endpoint approaches are built by `BuildApproaches`. Endpoint handoffs were introduced as an explicit resource in commit `8d901f7` (Phase 2A). A handoff is required when the physical terminal position and the first/last allocated route point do not meet on the required axis. It is not required when the terminal and adjacent route geometry already align.

An `ArchitectureV7EndpointHandoff` records resource ID, physical link, endpoint kind, route/run identity, terminal slot, route index, logical endpoint/handoff cell, orientation, relative physical position, required clearance, and provenance. The compiler consumes the frozen handoff to insert a physical handoff point; it does not alter logical route cells. Missing handoffs produce a hard scene diagnostic rather than a synthesized fallback.

## 20. Bend allocation

`BuildBendsResources` (Phase 2B) identifies every logical turn by comparing adjacent run orientations. It links the bend to physical link, route index, incoming/outgoing run IDs and lanes, and assigns a deterministic relative physical offset with clearance demand. Multiple bends in one logical cell are represented by multiple bend resources, each with its own relationship/run identity and offset.

The current capacity model is an envelope check based on configured clearance and parallel lane spacing. A capacity diagnostic is emitted when the number of bend/crossing resources in a logical cell cannot fit the calculated physical envelope. “Capacity” therefore means available physical offset envelope in the compiled track, not a new logical routing cell or alternate route.

## 21. Crossing allocation

`BuildCrossingResources` flattens complete route cells and groups entries by logical cell. It identifies horizontal passes, vertical passes, and turns. For each horizontal/vertical interaction it creates a candidate:

- `clean-crossing`: horizontal pass against vertical pass;
- `turn-pass`: a horizontal/vertical pass against a turn, or the corresponding turn/pass orientation.

The resource identity combines cell coordinates and the two physical link IDs. Each record also contains horizontal/vertical run IDs, lane IDs, route indices, relative position, effective position, required clearance, classification, and provenance.

The audited totals are:

| Measure | Count |
|---|---:|
| Crossing resources | 11,019 |
| clean-crossing | 5,630 |
| turn-pass | 5,389 |
| unique crossing cells | 1,032 |
| maximum resources in one cell | 405 |
| median resources per cell | 5 |
| p95 resources per cell | 39 |
| maximum resources per route | 236 |

A crossing resource represents a route/run pair interaction at a logical cell plus a physical offset allocation. It is not necessarily one unique physical slot per interaction. The current implementation emits a resource for each discovered candidate; compatible positions may still have identical or near-identical envelope semantics, so interaction count and distinct physical positions are not guaranteed one-to-one.

The maximum of 405 resources in one cell is possible because the cell-group contains many horizontal entries and vertical entries; candidate creation multiplies `horizontalEntries × verticalEntries`, with additional pass/turn candidates. The compact audit snapshot did not preserve the complete per-cell candidate decomposition for the densest cell, so its exact horizontal-run/vertical-run/turn-run counts cannot be reconstructed unambiguously from the retained summary alone. The authoritative allocation evidence preserves all individual crossing records in `04-allocation.json`.

The physical slot offset uses lane offsets and a centred slot offset:

```text
slotOffset = (slot - (count - 1) / 2) * ParallelLaneSpacing
```

The allocation stage takes responsibility for producing these resources; the compiler must consume them mechanically.

## 22. Track-demand aggregation

The allocation freeze contains lane assignments, terminal slots, endpoint approaches, handoffs, bends, crossings, and track demands. Each demand carries an axis, logical row/column, relative offset, and clearance/envelope requirement. Track-demand aggregation takes maxima over compatible demands for the affected physical track. Resource count does not automatically equal physical extent; compatible resources can share an envelope if their offsets and clearance fit.

## 23. Physical row/column sizing

`ArchitectureV7PhysicalSceneSizing.RowMinimum` chooses a role baseline by inspecting cell capabilities:

1. header-blocked row → `ProjectHeaderHeight`;
2. node-capable row → `NodeMinimumHeight`;
3. project boundary → `BoundaryRowMinimum`;
4. otherwise → `RoutingRowMinimum`.

For routing rows, `ArchitectureV7PhysicalSceneCompilationStage.SizeRows` expands the baseline with allocation demand. The lane envelope is:

```text
laneCount == 0 ? 0 : 2 * RouteClearance + (laneCount - 1) * ParallelLaneSpacing
```

Node-bearing rows begin at the configured node minimum. Boundary and header rows begin at their configured minima. Columns are sized from logical cells, frozen node width requirements, label/terminal requirements, and allocation demand; physical dimensions do not change logical spans or coordinates.

The current physical configuration mapping includes `BaseCellWidth=100`, `RoutingRowMinimum=20`, `BoundaryRowMinimum=20`, `NodeMinimumHeight=80`, `ProjectHeaderHeight=34`, `ParallelLaneSpacing=12`, and route clearance mapped from `LinkPadding`.

## 24. Physical scene compilation

Phase 2C commit `2625f2b` made scene compilation consume frozen allocation products and propagate their provenance. `MaterialiseNodes` uses frozen node bounds and labels. `MaterialiseTerminals` uses frozen terminal assignments. `MaterialiseRoutes` walks every frozen logical route cell, resolves runs and lanes, consumes handoff/bend/crossing resources, and emits unsimplified orthogonal physical points and segments.

The current compiler problem is in `MaterialiseRoutes` and `PointFor`:

- route runs are repeatedly found with `Where`/`First`;
- assignments are repeatedly found with `First`;
- bends and handoffs are repeatedly searched;
- `FindCrossing` linearly scans every crossing allocation;
- `RequiresCrossing` scans every other route and every route cell to rediscover whether a perpendicular pass/turn exists.

All facts required for the route polyline are already represented by frozen route cells, maximal runs, lane assignments, terminals, handoffs, bends, crossings, and track dimensions. `FindCrossing`'s `allRoutes` argument is unused; `RequiresCrossing` duplicates the crossing allocator's result. Therefore the global rediscovery is redundant implementation work, not an additional authority.

Conceptually, the compiler can be reduced to:

```text
ordered maximal runs + ordered frozen resource events -> physical orthogonal polyline
```

That reduction is documented here only; no implementation change was made.

## 25. Physical acceptance

`ArchitectureV7FinalAcceptanceValidationStage.Validate` validates fingerprints, accounting, placement, logical routes, physical geometry, track sizing, retained diagnostics, crossings, and compiler completeness. Rules include:

| Area | Current inspection | Status / complexity |
|---|---|---|
| Logical topology | route completeness, endpoint identity, cell adjacency | hard findings; linear in route cells |
| Cell capability | route transitions and traversal legality | hard route findings |
| Node collision | physical segments against node bounds | hard; approximately segment × node |
| Terminal capacity | frozen assignments and physical terminal positions | hard when allocation/scene facts are missing |
| Collinear sharing | segment overlap and spacing checks | hard; pairwise segment comparisons |
| Parallel spacing | parallel segment intervals and lane positions | hard; pairwise by axis |
| Bend conflicts | allocated bend resources and physical turns | hard when resource is missing/conflicting |
| Crossings | physical horizontal/vertical intersections versus frozen crossing resources | hard; horizontal × vertical comparisons plus resource lookups |
| Z geometry | route segments/turn sequence and endpoint handoff geometry | hard when diagonal or invalid handoff output occurs |
| Endpoint handoffs | frozen handoff presence and materialised points | hard |
| Diagonal output | every compiled segment must be orthogonal | hard compiler diagnostic |
| Compiler completeness | every complete frozen route must compile | hard |
| Renderer fidelity | emitted node/edge counts and geometry versus scene bounds/points | hard fidelity findings |

Allocation diagnostics are distinct from validator findings. A missing bend, crossing, or handoff can be emitted by allocation or compilation before acceptance; validator findings inspect the resulting frozen products and physical scene.

## 26. Renderer

The mechanical Draw.io renderer emits node vertices, project/container cells, External shapes, and connector edges from the physical scene. Connector waypoints are taken from compiled route points. The renderer does not own logical routing, lane selection, bend/crossing allocation, or physical acceptance. Draw.io automatic routing is not the V7 authority; the scene's waypoints are the intended geometry.

Known styling discrepancy: the current renderer path does not consistently consume preserved configured node/project styles. Some node styles are hardcoded or ignored, project styling is not fully preserved, and External styling, connector color/width/arrows, fonts, canvas/background, and extra properties do not all match configuration. This is a renderer-fidelity debt, not a placement or routing authority.

## 27. Evidence and provenance

Provenance is carried through the products:

- semantic: analyser node/link identities and project ownership;
- projection: physical-to-semantic mapping and duplicate provenance;
- ownership: positional parent and ownership fingerprint;
- placement: tree/project region, row/column, span, centre, detached/External/standalone flags;
- routing: route family, logical cell sequence, diagnostics, attempt evidence, operation metrics;
- runs/lanes: run IDs, route indices, orientation, fixed axis, lane IDs and ordinals;
- handoffs/bends/crossings: resource IDs, relationship/run linkage, offsets, clearance, classification, and allocation provenance;
- physical scene: point/segment provenance naming the frozen run/resource or terminal source.

Some diagnostics are global or null-subject findings because a stage can detect a grid-wide fingerprint mismatch, accounting mismatch, or shared resource-capacity problem without one unique node/relationship subject. The evidence stage bounds representative candidate summaries; it must not independently become a second routing authority.

## 28. Determinism

The implementation uses the following stable order decisions:

- project IDs: ordinal string order;
- semantic/external input nodes: stable IDs;
- physical duplicate branches: configured branch order and branch ordinal;
- ownership candidates: deterministic link/physical ID order;
- recursive children: projection/analyser FIFO order preserved in the child map;
- tree/root composition: tree/project IDs and stage order;
- detached units: recursive discovery/FIFO order;
- External nodes: physical ID order, then deterministic preferred-centre distance with left/right ordering;
- standalone nodes: physical ID order;
- routes: physical link ID order;
- continuation candidates: current column, then ±2 ordinal distance, left before right;
- lanes: deterministic run order and first available ordinal;
- terminals: endpoint direction order and stable physical-link/slot order;
- handoffs, bends, crossings: stable route/run/link/cell keys;
- compiler output: frozen route order and route-cell order.

Fingerprints concatenate ordered IDs, coordinates, resource IDs, and diagnostics and hash the result with SHA-256.

## 29. Complexity map

| Stage | As-built dominant behavior | CMS evidence |
|---|---|---:|
| Projection | node/link enumeration plus duplicate branch generation | included in semantic/projection phase |
| Ownership | link grouping and deterministic parent selection | 443 decisions |
| Sizing | physical-node pass plus grouped link counts | 443 requirements |
| Reservation | depth inspection plus ordered reduction | 6 frozen reservations |
| Recursive placement | recursive subtree construction and sibling packing | part of 2.733 s composition/accounting |
| Project composition | project/tree placement, External search, standalone packing, dense grid creation | 2.733 s composition/accounting |
| Routing | per-link bounded topology-directed routing; route-cell append and capability checks | 3.594 s |
| Run construction | one pass over route cells | 9 ms |
| Lane allocation | overlap scans among runs sharing an axis | 107 ms |
| Terminal allocation | grouped endpoint/run assignment | 16 ms |
| Handoff allocation | endpoint resource construction and matching | 426 ms |
| Bend allocation | turn enumeration and envelope checks | 1.107 s |
| Crossing allocation | cell grouping plus horizontal×vertical candidate multiplication | 10.735 s |
| Track sizing | row/column demand aggregation | row 337 ms; column 138 ms |
| Scene compilation | per-cell repeated global run/crossing/route discovery | route materialisation >45 s and incomplete |
| Acceptance | several segment/node and segment/segment scans | not reached in this run |
| Renderer/fidelity | Draw.io cell scans and geometry comparison | not reached in this run |

The compiler's effective worst-case behavior is approximately total route cells multiplied by crossing resources, plus repeated route-cell scans from `RequiresCrossing`, with additional repeated linear run/assignment lookups.

## 30. Current CMS worked example

```text
5 source projects
387 project semantic nodes
25 External semantic nodes
412 semantic/canonical nodes
31 configured duplicate physical nodes
443 physical nodes
383 semantic/physical relationships
92 x 857 logical grid
168 standalone physical nodes
56 External physical nodes
52 detached units
383 completed logical routes
45,708 logical route cells
1,299 maximal runs
2,202 lane/assignment records
766 terminals
667 handoffs
916 bends
11,019 crossing resources
```

The transformations are:

```text
387 project nodes + 25 External nodes = 412 semantic nodes
412 canonical nodes + 31 configured IEventHub branches = 443 physical nodes
443 placed nodes -> 92 x 857 frozen common grid
383 physical links -> 383 complete logical routes
45,708 route cells -> 1,299 maximal runs
1,299 runs -> 2,202 lane/assignment records
383 routes -> 766 endpoint terminal assignments
383 routes -> 667 endpoint handoff resources
383 routes -> 916 bend resources
route-cell interactions -> 11,019 crossing resources
```

The physical compiler reached row/column sizing, node materialisation, and terminal materialisation, then remained in route materialisation. Acceptance, rendering, and renderer-fidelity validation were not reached by this run.

## 31. Known discrepancies and implementation debt

The following are inventory items, not repair proposals:

1. The accepted 355-node artifact and the current 443-node run use different semantic input/project scope and projection state; counts are not directly comparable without matching the input and settings.
2. The implementation has one diagram-wide frozen reservation table, while some contract language describes reservation reasoning in more project-oriented terms.
3. External reservation coordinates are transformed to final row 37 by a separate composition formula; the two coordinate domains are easy to confuse.
4. Recursive placement retains detached units as explicit placement units; nested-detached aggregation was repaired in `9582e4e`, but detached geometry remains a special path.
5. Common-grid materialisation is dense across the final logical rectangle.
6. `BuildCrossingResources` produces a large number of relationship/run interaction resources in dense cells; interaction count and distinct physical slot count are not guaranteed to be one-to-one.
7. Physical compilation redundantly rediscoveries frozen crossing facts through `FindCrossing` and `RequiresCrossing`.
8. The current compiler is pathologically inefficient and did not complete route materialisation for the audited CMS run.
9. Several acceptance checks use pairwise segment/node or segment/segment scans and were not reached in the audited run.
10. Renderer styling does not fully preserve configured node/project/External/connector presentation settings.
11. The compact retained snapshot does not contain enough per-cell composition detail to identify the exact horizontal/vertical/turn decomposition of the densest 405-resource crossing cell without rerunning inspection.
12. The physical configuration names and their mapped authorities are not perfectly aligned: allocation clearance, terminal inset, route clearance, and node clearance are mapped from different configured fields.

No production, test, configuration, placement, routing, allocation, compilation, validation, rendering, or performance behavior was changed while producing this document.

## V7 Contract Compliance

This is documentation-only. No contract section was modified. The document records the current implementation and explicitly identifies known implementation/contract discrepancies rather than treating the contract as an implementation description.
