# Architecture, migration, and visual-output review

Date: 2026-07-19

Review baseline: `322548a`

Reviewed production: `69eed93`
Scope: Draw.io placement, routing, migration, diagnostics, and normal CLI generation.

## Executive conclusion

The shortest route back to visible progress is not another solver extension. It is a complete-region production slice. The present common-authority trial can regenerate link topology, but it begins with a legacy-produced `RenderLayout`, retains legacy placement around it, and validates the result in a mixed geometry graph. Under the strict definition in this review, **zero regions are currently common-owned from placement through serialization**.

The most useful next slice is an **isolated project-local dependency tree**: own its project placement, subtree placement, routes, validation, and serialization together; fall back for the entire project when it is not closed or supported. This removes dense link-by-link common/legacy contact, produces a diagram worth looking at, and creates a real deletion boundary.

Normal production was not changed. The only production-source deletion was an unreferenced enum. Normal output remained byte-identical.

## Evidence and method

- Real graph: deduplicated `cCoder.ContentManagement`; neutral JSON contains 1 project, 379 semantic types, 14 external dependencies, and 340 semantic edges.
- Current trial report: 294 route capabilities, 29 eligible closed route components, 0 accepted complete regions, 294 legacy routes retained.
- Earlier mixed trial: 255 eligible routes, 28 closed components, 26 accepted link components, 1 pre-execution mixed-topology rejection, 1 mixed-movement rejection, and 1 post-validation hard-geometry rejection. These were route migrations, not end-to-end region ownership.
- Smaller representative output: `artifacts/review-audit/standardio-normal.drawio` (44 final nodes, 48 logical routes after render duplication). Its development trial fails before output with `ArgumentOutOfRangeException (proposal)`, demonstrating that the trial harness is not an independent general renderer.
- Current routing/layout inventory: 92 source files and 13,184 lines in Draw.io model/processing directories; 65 Core test files and 8,781 test lines; 412 focused Core tests.
- Type lifetime counts below are a review classification of top-level Draw.io routing/layout types, not a quality score: 78 permanent target, 72 migration-only, 10 diagnostic-only, 42 retained legacy, and 70 uncertain/supporting or shared.

## 1. Legacy-comparison audit

| File / symbol | Comparison | Consumer | Execution effect | Classification | Decision |
|---|---|---|---|---|---|
| `AdjacentDownwardLinkDemandDiscovery.Discover` | common reconstruction versus authoritative legacy points/topology | observer and trial report | parity label only; inability to reconstruct affects capability | `LinkPathParity`, `GeometryCoordinateParity`, `DiagnosticBeforeAfterOnly` | Rewrite: retain topology/capability result; delete exact-point expectation after first complete production slice. |
| `AdjacentDownwardCommonAuthorityObserver.Observe` | lane, coordinate, and reconstructed path versus existing mappings | diagnostic report | no hard rejection for `ValidDifferentGeometry` | `GeometryCoordinateParity`, `LinkPathParity` | Narrow to invariant validation; keep lane comparison only as migration telemetry. |
| `CommonAuthorityParityReport` and parity enums | exact/equivalent/unmappable summaries | diagnostic JSON/tests | diagnostic only | `GeometryCoordinateParity`, `DiagnosticBeforeAfterOnly` | Delete with next production slice once that slice has independent fixtures. |
| `AdjacentDownwardComponentProjector.Project` | common-capable routes plus interactions with selected legacy segments | trial eligibility | can enlarge/reject migration component | `SemanticLinkParity`, migration boundary comparison | Retain only while mixed route authority exists; replace with complete-region closure. |
| `CommonAuthorityComponentClassifier.Classify` | supported/unsupported routes sharing legacy interaction closure | trial gate | pre-execution reject | migration boundary comparison | Rewrite around region semantic/geometric closure. |
| `MixedBoundaryAttributor` | common/legacy contacts and simulated unlocks | trial diagnostics | diagnostic only | `DiagnosticBeforeAfterOnly`, `CrossingComparison` | Retain for one migration slice, then delete when project fallback replaces link mixing. |
| `DevelopmentCommonAuthorityTrial` before/after quality | bends, length, findings, bounds | report and evidence | bend/length warnings do not accept/reject; hard mixed validation can reject | `BendCountComparison`, `LengthComparison`, `DiagramBoundsComparison`, `DiagnosticBeforeAfterOnly` | Keep labelled before/after only; legacy values must never be expected values. |
| `DevelopmentCommonAuthorityTrial.ValidateCandidate` | common candidate in full graph containing retained legacy paths | rollback gate | rejects new hard findings | `SerializationCompleteness`, mixed geometry validation | Split findings into intrinsic-common and boundary findings; use region fallback for boundary invalidity. |
| `RenderLayout.LegacyRoutingPipeline` adapters | legacy `LinkLayout` projected to canonical contexts | all common development paths | common development cannot operate without legacy layout | `SourceDestinationParity`, `StableIdentityParity`, `LinkPathParity` | Preserve semantic/identity projection only; eliminate path projection per production slice. |
| exporter before/after serialization | cell count, identities, metadata, ownership | trial and serializer tests | serialization correctness | `SemanticNodeParity`, `SemanticLinkParity`, `StableIdentityParity`, `DrawIoOwnershipParity`, `SerializationCompleteness`, `Determinism` | Retain as independent semantic/serialization assertions. |

Semantic identity, source/destination, ownership, complete serialization, and determinism remain correctness requirements. Coordinates, paths, bends, lengths, crossings, bounds, order inherited only from legacy, corridor assignments, and legacy repair output are not.

Recommended geometry-gate rewrite: validate common geometry first in the proposed region; separately classify contacts at the boundary; reject/fallback at the region boundary rather than requiring the new interior to resemble retained geometry.

## 2. Mixed common/legacy validation

The most informative real report is `artifacts/authority-final-attribution/trial-report.json`:

| Category | Count | Evidence / interpretation |
|---|---:|---|
| `CommonGeometryInvalid` | 1 | `HardGeometryFinding`; the candidate introduced a strict geometry finding. |
| `LegacyGeometryInvalid` | 0 | Existing legacy findings are baselined and do not alone reject a candidate. |
| `BothGeometriesIndividuallyValidButBoundaryInvalid` | 0 explicitly isolated | The validator records full-graph new findings, but the report does not persist enough ownership detail to prove this category independently. This is a reporting gap, not evidence of zero contacts. |
| `CommonGeometryValidButMigrationUnitTooSmall` | 2 | One supported/unsupported interaction closure and one mixed movement boundary. |
| `SemanticOrSerializationMismatch` | 0 | No such real rejection in the report. |

The latest report moves all 29 route components under one positional movement proposal and rejects it before execution because of an incomplete/positive-cycle movement closure. That is **not proof that the target rules are inconsistent**. The constraints include retained placement and incident legacy geometry outside the proposed authority. It is evidence that the link/component authority unit is too small for placement movement.

## 3. Intended lifetime

| Subsystem | Lifetime | Why / deletion event |
|---|---|---|
| positional hierarchy, subtree envelopes, natural sizing | `PermanentTargetArchitecture` | Core placement model. Retain, but validate through complete-region fixtures. |
| connection allocation, inter-layer bands, deterministic slots, vertical/return columns | `PermanentTargetArchitecture` | Express visible topology and spacing. |
| corridor observation/lane allocation/traversal and repair coordination | `RetainedLegacyProduction` | Current normal renderer. Deleted only when every production region using it has a replacement. |
| persistent constraints, difference constraints, alternative component solver, revision/invalidation | `TemporaryMigrationInfrastructure` | Reconcile common route demands with retained placement/mixed authority. A whole-region layouter may need simpler constraints, but not this coexistence machinery. |
| common/legacy context and segment projections | `TemporaryMigrationInfrastructure` | Required because common work starts from legacy routes. Delete as each region becomes independently placed/routed. |
| parity models, mixed-boundary attribution, rollback evidence, trial orchestration | `DiagnosticOnly` | Decision and migration evidence. Delete/narrow after one region reaches production. |
| `LegacyRoutingPipeline`, legacy selectors, traversal projection, repair passes | `RetainedLegacyProduction` | Still owns all normal routes. |
| standalone/disconnected-node placement | `PermanentTargetArchitecture` but not production-integrated | It owns placement for a supported family; the development trial invokes it after legacy production. Integrate through project-region ownership. |
| ownership compiler and multi-segment Draw.io serializer | `PermanentTargetArchitecture` | Required manual-movement semantics and exact logical reconstruction. |

Risk of accidental permanence: the component projector, mixed-boundary attributor, parity reports, movement-closure models, alternative solver, revision tracking, and rollback reports depend on each other. Continued link-level expansion makes this migration stack self-justifying.

Largest migration/diagnostic concentrations by implementation size and dependency reach are: `DevelopmentCommonAuthorityTrial`, pre-assignment movement models, difference-constraint materialization, component alternative solver, positional movement planner, persistent constraint store, component projector/classifier, mixed-boundary attribution, parity discovery/observer, and trial report/evidence models. Every one becomes smaller or disappears when authority is a complete project/region rather than selected links inside a legacy layout.

## 4. Architecture cost

| Measure | Count |
|---|---:|
| Permanent target types | 78 |
| Migration-only types | 72 |
| Diagnostic-only types | 10 |
| Retained legacy routing types | 42 |
| Uncertain/shared supporting types | 70 |
| Adapters/projections (named, active) | 9 |
| Constraint-related top-level types | 31 |
| Alternative-search top-level types | 8 |
| Routing/layout source files | 92 |
| Routing/layout source lines | 13,184 |
| Migration/diagnostic source lines (filename/symbol classification) | about 3,850 |
| Focused Core routing/layout tests | 412 tests across 65 files |
| Legacy geometry-parity test assertions | 4 direct exact/equivalent parity assertions |
| Independent target-invariant tests | majority of geometry tests; not separately tagged, so exact count is not defensible |

The important concentration is not total size: roughly one third of active target-area code exists to project, compare, close, move, invalidate, search, and roll back mixed legacy/common geometry without yet owning one complete production region.

## 5. Canonical rule decisions

| Rule | Class | Benefit / rationale | Recommendation |
|---|---|---|---|
| source links exit bottom; destination links enter top; no side connections | `HardInvariant` | consistent reading direction and terminal traceability | Keep. |
| no node intersections | `HardInvariant` | prevents false visual attachment | Keep. |
| no shared non-zero link segments | `HardInvariant` | preserves link identity | Keep. |
| no diagonals in generated output | `HardInvariant` | exporter promises orthogonal routes | Keep; manual post-move diagonals remain accepted. |
| no immediate reversals | `HardInvariant` | removes loops/ambiguous topology | Keep. |
| clean perpendicular crossings allowed | `ConfigurablePolicy` | compactness versus crossover readability | Keep allowed and diagnose. |
| non-leaf movement includes complete subtree | `StrongPreference` | preserves hierarchy | Make hard only inside an owned region. |
| sibling/project order preserved | `StrongPreference` | mental map stability | Do not preserve order solely because legacy chose it. |
| unrelated subtrees do not interleave | `HardInvariant` | hierarchy comprehension | Keep. |
| parent umbrella placement | `StrongPreference` | makes children visually attributable | Keep, allow evidence-backed exceptions. |
| long downward route has one horizontal departure and destination-midpoint column | `StrongPreference` | predictable trace | Keep as scorer, not validity. |
| no second horizontal arrival for long downward links | `NeedsDecision` | lowers bends but may force excessive width | Compare fixtures before hardening. |
| return columns ownership-envelope local; same/upward links use exterior topology | `StrongPreference` | separates returns from downward flow | Keep with bounds evidence. |
| inter-layer slots set horizontal Y; columns provide horizontal clearance | `PermanentTargetArchitecture` policy | deterministic lanes | Keep implementation concept, not legacy coordinates. |
| horizontal compactness removes unowned gaps | `StrongPreference` | avoids unexplained canvas waste | Measure and score. |
| atomic regeneration/rollback | `HardInvariant` during migration | production safety | Retain at region granularity; delete link-level machinery later. |
| byte-identical normal production during development | `MigrationOnlyRule` | protects inactive experiments | Drop as visual goal when a production slice is approved; retain deterministic repeatability. |
| link-level mixed authority | `LegacyAccident` | incremental implementation convenience | Replace. |
| whole-closure movement requirement | `MigrationOnlyRule` | avoids stale incident legacy routes | Naturally satisfied by complete-region ownership. |

## 6. Authority-unit evaluation on cCoder

Counts are based on the 294-route current trial and its 29 closed route components / 8 projected common regions.

| Unit | Candidate regions / capable now | Boundaries and ownership | Assessment |
|---|---|---|---|
| individual link | 294 / 294 topology-capable, 0 complete | maximum geometric contacts; placement cannot be owned | Reject. Dense mixing and no deletion boundary. |
| conflict component | 29 / 29 topology-capable, 0 complete | fewer contacts, but incident movement crosses components | Better diagnostic unit, poor production unit. |
| positional root subtree | 8 projected roots / 0 complete | coherent movement; semantic links cross roots | Viable only when boundary links are explicitly root-owned and exterior. |
| semantic connected component | 1 dominant real component / 0 complete | semantically closed but effectively whole project | Too coarse for cCoder; useful in smaller graphs. |
| project | 1 / 0 complete | explicit semantic/Draw.io boundary; owns placement and serialization | Best default slice boundary when project-local closure holds. |
| contiguous project span | 1 in this single-project evaluation / 0 | useful for multi-project cross-links | Second-stage option. |
| complete geometry-interaction region | 8 observed / 0 complete | best geometric closure, but current discovery depends on legacy paths | Good fallback refinement after project slice, not first foundation. |
| whole diagram | 1 / 0 complete | no mixed geometry; one unsupported case blocks everything | Keep as eventual target, not next slice. |

Recommended unit: **project-local closed positional forest**, serialized as one project region. Eligibility: all internal nodes are placeable, every internal link topology is supported, and boundary links can be represented as explicit root-owned exterior connections without traversing the project interior. Fallback is the entire project, never individual links.

## 7. Complete common regions

Count: **0 under the required end-to-end definition**. The 26 historically accepted components regenerated 27 links, but inherited node/project coordinates and route context from `LegacyRoutingPipeline`; they therefore do not count.

Available partial evidence:

- `docs/evidence/common-rail-real-component`: valuable route-regeneration before/after evidence, not placement ownership.
- `docs/evidence/common-authority-full-trial`: mixed migration evidence, not a complete renderer.
- `artifacts/review-audit/standardio-normal.drawio`: current normal visual baseline.
- `artifacts/review-audit/ccoder-normal-run1.drawio`: real normal baseline; 294 logical routes and stable hash.

The absence of a complete region is the review's main architectural finding, not a reason to expand the universal solver.

## 8. Independent visual acceptance catalogue

All fixtures assert semantic node/link identity, bottom/top terminals, no side contacts, node clearance, no shared non-zero segments, orthogonality, configured spacing, deterministic output, and serializer reconstruction. Legacy snapshots may be stored only as `legacy-before.drawio`.

| Fixture | Additional independent assertion |
|---|---|
| simple parent-child chain | zero unnecessary bends; compact vertical span |
| one parent/many children | monotonic fan-out and minimum lane spacing |
| uneven-depth sibling subtrees | contiguous subtrees; no interleaving |
| multiple layers/compact sibling trees | no unowned horizontal gaps |
| multi-parent canonical node | one stable node; all incoming routes traceable |
| adjacent downward links | deterministic distinct slots |
| long downward links | exterior obstacle clearance; bounded bends |
| destination-aligned columns | separated columns and stable ordering |
| same-layer returns | envelope-local exterior return |
| upward returns | exterior topology with bottom exit/top entry |
| cross-project links | root boundary ownership and exact reconstruction |
| clean perpendicular crossings | crossing permitted only away from nodes/bends |
| parallel-spacing pressure | no overlap; configured spacing or diagnosed capacity failure |
| disconnected-node project | natural sizing and compact deterministic placement |
| dense real-project extract | independent findings/bends/dimensions recorded |
| mixed-topology stress graph | atomic project fallback; no link-level mixing |

The catalogue should become executable in the next slice. Today these cases are distributed across unit tests; they lack one production-like fixture runner and a visual index.

## 9. Visual blockers, ranked

1. **No complete ownership boundary**: prevents judging new placement and routing together; affects all 379 real types and 294 rendered dependencies; solving it unlocks project-local regions and deletes projections/parity/mixed rollback code.
2. **Normal renderer still has strict geometry findings**: real baseline reports 4 node intersections, 54 shared segments, 26 spacing deficits, and 3 reused bends. These are true target defects in legacy production and the visible work the next slice should attack.
3. **Trial harness is coupled and brittle**: the StandardIo representative project throws on an invalid proposal; this blocks frequent small-graph visual review.
4. **Positive positional cycle**: affects the all-route movement proposal. Evidence points to preserved legacy placement and excluded incident ownership, so classify as mixed-authority limitation until reproduced inside a wholly owned region.
5. **Exact geometry parity telemetry**: distracts from invariant-based output assessment but currently blocks little execution.

Top visible-impact blocker: complete project-region authority. Top deletion-value blocker: removal of legacy-path projections and mixed rollback after that slice.

## 10. Constraint proportionality

| Capability | Current delivered value | Final or migration | Recommendation |
|---|---|---|---|
| persistent constraints / revisions | deterministic stale-state prevention in trials; no normal output | mainly migration | Defer expansion; replace with immutable per-region solve state if possible. |
| column-envelope and difference constraints | proves clearance/movement requirements | partly final, largely coexistence | Keep concepts; simplify representation inside owned region. |
| return-slot allocation | real supported topology evidence | final | Keep. |
| positive-cycle detection | prevents invalid movement | final safety mechanism | Keep a simple detector; do not treat mixed cycle as target impossibility. |
| component alternative search | exhaustive blocker proof; 0 accepted production output | migration | Freeze; likely delete after project-region layout. |
| invalidation/revision system | protects iterative mixed proposals | migration | Simplify/delete with atomic region generation. |
| atomic rollback | essential safety | final at region boundary | Keep, change granularity. |

## 11. Concrete deletion audit

Deleted now:

- `HorizontalDifferenceConstraintKind`: zero references; 1 type and 8 lines removed.
- No files, adapters, diagnostics, or tests deleted.
- Focused tests: 412/412 pass.
- StandardIo normal Draw.io hash before/after: `90C1BEDA7DE198B6025EAF789E2B71F2321C39116A77A8FA42AC2A560A4C74F3`.

Next classifications:

- `DeleteAfterThisReview`: none safely proven without weakening active trial evidence.
- `DeleteWithNextProductionSlice`: exact geometry parity enums/report, legacy segment mapping projection for migrated projects, mixed-boundary attribution for those projects, link-level component classifier/projector, alternative movement search for project-owned regions.
- `RetainForNormalProduction`: `LegacyRoutingPipeline`, corridor/lane routing, selectors, repair, ownership compiler, serializer.
- `RetainForActiveDevelopment`: trial orchestration and rollback until the first complete project slice replaces them.
- `RetainForPublicCompatibility`: serialized logical-edge ownership metadata and stable IDs.

Net migration-only line change: 0. The deleted enum was orphaned supporting code.

## 12. Exactly three production-capable slices

### 1. Isolated project-local dependency tree — recommended

- Authority boundary: one project containing a closed positional forest.
- Eligibility: supported internal topology; external/boundary semantic links attach through explicit project boundary contacts; no unrelated route traverses the interior.
- Ownership: project placement, nodes, internal routing, validation, and serialization are common-owned.
- Fallback: entire project to legacy.
- Candidate: a reduced dense cCoder project-local extract plus a smaller StandardIo project fixture.
- Visible improvement: remove the measured intersections/shared segments/spacing defects inside the region; compact subtree layout.
- Deletion: migrated-project legacy routing invocation, path projections, mixed link rollback/parity.
- Risk/effort: medium; strongest output/deletion ratio.

### 2. Disconnected-node project

- Authority boundary: project with no semantic links.
- Eligibility: all project nodes disconnected; no crossing boundary link.
- Ownership: natural sizing, placement, bounds, and serialization; routing is empty.
- Fallback: whole project.
- Candidate: existing disconnected-node fixture and any real utility/model-only project.
- Visible improvement: compact deterministic grid/forest and correct project bounds.
- Deletion: legacy standalone-node placement for eligible projects.
- Risk/effort: low, but limited visual/routing value.

### 3. Fully supported contiguous project span

- Authority boundary: two or more adjacent projects whose internal and cross-project links are all supported.
- Eligibility: semantic closure except explicit root-external links; no third-party route crosses span.
- Ownership: all project placements, cross-project routing, root segments, ownership segmentation, serialization.
- Fallback: entire span.
- Candidate: a two-project cross-link fixture, then a reduced real solution span.
- Visible improvement: coherent cross-project columns and fewer root-level collisions.
- Deletion: legacy cross-project routing and mixed ownership projections for the span.
- Risk/effort: high; follows slice 1.

## 13. Exact next diagram-generation pass

1. Build an executable `project-local-closed-tree` fixture from: parent-many-children, uneven siblings, long downward, same-layer return, upward return, canonical multi-parent target, and one external boundary link.
2. Add a reduced cCoder extract preserving one region that currently exhibits a node intersection/shared segment/spacing deficit.
3. Generate three artifacts every iteration: legacy-before, common-after, and invariant JSON. Legacy geometry is not asserted.
4. Expected initial ownership: 1 project, approximately 12–25 nodes and 12–30 links; exact count becomes fixed in the checked-in fixture manifest.
5. Acceptance: semantic identity complete; bottom/top contacts; no node intersection/shared segment/diagonal/immediate reversal; configured spacing; subtree contiguity; deterministic bytes; lower or equal bends and dimensions only as reported preferences.
6. Bypass `LegacyRoutingPipeline` entirely for the eligible project. Delete its project-specific path projection, parity, and link-level rollback consumers after green evidence.
7. Fallback atomically to the whole legacy project on unsupported topology or hard finding.
8. Performance gate: common project generation no slower than 2x legacy for the fixture and no material regression on full cCoder normal generation.
9. Production decision point: manual inspection of both diagrams plus invariant JSON; enable only the closed-project eligibility rule, not a broader authority expansion.

Working rhythm: generate, inspect, attribute to invariant, fix, regenerate, delete superseded code.

## 14. Controlled performance sanity check

Machine/session: Windows, .NET SDK 10.0.103, Release, one CLI process at a time. Solution was built once; three cCoder runs used `--no-build`, identical input/settings, serialization repeat 3. The first run serves as warm-up and was retained.

| Run | elapsed | layout/routing | SHA-256 |
|---|---:|---:|---|
| 1 | 20,044 ms | 16,833 ms | `08D70BBA...DAB7F6` |
| 2 | 20,055 ms | 16,776 ms | `08D70BBA...DAB7F6` |
| 3 | 19,905 ms | 16,653 ms | `08D70BBA...DAB7F6` |

All 61 measured normal-path phases were inspected. No common-authority trial, positional alternative search, or pre-assignment movement phase executed. Normal generation does execute the retained legacy candidate/global-selection/corridor/lane/repair pipeline. Because the sole baseline-to-reviewed production change deletes an unreferenced enum and the output hash is identical, a second checkout benchmark would measure host variance rather than a code-path difference.

## 15. Future progress report

Every pass should lead with: diagrams/regions newly common-owned; nodes/links owned; findings, bends, crossings, and dimensions before/after; visible defects and evidence. Follow with types/files/permanent and migration lines added/deleted, duplicate authorities and legacy responsibility removed. Then report authority unit, boundary links/contacts, fallback granularity, removed legacy invocations, user-visible delivery, and the next smallest defect.

## Decisions requiring approval

1. Approve project-local closed positional forests as the first production authority boundary.
2. Treat long-route bend shape and sibling ordering as preferences unless an independent visual rule makes them hard.
3. Accept project-granularity fallback in exchange for eliminating dense link-level mixed authority.
