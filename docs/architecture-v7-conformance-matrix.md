# Architecture V7 Implementation-vs-Algorithm Conformance Matrix

Status: review checkpoint A; no production, test, configuration, placement, routing, allocation, compiler, validator, renderer, or performance changes are included.

Baseline inspected:

- implementation HEAD: `10f1997789c9a5139a5259c6966683d021325e3e`;
- implementation description: `docs/architecture-v7-as-built.md`;
- normative comparison: `docs/architecture-v6-contract.md` and the V7 algorithm rules issued during the staged V7 work;
- audited CMS evidence: `C:\Users\Ash\Documents\codex-artifacts\v7-readonly-audit-20260810`.

Classification meanings:

- **MATCH** — implementation directly expresses the intended rule and the evidence supports it;
- **ACCEPTABLE IMPLEMENTATION DETAIL** — behavior matches, but representation/order/mechanics differ from the prose;
- **AMBIGUOUS — CODE REVIEW REQUIRED** — the intended rule or current data meaning cannot be proved from the present authority boundary;
- **MISMATCH** — implementation demonstrably differs from the intended rule or duplicates an authority that must be single-owned.

“Smallest corrective authority” identifies where a future change would belong. It is not authorization to implement that change.

## Matrix

| # | Intended rule | As-built implementation | Owning class/method | Evidence/reference | Classification | Impact if mismatched | Smallest corrective authority |
|---:|---|---|---|---|---|---|---|
| 1 | Roslyn analysis is the semantic input boundary. | `IArchitectureAnalyser.AnalyseAsync` supplies the `ArchitectureDiagram`; V7 does not re-analyse source. | `ArchitectureV7ProductionGenerationService.GenerateAsync` | As-built §2; production orchestration | MATCH | None established. | Semantic analyser boundary |
| 2 | Semantic nodes retain project identity and External identity. | Project nodes and `ExternalNodes` are read separately and retain IDs, names, full names, kind, project and flags. | `ArchitectureV7PhysicalProjectionStage.ReadNodes` | `ArchitectureV7PhysicalProjectionStage.cs` | MATCH | None established. | Projection input model |
| 3 | Semantic relationship selection is complete before projection. | Projection consumes `diagram.Links`; no later semantic discovery occurs. | `ArchitectureV7PhysicalProjectionStage.Project` | As-built §2–3 | MATCH | Omitted semantic links cannot be recovered downstream. | Semantic analyser, if selection is wrong |
| 4 | Projection creates one canonical physical node per semantic node. | Physical IDs are `physical:` plus semantic identity; all 412 current semantic nodes produce canonical nodes. | `ArchitectureV7PhysicalProjectionStage.Project` | CMS: 412 canonical nodes | MATCH | Count drift would affect every downstream freeze. | Projection |
| 5 | Configured duplication is explicit and deterministic. | Configured mode is selected when duplication is enabled; duplicate IDs use `:duplicate:<ordinal>` and carry provenance. | `ArchitectureV7PhysicalProjectionStage.Project` | CMS: 31 duplicates; `IEventHub` provenance | ACCEPTABLE IMPLEMENTATION DETAIL | Duplicate count changes placement and route endpoints. | Projection |
| 6 | Duplicate policy belongs to physical projection, not semantic analysis. | Semantic model is not mutated; duplicate branches are generated in projection. | `ArchitectureV7PhysicalProjectionStage` | As-built §3 | MATCH | Moving policy upstream would contaminate semantic meaning. | Projection |
| 7 | Every projected physical relationship has physical endpoints and semantic provenance. | 383 physical links retain semantic IDs and physical source/destination IDs. | `ArchitectureV7PhysicalProjectionStage.Project` | CMS: 383 links/routes | MATCH | Missing endpoint becomes a hard route diagnostic. | Projection |
| 8 | Projection has no placement authority. | Projection selects IDs/links only; no rows, columns, spans, or parents are chosen. | `ArchitectureV7PhysicalProjectionStage` | As-built §3 | MATCH | Placement contamination would violate freeze order. | Projection boundary |
| 9 | Every physical node has at most one positional parent. | Ownership emits one `PositionalParentPhysicalNodeId` or null per physical node. | `ArchitectureV7PositionalOwnershipStage.Resolve` | As-built §4; 443 decisions | MATCH | Multiple positional parents would make recursive placement ambiguous. | Ownership |
| 10 | Additional semantic parents remain routing relationships. | Ownership does not delete physical links; only one parent is used for tree construction. | `ArchitectureV7PositionalOwnershipStage.Resolve` | As-built §4 | MATCH | Removing links would lose required topology. | Ownership |
| 11 | Parent selection is deterministic and project-aware. | Incoming candidates are ordered by implementation comparers and same-project preference. | `ArchitectureV7PositionalOwnershipStage.Resolve` | Source inspection; as-built §4 | ACCEPTABLE IMPLEMENTATION DETAIL | Parent changes alter tree geometry. | Ownership |
| 12 | Span sizing is before placement and routing. | Sizer consumes ownership and configuration only and returns requirements/fingerprint. | `ArchitectureV7PreRoutingNodeSpanSizer.Size` | As-built §5 | MATCH | Later expansion would invalidate frozen topology. | Pre-placement sizing |
| 13 | Visible label requirement is independent. | Longest visible line × `LabelCharacterWidth` + `LabelHorizontalMargin`; External adds `[External]`. | `ArchitectureV7PreRoutingNodeSpanSizer.Size` | Method body; CMS settings | MATCH | Under-width nodes create terminal/label collisions. | Pre-placement sizing |
| 14 | Incoming and outgoing terminal capacity are calculated independently. | Links are grouped by destination and source physical ID separately. | `ArchitectureV7PreRoutingNodeSpanSizer.Size` | `incoming`/`outgoing` dictionaries | MATCH | Combining degree would over/under-size nodes. | Pre-placement sizing |
| 15 | Zero terminal demand contributes zero width. | `TerminalWidth(count)` returns zero for count ≤ 0. | `ArchitectureV7PreRoutingNodeSpanSizer.TerminalWidth` | Method body | MATCH | None established. | Pre-placement sizing |
| 16 | Terminal capacity is `2*inset + (count-1)*spacing` for count ≥ 1. | Exact formula is implemented; current production mapping supplies inset/spacing from configured fields. | `TerminalWidth`; allocation `AllocateTerminals` | As-built §§5,18 | MATCH | Authority-mapping errors can create overflow diagnostics. | Configuration mapping, then sizing/allocation |
| 17 | Span is the smallest odd span satisfying the maximum requirement, minimum 3. | `ceil(requiredWidth/baseCellWidth)`, minimum 3, then odd-rounding. | `ArchitectureV7PreRoutingNodeSpanSizer.Size` | Method body; `baseCellWidth=100` | MATCH | Wrong span changes all placement widths. | Pre-placement sizing |
| 18 | Duplicate physical nodes receive only their own projected demand. | Grouping is by physical endpoint ID, so duplicate branches do not inherit canonical degree. | `ArchitectureV7PreRoutingNodeSpanSizer.Size` | As-built §5 | MATCH | Duplication could inflate spans. | Projection/sizing boundary |
| 19 | Reserved patterns are ordered, case-insensitive suffix matches. | Inspector resolves first matching role by suffix; zero-match groups remain in inspection but are removed in reconciliation. | `ArchitectureV7ReservedRoleConstraintInspector.ResolveRole` | Method body; CMS reservation evidence | MATCH | Role misclassification shifts tree rows. | Reservation inspection |
| 20 | Natural depth is distinct from reservation/table depth. | Inspector computes `RequiredNodeRow(depth)=depth*2+1`, names it a node row, then recursive placement treats that value as a layer and multiplies it again by two for local row. | `ArchitectureV7ReservedRoleConstraintInspector.RequiredNodeRow`; `ArchitectureV7RecursiveTreeGridStage.ReservedLayer` | CMS: frozen 5/7/11/13/15/17; final rows 13/17/25/29/33/37 | **MISMATCH** | Reservation depths are double parity-converted; final placement rows and project height can drift from the intended depth model. | Reservation/recursive placement coordinate boundary |
| 21 | Reconciliation shifts only later groups in odd increments and appends External last. | Active groups are filtered, ordered, assigned odd rows, and propagated with `OddAtOrAbove`; External is appended. | `ArchitectureV7ReservationReconciliationStage.Reconcile` | As-built §6; frozen table | ACCEPTABLE IMPLEMENTATION DETAIL | Correct only if the input row domain is correct; row-domain mismatch above propagates. | Reservation coordinate model |
| 22 | One coherent reservation coordinate domain is consumed by every tree. | A frozen row is used as `layer`, then local node row is `layer*2`; composition adds `InteriorOriginRow+1`. External separately uses `NodeRow*2+3`. | Inspector, recursive stage, composition | `ReservedLayer`, `ArchitectureV7ProjectCompositionStage.Compose` | **MISMATCH** | External 17→37 and ordinary continuation rows cannot be explained by one typed conversion; hidden parity assumptions create semantic drift. | Introduce one explicit conversion boundary in reservation/placement |
| 23 | External final row is the shared reserved External node row in the final logical grid. | Frozen External node row 17 is converted to final row 37 by `17*2+3`. | `ArchitectureV7ProjectCompositionStage.Compose` | As-built §10; CMS row 37 | **MISMATCH** | External and ordinary project row comparisons use different row domains. | Reservation/composition boundary |
| 24 | Children are built recursively before parents. | `BuildNode` recursively builds child units before creating the parent placement. | `ArchitectureV7RecursiveTreeGridStage.BuildNode` | Method body | MATCH | None established. | Recursive placement |
| 25 | Complete child subtree width is used for sibling packing. | `RequiredWidth` uses maximum child placement right edge; units are offset by width plus one. | `BuildNode`, `AssertPackedUnits` | Method body; placement regressions | MATCH | Under-measurement causes overlap. | Recursive placement |
| 26 | Siblings have exactly one logical separation column. | `childWidth += RequiredWidth(child)+1`; assertions reject lost separation. | `BuildNode`, `AssertPackedUnits` | Method body | MATCH | Missing separation blocks routing corridors. | Recursive placement |
| 27 | One child centres parent on child centre. | Parent centre is first/last direct-child root centre midpoint; for one child both are equal. | `BuildNode` | Method body | MATCH | Parent alignment changes direct-child route geometry. | Recursive placement |
| 28 | Multiple children centre on direct-child node centres, not descendants. | Parent formula uses `RootCentreCell` of first and last positioned direct child; descendants affect only width. | `BuildNode` | Method body | MATCH | Descendant-bound centring would violate topology-directed placement. | Recursive placement |
| 29 | Reserved conflict creates detached unit without changing ownership. | Child target layer ≤ parent layer calls `BuildDetachedUnit`; the link/decision remains unchanged. | `BuildNode`, `BuildDetachedUnit` | As-built §8 | MATCH | Reparenting would corrupt semantic/positional accounting. | Recursive placement |
| 30 | Detached units are excluded from centring/sibling composition and appended right/FIFO. | Detached children are removed from `directChildren`, then normalised and appended after the main unit with one separator. | `BuildNode` | Method body | MATCH | Including them in centring creates parent drift. | Recursive placement |
| 31 | Nested detached units are aggregated once. | `built.Detached.ToList()` and `Complete`/`Detached` aggregation reflect the `9582e4e` repair; duplicate placement assertions remain. | `BuildNode`, top-level `Build` | Commit `9582e4e`; as-built §8 | MATCH | Duplicate detached emission would fail placement accounting. | Recursive placement |
| 32 | Every projected physical node is reached by recursive or detached construction exactly once. | `visited`, duplicate, missing, and unknown placement checks enforce this before composition. | `ArchitectureV7RecursiveTreeGridStage.Build` | Method body | MATCH | Omitted nodes would be silently unplaced. | Recursive placement |
| 33 | Top-level tree grids are atomic before project composition. | `ArchitectureV7TopLevelTreeGrid` contains local placements, detached units, width/height and fingerprints. | `Build` | As-built §7 | MATCH | Interleaving unrelated trees would destroy parent ownership. | Recursive placement |
| 34 | Projects are composed side-by-side in deterministic order. | Project IDs are ordinally sorted; tree units are separated by one column. | `ArchitectureV7ProjectCompositionStage.Compose` | Method body | MATCH | Project order changes all common columns. | Project composition |
| 35 | Exactly two surround tracks exist on each project side. | `BuildProjectCells` creates outer row/column and inner row/column, then interior. | `BuildProjectCells` | Method body; as-built §9 | MATCH | Fewer tracks expose headers/boundaries to general routing. | Project composition |
| 36 | Inner boundary/header is straight-only and header text blocked. | Inner cells get `ProjectBoundary|StraightPassthroughOnly`; row 1 also gets `HeaderBlocked`. | `BuildProjectCells` | Method body | MATCH | Bends through boundary/header would violate topology. | Project composition/capability model |
| 37 | Outer surround is GeneralRouting. | Outer cells get `RoutingAllowed|GeneralRouting`. | `BuildProjectCells` | Method body | MATCH | Loss causes avoidable route failures. | Project composition |
| 38 | External is placed after projects, before standalones, shared across diagram. | External nodes are ordered by physical ID and placed on one common row without moving ordinary trees. | `Compose`, `FindExternalCentre` | CMS: 56 External, row 37 | MATCH | Per-project External rows would change route topology. | Project composition |
| 39 | Standalone region is below External with one routing row between regions and between node rows. | First row is `externalRow+2`; subsequent rows increment by two; `span+1` horizontal consumption is used. | `PlaceStandalone` | Method body; CMS rows 39–91 | MATCH | Wrong parity allows node adjacency or External intermixing. | Standalone composition |
| 40 | Standalone packing uses actual spans and an intended square-ish shape metric. | Target width is `ceil(sqrt(sum(span+1)))`; it counts horizontal span+gap but not the inserted routing rows in the shape metric. | `PlaceStandalone` | Method body; CMS 168 nodes/27 rows | **AMBIGUOUS — CODE REVIEW REQUIRED** | If square-ish means total logical area including routing rows, the region is biased tall/narrow. | Standalone composition after normative packing rule is fixed |
| 41 | Common grid defaults to ordinary general-routing space and overlays project/node capabilities. | `BuildDiagramGrid` densely creates `rows*columns` cells with `RoutingAllowed|GeneralRouting`, then overlays project cells and node footprints. | `BuildDiagramGrid` | Method body; CMS 92×857 | ACCEPTABLE IMPLEMENTATION DETAIL | Dense materialisation increases memory but preserves cell semantics. | Common-grid construction |
| 42 | Project/common transforms are authoritative and immutable after placement. | Placement freeze stores projects, transforms, grid, nodes, regions and fingerprints; route stage consumes it. | `Compose`, `ArchitectureV7PlacementFreeze` | As-built §§12–13 | MATCH | Downstream movement would invalidate routes. | Placement freeze |
| 43 | Placement accounting requires projected IDs exactly once. | `ArchitectureV7PlacementAccounting.Validate` rejects missing, unprojected, duplicate, External-membership, and standalone-membership errors. | `ArchitectureV7PlacementAccounting.Validate` | Commit `10f1997` | MATCH | Accounting failures would be hidden placement defects. | Placement accounting |
| 44 | GeneralRouting permits H/V/bends; RoutingAllowed is straight-only; NodeAllowed is vertical straight-only; headers block. | `ArchitectureV7CellTraversalPolicy.Allows` is entry/exit based. `GeneralRouting` wins before `RoutingAllowed`, `NodeAllowed`, and `StraightPassthroughOnly`. | `ArchitectureV7CellTraversalPolicy.Allows` | Method body | **AMBIGUOUS — CODE REVIEW REQUIRED** | Combined capability flags can inherit GeneralRouting bend legality even when another flag says straight-only. | Typed capability/traversal authority |
| 45 | Other route occupancy does not block traversal; unrelated node footprints do. | Occupant checks permit source/target IDs and reject unrelated occupants; capability checks are separate. | `LogicalRelationshipRoutingStage.CanEnterCell`, `Validate` | Method body | MATCH | Confusing endpoint occupancy with unrelated occupancy creates false blocks. | Routing |
| 46 | Direct child uses exactly source, routing, target. | Three-cell path is constructed and validated. | `DirectChild` | Method body; direct-child regression history | MATCH | Extra cells change the topology family. | Routing |
| 47 | Downward routing is source-down, horizontal alignment, bounded ±2 continuation, then target entry. | `General`, `Continue`, `TrySelectContinuation`, and final `AppendHorizontal` implement this sequence. | `ArchitectureV7LogicalRelationshipRoutingStage` | Method body; 383 complete routes | MATCH | Search-history geometry would create reversals. | Routing |
| 48 | Candidate selection happens before append; failed probes never become route geometry. | `TrySelectContinuation` tests candidates, `AppendContinuation` appends only selected segment. | `TrySelectContinuation`, `AppendContinuation` | Commit `9ee7d0f` | MATCH | Probe cells would create A-B-A routes. | Routing |
| 49 | Upward escape must leave source footprint using deterministic candidate arithmetic. | `EscapeCandidates` computes ±2 columns outside source footprint and `TryEscape` uses authoritative candidates/evidence. | `EscapeCandidates`, `TryEscape` | Commit `6adac05`; as-built §14 | MATCH | Escape inside footprint blocks ascent or creates invalid geometry. | Routing |
| 50 | Every traversed logical cell is retained and frozen once. | `ArchitectureV7LogicalRoute` stores ordered cells; allocation/compilation consume them. | `Route` | CMS: 45,708 cells | MATCH | Rebuilding routes downstream would violate freeze order. | Routing |
| 51 | Routes are converted to maximal same-axis runs. | `BuildRuns` emits one run per maximal orientation segment with route indices and cells. | `BuildRuns` | CMS: 45,708 cells → 1,299 runs | MATCH | Split/merged runs change lane/resource semantics. | Allocation |
| 52 | One maximal run owns one lane for its entire run. | `AllocateLanes` appends exactly one `RunLaneAssignment` per run; 2,202 in the audit was lanes plus assignments, not assignments alone. | `AllocateLanes` | Method body; 1,299 runs and 1,299 assignments | MATCH | Internal lane changes would invalidate physical continuity. | Allocation |
| 53 | Overlapping same-axis runs receive distinct compatible lanes. | Lane domain is orientation plus fixed row/column; intervals are compared and first available ordinal selected. | `AllocateLanes` | Method body; 107 ms audit | MATCH | Shared overlapping lane produces physical collisions. | Allocation |
| 54 | Terminal allocation is collective, deterministic, and capacity-checked. | Groups by node/kind/direction, orders by anchor/link, assigns offsets and emits overflow diagnostics. | `AllocateTerminals` | Method body | MATCH | Overflow becomes hard allocation/acceptance evidence. | Allocation/configuration mapping |
| 55 | Endpoint mismatch receives an explicit handoff resource. | `BuildHandoffs` emits immutable resources consumed by compiler; no logical route changes. | `BuildHandoffs`, `AddAllocatedEndpointHandoffs` | Commit `8d901f7` | MATCH | Synthesized handoffs would hide allocation defects. | Allocation |
| 56 | Turns receive explicit bend resources linked to runs/routes. | `BuildBendsResources` identifies turns, assigns offsets and clearance, and emits capacity diagnostics. | `BuildBendsResources` | Commit `3a3dac4` | MATCH | Compiler cannot invent missing bend resources. | Allocation |
| 57 | Interaction/provenance is distinct from physical crossing resource/slot. | `BuildCrossingResources` emits one `ArchitectureV7CrossingAllocation` per horizontal/vertical pass/turn candidate, with pair IDs and relative position; there is no separate interaction model or many-to-one resource grouping. | `BuildCrossingResources` | CMS: 11,019 resources, max 405/cell; `CrossingAllocation` model | **MISMATCH** | Dense pair interactions are treated as resource count/envelope demand; physical capacity and evidence semantics are conflated. | Collective allocation model |
| 58 | Multiple logical interactions may share one physical crossing resource when geometry permits. | Candidate multiplication uses `hEntries × vEntries`; each candidate becomes an allocation record. | `BuildCrossingResources` | Method body; as-built §21 | **MISMATCH** | 405 interactions can inflate resource counts and physical demand even where slots could be shared. | Allocation resource model |
| 59 | Track demand is frozen from lanes, terminals, handoffs, bends, crossings. | Allocation emits `TrackDemands`; scene sizing reads allocation and configuration. | `Allocate`, `SizeRows`, `SizeColumns` | As-built §§22–23 | ACCEPTABLE IMPLEMENTATION DETAIL | Demand-model errors expand physical tracks or emit capacity findings. | Allocation/physical sizing boundary |
| 60 | Row/column sizing may expand physical extents but cannot change logical topology. | Scene compiler sizes physical tracks after placement/routes/allocation; logical rows/columns remain frozen. | `ArchitectureV7PhysicalSceneCompilationStage` | Freeze order; as-built §23 | MATCH | Any logical movement would invalidate route fingerprints. | Physical sizing |
| 61 | Compiler consumes frozen lane/terminal/handoff/bend/crossing facts mechanically. | `PointFor` repeatedly calls `First`, `FindCrossing`, and `RequiresCrossing`; the latter scans all routes/cells and rediscovers crossing existence. | `MaterialiseRoutes`, `PointFor`, `FindCrossing`, `RequiresCrossing` | Method body; >45 s incomplete route materialisation | **MISMATCH** | Compilation is pathologically expensive and has duplicated downstream authority. | Physical scene compiler |
| 62 | Compiler must not rediscover bend need or handoff need. | Handoff and bend records are searched per route/cell rather than pre-indexed; missing resources become diagnostics. | `PointFor`, `AddAllocatedEndpointHandoffs` | Method body | **MISMATCH** | Repeated discovery risks inconsistent behavior and runtime collapse. | Physical scene compiler |
| 63 | Scene compiler emits orthogonal physical points/segments without route repair. | `MaterialiseRoutes` walks ordered cells, creates points, rejects diagonals, and preserves the frozen route sequence. | `MaterialiseRoutes` | As-built §24 | MATCH | Diagonal output becomes a hard compiler finding. | Physical scene compiler |
| 64 | Acceptance verifies frozen products and physical geometry; it does not repair. | Acceptance checks fingerprints, accounting, logical routes, geometry, tracks, crossings, diagnostics, and fidelity inputs. | `ArchitectureV7FinalAcceptanceValidationStage.Validate` | As-built §25 | MATCH | Pairwise complexity remains a performance issue, not a semantic fallback. | Validator |
| 65 | Renderer displays the physical scene and consumes waypoints; it does not route or allocate. | Internal `ArchitectureV7MechanicalDrawioRenderer.Render` emits Draw.io nodes, containers, External shapes, and connector waypoints. | Production service/internal renderer | `ArchitectureV7ProductionGenerationService.cs` | MATCH | Renderer-side routing would create a second geometry authority. | Renderer |
| 66 | Configured styling is preserved mechanically by renderer. | Known current renderer styling ignores or hardcodes some node/project/External/connector/font/canvas settings. | `ArchitectureV7MechanicalDrawioRenderer` | As-built §26 | **MISMATCH** | Visual output differs from preserved user configuration. | Renderer-only styling tranche |
| 67 | Provenance survives every freeze and identifies the owning authority. | Fingerprints and provenance strings/records exist through projection, ownership, placement, routing, allocation, scene, and evidence. | All V7 freeze models; `ArchitectureV7RoutingEvidenceStage` | As-built §27 | ACCEPTABLE IMPLEMENTATION DETAIL | Global/null-subject diagnostics can make root attribution difficult. | Evidence/provenance |
| 68 | Evidence observes production decisions and is bounded. | Routing evidence consumes route attempt evidence and summarizes candidates; it has representative limits and does not route independently. | `ArchitectureV7RoutingEvidenceStage` | As-built §27; prior diagnostic tranche | MATCH | Unbounded evidence could become a second search authority. | Evidence stage |
| 69 | Deterministic ordering is explicit at every stage. | IDs, project order, duplicate order, children, roots, External, standalone, routes, candidates, lanes, terminals, bends, crossings, and output are sorted or FIFO-preserved. | Stage-specific comparers | As-built §28 | ACCEPTABLE IMPLEMENTATION DETAIL | Any missing comparer can change fingerprints and layout. | Affected stage |
| 70 | Post-freeze stages cannot change placement, spans, logical grid, or route cells. | Later APIs consume freeze products; compiler/validator emit diagnostics rather than mutate topology. | Freeze model boundaries | Production orchestration | MATCH | A hidden mutation would invalidate all downstream evidence. | Freeze boundary |

## Priority findings

### Reservation coordinate domains — MISMATCH

The current code uses at least five integer domains, but only some are named:

```text
natural semantic depth
reservation RequiredNodeRow = depth*2+1
recursive layer = frozen NodeRow
tree-local row = layer*2
project/common row = InteriorOriginRow + localRow + 1
External common row = frozen External.NodeRow*2+3
```

This proves that a reservation value such as 17 is not the final logical node row. It is first treated as a layer and doubled again. The current CMS therefore places reserved-role occurrences at final rows 13, 17, 25, 29, and 33, while External 17 maps to 37. The implementation has no typed conversion boundary proving that these are intentionally different domains. The earliest authority to correct, if confirmed against the intended algorithm, is the inspector/recursive-placement boundary—not a downstream row adjustment.

Required reduced regressions before any repair:

1. natural depths 0–7 map to exactly one named reservation domain;
2. each reserved group lands on the intended final common row;
3. External frozen row and final row have one explicit conversion;
4. continuation rows 17, 25, 29, and 33 are either intentional tree rows or are rejected as double conversion;
5. reservation, tree, placement, and route fingerprints are expected and documented.

### External row conversion — MISMATCH pending coordinate decision

The formula `External.NodeRow*2+3` is internally consistent with the current recursive/local/project mapping, but it is not a direct use of a frozen final logical row. It must be resolved together with the reservation-domain finding above. No downstream compensation is authorized.

### Standalone packing — AMBIGUOUS — CODE REVIEW REQUIRED

The current formula uses actual spans and one gap, but the square-root metric sums only horizontal `span+1` demand. Because node rows are separated by routing rows, a normative “square-ish” metric that includes both axes could produce a different target width and row count. The current code cannot be called a mismatch until the intended shape metric is stated mathematically.

Required reduced regressions:

- spans `[3,3,3,3]`, `[3,5,7]`, and a wide-span mix;
- target width and break positions;
- row count including inserted routing rows;
- one gap between every adjacent node footprint;
- External-to-standalone and standalone-to-standalone row parity.

### Maximal run → lane cardinality — MATCH

The audit's 2,202 combined lane/assignment count is not 2,202 assignments. `AllocateLanes` emits one assignment per 1,299 run and separately materialises lane models. No run changes lane internally. This priority concern is not a demonstrated mismatch.

### Crossing interaction vs physical resource — MISMATCH

The allocation model currently combines pair provenance and physical resource records in `ArchitectureV7CrossingAllocation`. `BuildCrossingResources` creates candidate resources from horizontal/vertical pass and turn entries, and a dense cell can emit 405 records. There is no independent interaction collection that can map many interactions onto one physical crossing slot. The earliest authority is the collective allocator. The expected correction is a model-level distinction between interaction evidence and physical resource/slot allocation, preceded by reduced dense-cell regressions.

Required regressions:

- two route pairs sharing one valid physical crossing slot;
- two interactions requiring distinct offsets in one cell;
- pass/pass and turn/pass classification;
- capacity based on distinct physical offsets, not raw pair count;
- stable interaction-to-resource provenance and allocation fingerprint expectations.

### Compiler duplicate authority — MISMATCH

`MaterialiseRoutes` should be mechanical, but `PointFor` performs repeated global searches. `FindCrossing` linearly scans the frozen crossing list, and `RequiresCrossing` scans all other routes and all route cells to rediscover whether a crossing exists. This is not a semantic route change, but it is a direct violation of single-owner resource allocation and explains the >45-second incomplete materialisation.

The smallest correction is a compiler-only indexed lookup over frozen runs, assignments, terminals, handoffs, bends, and crossings. It must preserve the existing physical point formula and fingerprints where lookup order is semantically irrelevant. It must not be implemented in this matrix checkpoint.

## Fingerprint expectations for future checkpoints

| Potential correction | Expected unchanged fingerprints | Expected changed fingerprints |
|---|---|---|
| Reservation coordinate correction | projection, ownership, sizing | reservation, tree, placement, routes, allocation, scene, acceptance evidence |
| Standalone shape correction | projection, ownership, sizing, reservations, ordinary tree fingerprints | placement onward; routes may change if standalone links exist |
| Lane cardinality clarification with no semantic change | all fingerprints | none, if current one-run assignments are confirmed |
| Crossing interaction/resource model | projection through routes | allocation, scene, acceptance, evidence; route fingerprint should remain unchanged |
| Compiler indexing only | every frozen planning/allocation fingerprint | none; only elapsed time and compiler diagnostic timing should change |
| Styling-only future tranche | all geometry/planning fingerprints | renderer/fidelity output only |

## Review gate

The matrix identifies two demonstrated mismatches requiring review before implementation:

1. reservation/placement coordinate-domain ownership;
2. crossing interaction versus physical-resource modelling;
3. duplicated crossing discovery in the physical compiler.

It identifies two areas requiring a normative decision rather than immediate code change:

1. standalone square-ish packing metric;
2. combined capability precedence where `GeneralRouting` and restrictive flags coexist.

No V7 repair, optimization, styling work, validator-performance work, or production regeneration is authorized by this checkpoint.

## V7 Contract Compliance

This checkpoint is review/documentation-only. No production behavior or acceptance rule was changed. Existing unrelated V6/generated worktree changes were left untouched. The matrix distinguishes implementation facts from contract expectations and stops before any corrective implementation.
