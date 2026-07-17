# Routing architecture

This document describes the routing, bounded repair, validation, and advisory-output architecture. Future routing changes should respond to a concrete defect in a generated diagram.

## Repair and advisory pipeline

The post-selection pipeline is:

```text
selected logical routes
-> validate
-> attempt bounded local repair
-> expand affected physical layer capacity when required
-> rebuild corridor observations, lanes, and traversals
-> revalidate
-> serialize the best geometry
-> report unresolved advisories
```

Validation findings are repair inputs, not automatic generation failures. The repair coordinator processes node-interior intersections, shared non-zero-length segments, severe spacing deficits, ambiguous reused bends, and minor spacing in that order. Each proposed route is passed through corridor observation, lane allocation, traversal compilation, and whole-diagram validation. A lower-tier improvement cannot worsen a higher tier, and an equal-score change is not accepted.

Default limits are 32 affected routes, four candidates per finding, two passes, and 128 whole-layout trials. Graphs over 128 and 256 routes receive progressively tighter limits. A graph over 256 routes is limited to 16 affected routes, two candidates, one pass, and 24 whole-layout trials. Budget exhaustion retains the best geometry and records an unresolved advisory.

Node collisions generate compact obstacle-side bypass candidates. Shared, closely spaced, and reused-bend geometry generates deterministic perpendicular offsets. When observed lane demand requires physical room, the affected depth and downstream layers move by a bounded deterministic amount; projects and routes are rebuilt against the moved node geometry. Capacity expansion is never metadata-only.

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

Logical route geometry is immutable between compilation stages. Final validation runs before coordinate-ownership segmentation. Reconstructing the physical Draw.io segments in absolute coordinates must reproduce the selected logical route exactly.

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

Corridors describe observable horizontal or vertical routing space, theoretical lane capacity, usage, and junction connections. Lane allocation is deterministic and assigns distinct coordinates before geometry compilation.

Supported junction transitions are bounded orthogonal turns and departures for which the traversal and lane allocators can preserve route identity with distinct bend geometry. Straight corridor traversal remains supported.

Arbitrary multi-direction junctions, required lane permutations, or opposing traffic arrangements may be unsupported. An unsupported topology retains the accepted logical path, uses diagnosed fallback geometry, and emits `UNSUPPORTED_JUNCTION_TOPOLOGY` rather than silently dropping or inventing a route.

## Path selection

Small ordinary diagrams use bounded global coordinate descent. Candidate reduction preserves structurally distinct corridor-path signatures before retaining lane variants. The accepted route always remains a candidate.

Current global limits are:

```text
maximum ordinary-graph edges: 64
maximum candidates per edge: 8
maximum improvement passes: 4
```

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

Generated edge cells may include routing mode, path signatures, local cost, rejected alternatives, regional decision and fallback information, fan-out membership, and ownership metadata. Diagnostics explain why accepted geometry was retained, including no issue, no viable alternative, size limit, exposure-locality violation, unsupported junction, or whole-diagram regression.

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

Optional future work—such as arbitrary junctions, lane permutations, opposing traffic, layout expansion for physical capacity, or diagnostic presentation—requires concrete diagram evidence rather than a predefined architectural stage.
