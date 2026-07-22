# Routing architecture

This document describes the routing, bounded repair, validation, and advisory-output architecture. Future routing changes should respond to a concrete defect in a generated diagram.

## Repair and advisory pipeline

The authoritative post-selection pipeline is:

```text
selected logical route revision
-> terminal ordering and candidate selection
-> corridor observation and capacity planning
-> deterministic lane allocation
-> traversal compilation
-> logical normalization
-> validation
-> bounded regional repair, when required
-> coordinate-ownership segmentation
-> Draw.io XML
-> unresolved advisory report
```

Each logical dependency has one `LogicalRouteState`: stable identity, authoritative points, revision, producer, compilation status, diagnostics, and immutable historical snapshots. A stage accepts geometry by producing a new revision or rejects it while retaining the current authoritative revision. Historical candidates and rejected traversal attempts are diagnostic evidence only; they cannot silently restore geometry derived before the current corridor and lane state.

Validation findings are repair inputs, not automatic generation failures. The repair coordinator processes node-interior intersections, shared non-zero-length segments, severe spacing deficits, ambiguous reused bends, and minor spacing in that order. Candidate trials rebuild and validate a bounded interaction closure containing the changed route, shared-corridor routes, existing conflicts, and envelope-intersecting routes. One whole-graph compile remains the acceptance checkpoint for a promising regional change. A lower-tier improvement cannot worsen a higher tier, and an equal-score change is not accepted.

Default repair limits are 32 affected routes, four candidates per finding, two passes, and 128 estimated regional work units. Graphs over 128 and 256 routes receive progressively tighter limits. A graph over 256 routes is limited to 16 affected routes, two candidates, one pass, and 24 estimated work units. Budget exhaustion retains the current authoritative geometry and records an unresolved advisory.

Node collisions generate compact obstacle-side bypass candidates. Shared, closely spaced, and reused-bend geometry generates deterministic perpendicular offsets. Insufficient corridor capacity produces a structured capacity request containing route revisions, required and available lanes, bounds, required perpendicular extent, and the smallest deterministic expansion. A bounded downstream dependency closure may move; candidates, corridors, lanes, and traversals are then rebuilt. If capacity remains unavailable, the selected explicit geometry remains authoritative and the failure is diagnosed rather than replaced by manufactured coincident lanes.

Clean perpendicular crossings are explicitly informational. They do not fail strict validation and are preferable to disproportionate exterior detours where route identity remains clear.

The typed `DrawioGenerationResult` exposes the document, selected route points, pre-repair findings, repair attempts, post-repair findings, serialization status, and strict-validation outcome. Unresolved findings retain route identities, interacting routes or nodes, exact coordinates or intervals, and spacing measurements.

Normal rendering emits a parseable diagram and reports unresolved geometry as advisories. Analysis, semantic-graph construction, invalid settings, serialization, and output-write failures remain technical failures. The CLI exits zero after successful default generation. `--strict-validation` still writes the diagram and complete diagnostic JSON, but returns non-zero when enforced findings remain. `--emit-on-validation-failure` is retained only as a deprecated strict-mode alias.

## Pipeline

The Draw.io renderer is authoritative for connector geometry. diagrams.net displays explicit exporter-owned terminal constraints and waypoints; it is not expected to reroute generated connectors.

```text
semantic dependencies
→ node and project layout
→ accepted logical routes
→ corridor observation and capacity
→ deterministic corridor-lane allocation
→ terminal fan-out modelling
→ complete corridor traversal representation
→ bounded junction-owned transitions
→ bounded global or regional path selection
→ final whole-diagram traceability validation
→ coordinate-ownership segmentation
→ Draw.io XML
```

Logical route history is immutable, while authoritative geometry progresses through explicit `Selected`, `Allocated`, `Compiled`, `Normalized`, and `Validated` revisions. Corridor mappings record the route revision they observed, and compilation rejects stale mappings. Final validation consumes the normalized authoritative points before coordinate-ownership segmentation. Reconstructing physical Draw.io segments in absolute coordinates must reproduce those same normalized points exactly.

## Traceability priorities

Route selection uses lexicographic scoring. A lower-priority improvement cannot compensate for a higher-priority regression.

1. Invalid geometry or node collision.
2. Shared non-zero-length segments and route-identity loss.
3. Minimum-spacing violations.
4. Terminal fan-out ordering, duplicate ports, duplicate lanes, or terminal ambiguity.
5. Ambiguous junction transitions and reused bends.
6. Corridor-capacity failures.
7. Avoidable crossings and congestion.
8. Excessive canvas escape.
9. Path length, bend count, and locality.

Congestion may communicate genuine architectural convergence. A busy corridor is acceptable while spacing and route identity remain clear.

Protected invariants include configured parallel spacing, bottom-edge source exits, top-edge target entries, monotonic same-side fan-out, distinct supported bends, exposure-tree locality, and byte-deterministic output.

## Corridors, lanes, and junctions

Corridors describe observable horizontal or vertical routing space, theoretical lane capacity, usage, and junction connections. Identity includes orientation, contiguous interval, obstacle-boundary context, routing region, and role. Ordinary corridors, source transitions, and target transitions are distinct even when temporarily collinear. Lane allocation is deterministic and assigns distinct coordinates before geometry compilation.

Terminal transition observations carry terminal side, ordered route group, port, protected stub, first ordinary corridor, required transition depth and spread, and lane order. Duplicate-mode fan-out order is resolved before corridor observation; a downstream mapping cannot compile a different route revision.

Supported junction transitions are bounded orthogonal turns and departures for which the traversal and lane allocators can preserve route identity with distinct bend geometry. Straight corridor traversal remains supported.

Arbitrary multi-direction junctions, required lane permutations, or opposing traffic arrangements may be unsupported. An unsupported topology retains the accepted logical path, uses diagnosed fallback geometry, and emits `UNSUPPORTED_JUNCTION_TOPOLOGY` rather than silently dropping or inventing a route.

## Path selection

Ordinary diagrams use bounded global coordinate descent when the estimated work remains within budget. Candidate reduction preserves structurally distinct corridor-path signatures before retaining lane variants. The accepted route always remains a candidate.

Current global limits are:

```text
maximum candidates per edge: 8
maximum improvement passes: 4
maximum estimated work: 2,000,000
```

Estimated work is deterministic and is derived from alternative count, route-pair count, and pass count. There is no fixed 64-edge authority threshold. Work above the limit is routed through bounded regional optimisation.

Large and exposure-tree diagrams use deterministic interaction regions rather than one monolithic optimiser. Regions contain mutable routes and fixed context routes. Context affects scoring but cannot move during that regional decision.

Current regional limits are:

```text
maximum mutable edges per region: 24
maximum fixed context edges:      48
maximum candidates per edge:       8
maximum passes per region:          4
maximum regions per diagram:       64
interaction margin:                12 px
```

Overlapping interactions form deterministic connected regions. Oversized regions preserve accepted geometry and emit `RegionTooLarge`. Every regional proposal is rescored against the complete diagram and is reverted if it causes a higher-tier whole-diagram regression.

Exposure alternatives must retain the accepted exposure root and cloned branch. The optimiser does not merge clones or route through a sibling/root merely to balance utilisation.

## Terminal fan-out

Shared-source and shared-target groups record terminal order, lane order, remote-node order, and left/right side. Left and right subsets must remain independently monotonic.

A fan-out route is mutable only when its candidate:

- retains the accepted route as fallback;
- preserves the accepted terminal-side prefix and suffix;
- preserves distinct terminal ports and lanes;
- maintains configured terminal spacing;
- does not reverse same-side remote-node order;
- strictly improves the lexicographic whole-diagram score.

Changing only a terminal point is insufficient because a different first or last corridor can cause later lane compilation to reorder the fan-out. Downstream-only alternatives are therefore the safe mutable form.

## Coordinate ownership and Draw.io cells

Nodes, anchors, and route portions owned by a project are children of its project container and use project-relative coordinates. Root-owned geometry uses absolute root coordinates. External diamonds rendered for a project are unique physical nodes owned by that project, and project bounds include owned vertices and connector geometry with padding.

A cross-boundary logical dependency may compile to several physical edge cells joined through invisible zero-size boundary anchors:

```text
source-project segment
→ root/inter-project segment
→ target-project segment
```

Only the first physical segment carries a source marker, and only the last carries the target arrow. Segment metadata records the logical edge, semantic terminals, segment index and role, and owner project. Save/reopen must preserve parentage, metadata, waypoints, and reconstructed geometry.

Moving a project container moves its owned nodes, anchors, and route portions. Root-owned and other-project geometry remains fixed, so a transitional segment may become diagonal after manual movement. This is intentional.

## Manual-editing limitations

Multi-parent physical segments cannot behave as one native selectable Draw.io edge. Selecting or deleting one visible segment affects that segment only. Metadata permits logical reconstruction, but diagrams.net does not provide unified selection without a custom plugin.

Explicit exporter-owned waypoints also mean arbitrary manual node movement can stretch the nearest segment. Project-container movement is supported through coordinate ownership; general-purpose interactive rerouting is not.

## Diagnostics

Generated edge cells may include routing mode, path signatures, local cost, rejected alternatives, regional decision and fallback information, fan-out membership, and ownership metadata. Diagnostic JSON also records stage timings, route revisions, stale-state rejections, invalidated routes, revalidated pairs, corridor rebuilds, capacity failures and expansions, repair run/skip reason, and diagnostic-result reuse.

`GenerateResult` owns one render preparation and contains the normal document plus all information required for diagnostic JSON and focused diagrams. Requesting diagnostics from that result does not repeat semantic analysis or rendering.

Duplicated exposure mode may skip cosmetic repair only after normalized validation proves that there are no node intersections and no serious shared-route ambiguity. The report records whether repair ran or why it was skipped.

## Visual Studio execution

Visual Studio workspace acquisition, hierarchy/DTE access, settings, and dialogs remain on the UI thread. Semantic analysis and deterministic rendering run behind an explicit background boundary. A modeless progress dialog reports analysis, layout/routing, and output-writing stages and supplies cancellation to analysis and writing, with cancellation checks around synchronous rendering. Route mutation remains single-threaded and deterministic.

## Defect-driven routing workflow

Do not begin a broad routing rewrite. For each observed defect:

1. Preserve the exact problematic `.drawio` file and effective settings.
2. Record the involved source, target, logical routes, and diagnostic metadata.
3. Reduce the topology to a deterministic fixture where practical.
4. Classify the defect as a candidate-generation gap, unsupported junction, lane allocation, fan-out ordering, corridor capacity, ownership/serialization, or node-layout limitation.
5. Add a focused failing regression test before production changes.
6. Make the smallest coherent correction.
7. Rebuild the CLI before every `--no-build` generation.
8. Regenerate the original project with identical settings.
9. Verify the complete diagram has no higher-priority traceability regression and that unaffected routes remain identical.
10. Package a new VSIX only when the concrete defect is visibly improved and full build/test/determinism checks pass.

## Typed Architecture production path

The canonical production sequence is:

```text
Roslyn workspace and DI analysis
-> ArchitectureDiagram semantic model
-> ArchitectureTopologyProjector
-> ArchitectureRenderGraph
-> project-local placement and project-region composition
-> DrawioArchitectureRenderer
-> authoritative logical routes
-> coordinate-ownership compilation
-> Draw.io XML
```

The analyser owns semantic type and DI meaning. A uniquely registered interface is represented by its implementation-owned combined node (`OrderService : IOrderService`). An unresolved interface remains an interface node. Multiple distinct registrations remain one counted interface node. Class implementation alone is not treated as registration.

The renderer-neutral projector owns canonical versus duplicated occurrences, exception matching, deterministic traversal roots, render-instance IDs, first-occurrence placement parents, project ownership and semantic-to-render-instance reconciliation. It owns no coordinates or Draw.io types. Empty-root mode infers roots by collapsing semantic strongly connected components, selecting zero-incoming components, and then adding the lowest remaining semantic ID until every selected node is represented.

Duplication is independent of configured root filters. Enabled mode recreates repeated downstream occurrences. Disabled mode retains the first deterministic canonical occurrence. Exception patterns opt matching branches into duplicated behavior. Internal occurrences always remain owned by the source-code project defining the type. A canonical external node referenced by several projects is root-owned; duplicated external occurrences are project-local.

Multi-project placement first lays out each project's owned graph in local coordinates. It then builds the project dependency graph, collapses project cycles for deterministic component placement, composes distinct non-overlapping global regions, and places shared root-owned external nodes outside those regions. The old global layer-band alignment is not allowed to interleave nodes again after region composition.

Production validation reports node/project overlap, owner containment, route/node contact, unrelated-project traversal, project-label contact, shared segments, spacing, reversal and ownership reconstruction findings. Geometry findings do not suppress the diagram. CLI strict validation writes the diagram and reports a failing exit status afterward.

Use these determinism terms precisely:

- Serialization repeat repeats document serialization from one generated page.
- Render repeat repeats projection, placement, routing, ownership and serialization from an existing semantic model.
- Full-pipeline generation repeat independently repeats workspace creation/load, Roslyn analysis, DI resolution, topology projection, layout, routing, ownership and serialization.

`DrawioArchitectureRenderer` is the only canonical typed Architecture renderer. `DrawioDiagramRenderer`, `IDeterministicDrawioExporter` and semantic-model exporter entry points are compatibility-only. Their tests do not constitute typed-production evidence.

Optional future work—such as arbitrary junctions, lane permutations, opposing traffic, layout expansion for physical capacity, or diagnostic presentation—requires concrete diagram evidence rather than a predefined architectural stage.
