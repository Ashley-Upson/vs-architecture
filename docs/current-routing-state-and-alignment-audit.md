# Current routing state and alignment audit

Date: 2026-07-18. This is an audit of existing behaviour only. It does not change routing, layout, compatibility, performance, or packaging.

## Executive finding

Production currently contains two mutually exclusive post-classification routing authorities, but the new grouped authority supports only a whole graph made exclusively from ordinary orthogonal, adjacent downward routes with exactly one band membership and at least one missing extent. Deduplicated cCoder does not reach group planning: the complete graph falls back to legacy routing before any grouped mutation. Consequently Stage C has no effect on its final geometry. The accepted Stage B and current Stage C-fallback XML files are byte-identical.

The principal design disconnect is that the eleven visibility rules are universal product requirements while production fallback is permitted to emit diagnosed violations. The new grouped model is also substantially narrower than its types and documentation might suggest: only vertical adjacent-layer expansion is active; horizontal/project constraints, persistent cross-iteration constraint state, group-level validation/rollback, and parallel merge are absent.

## 1. Repository state

```text
current branch: feature/decuplicate-node-option
production HEAD before this audit commit: e1d1f078b73e30d0f75fe01b36761639be2c922f
working tree at audit start: clean
tracking status: ahead of origin/feature/decuplicate-node-option by 26
relevant branches: feature/decuplicate-node-option, codex/experimental-corner-separation,
                   v3/routing-refactor, main
experimental HEAD: 79f6c88122e5cefe92e4332431145e0491d221cd
production/experimental merge base: 50ed465163c791063515dc29fbe5f89e7aab84f0
```

`codex/experimental-corner-separation` contains `79f6c88`. Its `OverlappingCornerSeparator`, tests, geometry helpers and report changes are absent from production. No useful primitives or tests exist only as untracked files; the initial production tree was clean.

Files differing directly between production and experimental are:

- production-only: `docs/grouped-spacing-stage-c-slice.md`, `GroupedSpacingModels.cs`, `BandConflictGrouper.cs`, `GroupedVerticalBandPlanner.cs`, `MonotonicSpacingConstraintStore.cs`, and their two test files;
- experimental-only: `OverlappingCornerSeparator.cs` and `OverlappingCornerSeparatorTests.cs`;
- different on both: the simplified design document, `DeterministicDrawioExporter.RenderLayout.cs`, `DrawioDiagnosticReportBuilder.cs`, `OrthogonalGeometry.cs`, and `LegacyRoutingPipeline.cs`.

Commits after the accepted Stage B documentation baseline `3c87724`, in chronological order:

| Commit | Real runtime effect |
|---|---|
| `b125ec0` | Adds diagnostic evidence tracing unsupported band diagonals; routing output is unchanged. |
| `50ed465` | Records a Stage C readiness stop; documentation only. |
| `fd0f6e8` | Replaces the proposed Stage C direction in documentation; no runtime effect. |
| `706dd19` | Adds grouped constraint, conflict and planner models plus fixtures, but does not put them on generation's runtime path. |
| `7b475d1` | Adds eager Stage B observation and the whole-graph compatibility branch; supported graphs use grouped vertical-band regeneration, rejected graphs continue through legacy routing. |
| `e1d1f07` | Records Stage C evidence; documentation only, including the erroneous hash transcription resolved below. |

## 2. Accepted baselines and hash resolution

The controlled comparison used Release output, cCoder source revision `459dffa707954152713f473d9c18c57f6efc4da4`, settings `artifacts/node-duplication/real-project-deduplicated-settings.json`, Draw.io renderer, normal output, and local NTFS destinations. The cCoder source checkout had four unrelated modified application settings files during this audit; both compared outputs use the same source tree and the diagram settings are the recorded repository artefact.

| File | SHA-256 | Bytes |
|---|---|---:|
| accepted Stage B `artifacts/stage-b/ccoder-deduplicated.drawio` | `8A7C26A1FE8B4A7460996FDBA643DBDFDC603F80CCE215A34715988E0E72D1DE` | 12,455,618 |
| current `artifacts/stage-c-grouped/ccoder-deduplicated.drawio` | `8A7C26A1FE8B4A7460996FDBA643DBDFDC603F80CCE215A34715988E0E72D1DE` | 12,455,618 |

The files are byte-identical, so semantic/XML diff is empty. The reported `...DC7A0C...` value never describes either file; it was a transcription error in `docs/grouped-spacing-stage-c-slice.md`, not another settings file, revision, mode, XML change, or stale artefact.

## 3. Timing resolution

`scripts/benchmark-generation.ps1` ran one excluded warm-up and five measured fresh Release CLI processes in normal mode, with diagnostic JSON, thread telemetry, performance audit and serialization repeats disabled. Results are in `artifacts/current-state-audit/benchmarks/ccoder-deduplicated-current-audit-normal/results.csv`.

```text
warm-up: 14,653 ms
measured: 14,179; 14,540; 14,034; 14,279; 14,151 ms
minimum: 14,034 ms
median:  14,179 ms
maximum: 14,540 ms
hashes:  1 distinct
```

This agrees with the accepted approximately 14-second baseline. The reported 22.817 seconds came from a diagnostic/performance run with additional instrumentation and serialization repeats, not normal generation.

A separate phase-accounting run (`ccoder-phase.perf.json`) deliberately enabled performance output and two serialization repeats. It took 19,970 ms and must not be compared as normal latency. Its principal phases were:

| Phase | Time | Invocations |
|---|---:|---:|
| workspace/project acquisition | 607.922 ms | 1 |
| semantic analysis | 1,782.708 ms | 1 |
| Roslyn compilation acquisition | 544.856 ms | recorded within analysis |
| render graph construction | 13.864 ms | 1 |
| exposure/canonical construction | 5.949 ms | 1 |
| placement | 65.715 ms | 1 |
| layout/routing total | 16,504.616 ms | 1 |
| outer `legacy route generation` phase | 16,437.402 ms | 1 |
| candidate construction/selection | 11,405.736 ms | 4 |
| regional optimisation | 6,170.110 ms | 4 |
| repair passes | 4,165.754 ms | 1 |
| top-level normalization | 14.167 ms | 2 |
| top-level validation | 463.347 ms | 2 |
| ownership compilation/rebase | 14.653 ms | one preparation path |
| project bounds | 4.297 ms | one preparation path |
| XML serialization | 297.614 ms | 1 |
| file write | 303.600 ms | 1 |
| extra serialization repeats | 348.284 ms | 2 |

The grouped route phase was invoked zero times; legacy routing was invoked once. Stage C compatibility does run the Stage B observer before fallback, but that cost is tens of milliseconds in prior observation measurements, not the multi-second discrepancy. There is no dedicated performance phase around classification, so exact observer/classifier separation is a telemetry gap.

## 4. Actual runtime call graph

```text
CLI Program / Visual Studio command
  -> workspace acquisition (lightweight loader / VisualStudioWorkspace)
  -> diagram analysis
  -> DeterministicDrawioExporter.GenerateResult / renderer.Render
     -> Prepare
        -> normalize settings
        -> RenderGraph.From
        -> RenderLayout.Build
           -> PlacementPipeline.Place                         [Stage A]
           -> LegacyRoutingPipeline.Run
              -> PositionLinks                                [provisional]
              -> GeneratedLogicalRoutes(revision 0)
              -> InterLayerBandObserver.Observe               [Stage B]
              -> GroupedVerticalBandPlanner.Supports          [Stage C gate]
                 true -> RunGroupedVerticalBands
                         -> Apply / revise placement / regenerate routes
                         -> re-observe until no missing extent (max 16)
                         -> LogicalRouteNormalizer.Normalize
                         -> TraceabilityValidator.Validate
                 false -> CorridorObserver.Observe
                          -> CorridorLaneAllocator.Allocate
                          -> optional capacity expansion and reroute
                          -> CorridorLaneGeometryCompiler.Compile
                          -> EdgeTraversalCompiler.Compile/Apply
                          -> normalization / validation
                          -> optional layer expansion and reroute
                          -> RouteRepairCoordinator.Repair or CompileOnly
        -> coordinate-ownership compiler
        -> project bounds/rebase loop
        -> DiagramFileBuilder / XML construction
  -> file write
  -> optional ExportDiagnostic
     -> reuses prepared layout and ownership
     -> InterLayerBandObserver.Observe(final route set)         [no reroute]
```

The exact compatibility condition is `GroupedVerticalBandPlanner.Supports`: placement/routes revisions must match; unsupported-shape count must be zero; at least one band must have positive missing extent; no node ID may begin `tree_`; and every route must connect depth `d` to `d+1` and have exactly one observed membership. Its decision scope is the whole graph.

True selects grouped authority and never runs legacy corridors or repair. False performs no grouped geometry change and enters the full legacy path. Both branches consume the same provisional `PositionLinks` result, so the same graph is touched by common provisional construction and one authoritative branch, not by both post-gate authorities. Diagnostics do not choose a different routing branch; they observe the already prepared final route set.

## 5. Compatibility/support gate inventory

| Gate | Condition and scope | Rejected families / reason | After rejection and visibility consequence | Status/tests |
|---|---|---|---|---|
| `GeneratedLogicalRoutes.EnsureCompatible` | placement identity/revision, route-set boundary | stale route data | throws rather than mixing revisions | permanent invariant; observer/planner revision tests |
| `GroupedVerticalBandPlanner.Supports` unsupported shape | `report.UnsupportedShapeCount == 0`, whole graph | any non-orthogonal/unsupported observed route | complete legacy fallback; known violations may remain | temporary migration gate; planner unsupported tests |
| exposure gate | no node ID begins `tree_`, whole graph | exposure trees | complete legacy fallback | temporary; existing exposure/global routing tests retain legacy behaviour |
| missing-extent gate | at least one band is deficient, whole graph | supported-but-already-spacious graphs | legacy path, because grouped path has no work | implementation shortcut; eligible fixture contrasts it |
| adjacent-layer gate | every target depth equals source depth + 1 | skipped, upward and return routes | whole graph legacy | temporary; skipped-layer planner test |
| single-membership gate | exactly one band membership per route | multiple-band/cross-branch shapes | whole graph legacy | temporary; observer/planner tests |
| route lookup | every route has source/target placed nodes | malformed/incomplete graph | unsupported or invariant failure | intended invariant |
| duplicate repair gate | duplicated exposure mode and blocking findings | decides `Repair` versus `CompileOnly`, graph-level legacy repair | advisories may remain after CompileOnly | product-mode policy; `RequiresDuplicateRepair` tests |
| repair budget | size-dependent work limits | costly remaining legacy defects | returns best result with `WorkBudgetExhausted`/reason; findings may remain | intended bounded legacy behaviour; repair tests |
| regional/global selector eligibility | candidate and interaction limits | no alternative, excessive or unsupported regions | retain selected route | intended bounded legacy optimisation; selector tests |

Canonical cross-branch, cross-project, interacting groups, project ownership and terminal shapes have no explicit Stage C predicates. They are rejected only indirectly when their depths, memberships, shapes or exposure IDs fail. Therefore a canonical or cross-project route satisfying the narrow syntactic predicate could enter grouped routing despite not being named as supported. Terminal prefix/suffix preservation is constructed by the grouped generator but is not a support gate. One unsupported route does force the entire graph to legacy routing.

## 6. Visibility-rule matrix

| # | Rule | Current classification | Concrete basis |
|---:|---|---|---|
| 1 | semantic dependency remains present/traceable | **guaranteed** at model/serialization boundaries | stable logical link dictionaries, ownership reconstruction and exporter dependency-count tests |
| 2 | no route through node | **known violation retained by fallback** | validator reports `NodeCollision`; repair can exhaust/retain; cCoder has 10 |
| 3 | separate ambiguous collinear sharing | **known violation retained by fallback** | `SharedSegment` validation/repair tests; cCoder has 45 |
| 4 | separate shared bends/endpoints | **known violation retained by fallback** | `ReusedBend` diagnostic; cCoder has 3; production cleanup can introduce diagonals |
| 5 | clean perpendicular crossovers may remain | **guaranteed as an allowed classification**, not a correction guarantee | `PerpendicularCrossing` and `RoutePointContactKind.CleanCrossover` tests |
| 6 | crossover strictly interior and never a bend | **guaranteed only by grouped contact classifier; best effort overall** | `GroupedSpacingTests` distinguishes clean crossover from ambiguous bend; legacy geometry is not universally constrained by it |
| 7 | required vertical and horizontal space is created | **known violation retained by fallback** | vertical constraints work only in grouped adjacent-layer subset; cCoder has 20 deficits; horizontal constraint types are unwired |
| 8 | width/height may grow without arbitrary limits | **best effort** | legacy has bounded passes/work budgets; grouped has a 16-iteration convergence guard rather than a visual-size cap |
| 9 | authoritative routes remain orthogonal | **known violation retained by fallback** | Stage B records 20 unsupported band shapes; production `SeparateOverlappingCorners` can create them |
| 10 | no immediate reversal | **best effort** | normalizer and validator detect it; regression tests cover selected cases, but fallback may retain findings |
| 11 | no route shape exempt from visibility rules | **not represented as a universal enforcement rule** | compatibility fallback exempts unsupported graphs from grouped enforcement and legacy may emit diagnosed defects |

A validator finding is evidence of detection only. It is not counted as satisfaction.

## 7. Real authority model

| Phase | Actual role |
|---|---|
| Stage A `PlacementPipeline.Place` | authoritative initial placement, later provisionally revisable by routing expansion |
| Stage B `InterLayerBandObserver` | observational; its first report is Stage C input, later reports follow revisions |
| `MonotonicSpacingConstraintStore` | deterministic model inside one grouped plan call; not persistent across convergence iterations |
| grouped lane assignment | authoritative for a supported complete graph |
| grouped regeneration | mutating/regenerating; reconstructs invalidated routes from semantic endpoints |
| legacy corridor/lane/traversal | observational then mutating/regenerating; authoritative only on the legacy branch |
| `RouteRepairCoordinator` | mutating and validating with bounded work; owns final legacy logical points |
| production `SeparateOverlappingCorners` | active mutating heuristic during provisional route construction |
| traversal fallback | mutating fallback that can preserve/restore provisional geometry even with known findings |
| normalization | mutating canonicalization followed by validation |
| ownership segmentation | serializing compiler; splits and rebases while preserving exact absolute logical geometry |
| XML emission | serializing only |

Final node coordinates belong to the final `PlacedGraph` returned by `RenderLayout` after any routing-driven revision. Final logical route points belong to `RenderLayout.Links`: grouped normalized routes on the supported branch, or repair output on legacy. A provisional route exists before all spacing is known; later layer/capacity movement invalidates it and triggers regeneration, while bounded fallback can retain known violations. Ownership and serialization alter parent-relative representation, not reconstructed absolute geometry.

There is one logical `LinkLayout` representation at each typed revision boundary, but not one immutable representation for the whole pipeline: generated, normalized and validated wrappers record evolving authority; ownership later maps one logical edge to multiple physical cells. Empty legacy corridor/lane/traversal models returned by grouped routing are compatibility scaffolding, not a second authority.

## 8. Stage C on real and fixture graphs

Deduplicated cCoder:

```text
graphs classified: 1
bands inspected: 6
groups discovered: 0 (rejected before Plan)
groups supported: 0
groups unsupported: not constructed/not counted
routes eligible: 0
routes regenerated by grouped path: 0
constraints proposed/applied: 0 / 0
nodes shifted/layers shifted: 0 / 0
legacy routes generated: 294
new routes generated: 0
fallback reason: not recorded; UnsupportedShapeCount=20 and tree_ nodes are independently sufficient
```

Stage C has no final production effect on cCoder. The smallest activating fixture is `Eligible_under_sized_adjacent_graph_uses_grouped_pipeline_as_one_authority`: one graph, one deficient band, one transitive group, two routes eligible and regenerated, one vertical minimum proposal/increase, the lower layer shifted once, zero legacy corridor routes, and result reason `GroupedVerticalBandConverged`. Exact low-level telemetry is asserted through the planner and generation-level tests; no normal-runtime switch exists to force that identical graph through legacy, so claiming a same-graph visual A/B would be false. The deterministic evidence catalogue records the available pair and changed region.

## 9. Fallback-scope analysis

| Candidate boundary | Safety analysis |
|---|---|
| whole graph | Safe for single authority and current implementation, but unnecessarily coarse. Any unsupported route discards all grouped opportunities. |
| project | Unsafe alone: cross-project routes, root-owned corridor portions, canonical shared nodes and project-bound changes couple projects. |
| disconnected layout tree | Potentially safe only after proving no shared canonical node, cross-tree route, common obstacle/band or project-bound movement. Exposure trees deliberately violate simple tree isolation. |
| band | Unsafe alone: routes cross multiple bands; moving a lower layer affects every layer below and routes outside the band. |
| conflict group | Best candidate, but only after closing transitively over shared routes, obstacles, bands, semantic endpoints and every overlapping node-movement scope. Groups whose movement scopes overlap must merge. |
| route | Unsafe: routes share terminals, lanes, bends, obstacles and placement movement. |

The smallest safe temporary migration boundary is therefore a connected interaction component closed over route sharing, obstacles, semantic terminals, all crossed bands and placement invalidation scopes. Until that dependency closure exists and can be validated atomically, whole-graph fallback is the only demonstrated safe authority boundary.

## 10. Grouped-model wiring matrix

| Requested capability | Exists/tested | Production/activation/final effect |
|---|---|---|
| monotonic minimum X | type/scope enum only | not proposed or applied |
| monotonic minimum Y | yes; constraint-store tests | wired for adjacent vertical bands only; active fixture yes, cCoder no |
| origin-to-extent ordering | yes; store sorting tests | used within each plan call only; no persistent store across iterations |
| vertical layer spacing | yes | production grouped path; active fixture changes geometry |
| horizontal sibling/subtree spacing | model vocabulary only | no producer/consumer |
| project-container reflow | generic `PositionProjects` after Y move | indirect bounds reflow only; no grouped project constraint |
| transitive conflict grouping | yes; fixture | wired after support gate; not reached on cCoder |
| inclusive endpoint classification | yes; fixture | grouping helper/model; narrow grouped path only |
| shared-bend classification | classifier/test | not used as a production grouped correction |
| clean-crossover classification | classifier/test | fixture-level classification; not a universal production gate |
| complete-group lane assignment | yes | grouped path only; active fixture yes |
| grouped constraint merge | yes inside one `Plan` | deterministic max merge; recreated next iteration |
| lower-layer shifting | yes | grouped path; moves lower and all deeper nodes |
| route invalidation closure | endpoint/crossed-band closure | grouped path; not a general obstacle/movement interaction closure |
| semantic endpoint regeneration | yes | grouped supported routes only |
| atomic group authority | partial | routes regenerate in one `Apply`, but validation is whole-graph afterward and there is no group rollback |
| convergence iteration | yes, maximum 16 | production grouped path |
| deterministic parallel analysis | immutable models only | no parallel execution |
| deterministic merge | yes, tested ordering | single-threaded plan merge |
| group-level validation | absent | only complete final route-set validation |

`SpacingConstraintScope` advertises horizontal and project scopes that have no active production implementation. Telemetry's collapsed-pair estimate is diagnostic rather than a direct count of every pair. Empty legacy models on the grouped return path exist solely to satisfy `LegacyRoutingResult` consumers.

## 11. `SeparateOverlappingCorners`

Production calls the private `RenderLayout.SeparateOverlappingCorners` from `BuildRoute` before candidate selection completes. It offsets a repeated bend independently. It has no dedicated production test class, though broader exporter/routing tests exercise the surrounding path. It is active and can still create the known 20 non-orthogonal routes.

The experimental branch replaces this with `OverlappingCornerSeparator`, which proposes coherent run shifts/doglegs, adds geometry/report helpers and has `OverlappingCornerSeparatorTests`; commit `79f6c88` contains 573 inserted and 38 removed lines across five files. None is active in production.

Upstream overlapping corners arise when separately constructed routes choose the same source/target fan-out bend, junction transition, or corridor turn. The eventual generator owners should prevent these respectively through terminal fan-out allocation, junction-owned transitions and complete group/corridor lane assignment. Cleanup should not be the authority. Stage C's `UnsupportedShapeCount == 0` gate directly relies on diagonals as a rejection signal, so the current graph cannot reach the component that might otherwise create distinct grouped bends.

## 12. Stage B semantics and revisions

The eager Stage B observation sees accepted Stage A placement plus initial provisional `PositionLinks` routes. It is an input to Stage C compatibility and planning. On grouped activation it is recalculated after every placement/route revision until missing extent is zero. On legacy fallback it is discarded after classification; the legacy corridor pipeline proceeds independently. Diagnostic export later observes final selected routes and placement without rerouting. Thus no individual report contains a mixture of legacy and grouped routes.

`PlacedGraph.Revise`, `RouteRevision.Next`, `GeneratedLogicalRoutes.EnsureCompatible`, and expected-revision checks prevent a stale report or route set from being applied to a new placement. The first Stage B report becomes stale after grouped movement, but the loop immediately replaces it. The final diagnostic report is recalculated from final geometry.

## 13. Duplicated and obsolete concepts

| Concept(s) | Classification |
|---|---|
| `CorridorObservation` / `CorridorLaneAllocation` versus `InterLayerBandReport` / grouped lanes | both authoritative on exclusive branches; temporary migration duplication |
| legacy capacity expansion versus `MinimumSpacingConstraint` | exclusive old/new spacing models; migration scaffold |
| global/regional interaction regions versus `BandConflictGroup` | overlapping grouping concepts with different scopes; both authoritative only in their branch |
| `PositionedLinkLayouts`, generated/normalized/validated wrappers | still authoritative revision boundaries |
| empty corridor/lane/traversal models returned by grouped path | temporary compatibility scaffold |
| Stage B lane indices versus grouped assigned lanes | diagnostic observation followed by production recomputation; duplicated calculation |
| horizontal/project `SpacingConstraintScope` members | model exists but apparently unused |
| contact classifier's bend/crossover cases | fixture/model evidence; not consumed by grouped production correction |
| eager Stage B observation on rejected graphs | production compatibility input whose detailed result is then discarded |
| diagnostic final Stage B observation | diagnostic only and intentionally duplicated to describe final geometry |
| `LegacyRoutingPipeline` name and `legacy route generation` phase around both authorities | misleading migration naming, not obsolete executable code |

## 14. Contradictions and decisions

Unresolved contradictions:

1. Universal visibility rules conflict with fallback that knowingly emits node collisions, shared segments, spacing deficits and diagonals.
2. “Complete-group atomic authority” is not implemented as group validation/rollback; validation remains whole-graph after mutation.
3. “Monotonic constraints” are monotonic within a plan call, not a persistent constraint system across convergence iterations.
4. Stage C types imply X, project and broad contact support that production does not implement.
5. Compatibility documentation names rejected route families, but code uses indirect shape/depth/membership tests and can admit unlisted canonical/cross-project cases.
6. The new authority lives inside `LegacyRoutingPipeline` and its time is labelled `legacy route generation`, obscuring invocation accounting.
7. The current Stage C document's hash was wrong despite claiming exact parity.
8. Required same-graph new/legacy visual comparison is not selectable in the current runtime; creating one would require a development gate or building a prior revision.

Questions requiring product decision:

- Are visibility violations allowed in ordinary output during migration, or must generation fail/mark the diagram when universal rules are not achieved?
- Should support be explicit by semantic route family/ownership, rather than inferred from geometry and depth?
- Is the migration unit the proposed closed interaction component, and what movement/obstacle closure constitutes atomicity?
- Must constraint state persist across iterations and include both X and Y before grouped authority expands?
- Should Stage B remain a production classifier input or return to diagnostics once an explicit capability model exists?
- Is a temporary developer-only authority selector worth adding for exact A/B evidence, or should revision-based comparisons remain sufficient?

Recommended decisions, without an implementation plan:

- Declare one enforceable output contract for the eleven rules and distinguish hard invariants from advisory quality metrics.
- Define support through explicit semantic and interaction capabilities, with a recorded rejection reason, rather than whole-graph incidental predicates.
- Retain whole-graph fallback until a closed interacting-region model proves independent authority and atomic validation.
- Treat the current grouped slice as an experimental production path, not a general replacement, until X/Y constraints and invalidation closure match the advertised model.
- Require phase names/counters to identify provisional construction, classification, grouped authority and legacy authority separately.
- Keep the experimental corner work isolated; remove the need for cleanup by making the future generator own distinct bends.
- Do not proceed directly to skipped-layer support until the product decisions above reconcile fallback with universal visibility.

## 15. Evidence and verification

The focused catalogue is `docs/evidence/current-routing-audit-catalogue.md`. Existing characterisation suites include `GroupedSpacingTests`, `GroupedVerticalBandPlannerTests`, `InterLayerBandObserverTests`, `TraceabilityValidatorTests`, `LogicalRouteNormalizerTests`, `RouteRepairCoordinatorTests`, `EdgeTraversalCompilerTests`, `LayeredLayoutRegressionTests`, and `DeterministicDrawioExporterTests`.

No router, layout, support gate, fallback, performance path, or VSIX file was changed by this audit.
