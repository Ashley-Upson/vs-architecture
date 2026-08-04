# Replacement Architecture Renderer Report

Date: 2026-08-04  
Branch: `codex/replace-architecture-renderer`  
Input reviewed: `C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement.sln`  
Output reviewed: `replacement-content-management.drawio`

## Executive Summary

The replacement renderer is now wired into the active Architecture Draw.io path. It is no longer an experimental side path. It has a staged internal pipeline, deterministic output, provenance artifacts, route validation, and a CLI-generated real-project output.

The implementation is not yet a finished replacement from a visual-quality perspective. The generated file is structurally valid after the root-cell fix, but the Content Management scene is still extremely large and contains evidence of route sharing. The most important remaining problem is not XML validity; it is that analysis projection and layout policy are creating too many physical node instances and very large coordinate spans before routing begins.

## Work Completed

### Renderer replacement

The active Architecture path now runs through:

1. Planning graph creation.
2. Candidate layout and route construction.
3. Accepted physical scene creation.
4. Draw.io page-model creation.
5. XML serialization.
6. XML geometry reconstruction and validation.

The main implementation is in:

- `src/StandardIo.ArchitectureDiagram.Core/Services/Processings/Drawios/ReplacementArchitectureRenderer.cs`
- `src/StandardIo.ArchitectureDiagram.Core/Models/Drawios/ReplacementRenderModels.cs`

The generic Draw.io renderer and the typed Architecture renderer both use the replacement implementation.

### Model and routing behavior added

- Explicit planning nodes and links.
- Finite handling of cycles.
- Deterministic ordering based on discovered order.
- Dependency depth calculation.
- Standalone-node grid placement.
- Project bounds and project-relative child geometry.
- Explicit source and target port ratios.
- Bottom-to-top dependency direction.
- Inter-layer lane reservation.
- Semantic source/target provenance on rendered cells.
- Reconstruction checks for serialized edge geometry.
- Logical, physical, and geometry findings in development artifacts.

### Draw.io XML correction

The first generated replacement file opened blank because the serializer emitted the required Draw.io structural cells as vertices. This was corrected in commit `58ede8b`:

```xml
<mxCell id="0" />
<mxCell id="1" parent="0" />
```

The corrected output parses successfully and contains 2,541 cells.

### Tests and verification

- Core test suite: **470 passed, 0 failed**.
- Full solution build: **0 errors**.
- CLI build: **0 errors**.
- Real Content Management generation completed successfully.
- Replacement output is deterministic for the same graph and settings.
- Representative replacement tests cover cycles, standalones, ports, provenance, and deterministic output.

Relevant commits:

- `07bcba5` replace architecture renderer core
- `e20edf1` harden replacement scene provenance
- `2618972` reserve replacement inter-layer routing lanes
- `c528308` remove legacy architecture exporter entry
- `58ede8b` fix replacement Draw.io root cells

## Current Real-Project Measurements

From `replacement-content-management-analysis/architecture-evidence.json`:

| Measurement | Result |
|---|---:|
| Projects | 1 |
| Semantic nodes | 239 |
| Semantic links | 340 |
| Projected render nodes | 1,309 |
| Projected render links | 1,229 |
| Rendered routes | 1,229 |
| Duplicated instances | 1,070 |
| Canonical shared nodes | 0 |
| Multi-parent nodes | 48 |
| Unresolved interface resolutions | 1 |
| Logical findings | 2 |
| Physical findings | 0 |
| Geometry findings | 2 |

The generated project bounds are approximately **124,672px wide by 16,382px high**. That is the most visible sign that the current projection/layout combination is still not behaving as intended for a real, dense project.

## Work Not Yet Done

### 1. Visual acceptance has not been completed

The replacement has been tested through XML and geometry data, but it has not yet been accepted through a systematic Draw.io visual review at useful zoom levels. Structural validity is not the same thing as a readable diagram.

The next verification should inspect:

- the first viewport after opening;
- the overall fit-to-content view;
- dense orchestration/processing areas;
- external nodes directly beneath their constructing node;
- shared dependencies with several incoming links;
- standalone-node placement;
- baseline-aligned service groups.

### 2. Legacy lower-level utility code remains

The old public/exporter entry components were removed, but some lower-level legacy Draw.io utility classes and tests remain in the repository. They are no longer on the active Architecture entry path, but they have not all been deleted or fully migrated.

This leaves avoidable maintenance ambiguity and should be cleaned up after the replacement behavior has been visually accepted.

### 3. Candidate replanning is not yet a true search

The new renderer has a candidate/scene boundary, but it currently constructs one deterministic candidate and validates it. It does not yet perform a meaningful set of alternative placements, score them, and choose the best valid candidate.

The established goal was to shift nodes when routing becomes difficult. That requires layout and routing to cooperate through candidate scoring, rather than routing around a mostly fixed layout.

### 4. Absolute physical geometry reconstruction is incomplete

Edge waypoint preservation is checked after serialization. Full reconstruction of nested project-relative node geometry and comparison against the accepted absolute scene is not yet complete.

That means XML can preserve edge points while a parent-relative geometry mistake could still go undetected.

### 5. The existing geometry analyser is still partly legacy-oriented

The generated evidence includes findings from the existing geometry analysis path. Some of those checks describe the output, but they are not yet fully unified with the replacement renderer's own physical-scene model.

The validation model should eventually have one authoritative interpretation of node bounds, project-relative coordinates, edge ownership, and route segments.

## Rule-by-Rule Comparison

### Selected projects first

**Status: Partially satisfied.**

The model orders selected projects first and the real run contains one selected project. Multi-project ordering has not been exercised in this report.

### Constructor dependencies are the mapped dependencies

**Status: Partially satisfied.**

The replacement consumes the existing semantic analysis output, and interface resolution metrics show 154 unique resolutions with one unresolved case. However, the real output still needs targeted inspection to prove every interface dependency is represented by its registered implementation where registration information exists, without incorrectly adding implementation substitutions that were not requested by the constructor.

### Unique node identity and reusable nodes

**Status: Not satisfied for the reviewed default output.**

The semantic graph has 239 nodes, but the renderer projects 1,309 physical render nodes and reports 1,070 duplicated instances. Some duplication can be valid for high-noise types, but the reviewed settings have `allowDuplicateNodes: true` and no duplication exception list. That makes duplication broadly enabled rather than narrowly configuration-driven.

This is a major contributor to the huge diagram and should be changed so canonical reuse is the default, with duplication limited to explicit configured patterns.

### Cycles are finite and do not loop

**Status: Satisfied at the renderer level.**

Replacement tests include a cycle and complete successfully with finite routes. The model reuses node identities rather than recursively creating an infinite graph.

### External dependencies terminate analysis

**Status: Partially satisfied.**

External nodes are emitted with the `[External]` tag and are present in the output. The renderer treats them as ordinary physical nodes. Boundary behavior needs further analysis tests on a real project to prove that external nodes are never expanded by subsequent analysis.

### External nodes sit directly below their constructing node

**Status: Not reliably satisfied.**

The renderer has explicit external node support, but the real output's extreme vertical span and duplicated tree projection indicate that external placement is still being influenced by the larger projected traversal structure. This needs a targeted acceptance test asserting parent-relative external placement.

### Top-level nodes and baseline alignment

**Status: Not satisfied for the real output.**

The settings include baseline-related patterns, but the real evidence contains many inferred roots under `FullSelectedInput`. The current output therefore behaves more like a forest of inferred roots than one clearly established dependency exit layer.

The baseline rule must be applied as a final hard constraint to every matching node after all placement and overlap passes. It should not be allowed to drift when horizontal spacing or route lanes are adjusted.

### Same-depth layout

**Status: Partially satisfied by design.**

The replacement computes dependency depth and uses depth bands, but it does not force every depth to share one global horizontal level. That matches the later established rule. Only the configured baseline should be rigidly aligned.

### Parent centering

**Status: Partially satisfied.**

The candidate builder attempts to center parents over child spans. However, the current projected graph is so duplicated and wide that parent centering cannot reliably produce the intended local visual relationship. It needs to operate on canonical topology first, with duplication applied only where explicitly requested.

### Standalone nodes in a compact grid away from link traffic

**Status: Partially satisfied.**

The renderer includes square-root grid placement for standalone nodes. A real-project acceptance check has not yet proven that the standalone region is outside the routed tree and does not expand the diagram unnecessarily.

### Links exit from the bottom and enter at the top

**Status: Mostly satisfied in emitted geometry.**

The replacement emits explicit `exitY="1"` and `entryY="0"` values and validates endpoint orientation. This is one of the stronger parts of the current implementation.

### Port spacing and parallel lane spacing

**Status: Partially satisfied.**

Port ratios and inter-layer lane reservations exist. Two shared-segment findings remain in the real output, proving that the lane allocator does not yet guarantee separation in every topology.

### Links must not cross nodes

**Status: Not proven for the real output.**

The replacement scene validator checks link/node intersections, but there is no completed visual acceptance report proving that nested project-relative Draw.io geometry renders identically to the validated absolute scene. This remains a high-risk area.

### Perpendicular crossings are allowed

**Status: Implemented in validation policy.**

Perpendicular route crossings are not treated as invalid. Shared collinear segments remain invalid.

### Corner separation

**Status: Incomplete.**

The renderer creates deterministic orthogonal routes, but there is not yet a complete validator for two 90-degree corners occupying the same location or an acceptance test covering the established midpoint rule for competing left/right routes.

### Project borders and header text

**Status: Partially satisfied.**

Project containers are emitted as swimlane-like borders and nodes/edges can be root-owned or project-owned. The replacement has not yet fully validated that routed lines may cross the project border while never crossing the project header text in all parent-relative cases.

### Style precedence

**Status: Existing behavior retained, replacement output uses it.**

The evidence shows specific styles such as orchestration, processing, broker, and external styles being emitted. The first-match-wins rule should still be covered by focused tests, especially to ensure `*CoordinationService` and `*AggregationService` are not swallowed by a broader `*Service` rule.

## Main Root Cause Assessment

The current file's largest problem is not one bad route calculation. It is the combination of:

1. Broad render-instance duplication enabled in the reviewed settings.
2. A full-project input producing many inferred roots.
3. Layout operating on the expanded render tree instead of primarily on canonical semantic topology.
4. Routing lanes being allocated after the graph has already expanded to a very large physical scene.
5. Validation not yet comparing every emitted nested coordinate against the accepted absolute scene.

This explains why the output can be structurally valid and deterministic while still looking unlike the established rules and consuming enormous horizontal/vertical space.

## Recommended Next Work

1. Make canonical node reuse the default; enable duplication only for explicit configured patterns.
2. Add a canonical-topology placement phase before physical duplication.
3. Define the selected-project exit layer explicitly and use it to establish top-level/baseline nodes.
4. Add candidate scoring and re-planning for node shifts before accepting routes.
5. Complete absolute-to-parent-relative geometry reconstruction validation.
6. Add focused real-graph assertions for external placement, baseline lock, line/node intersections, and shared route segments.
7. Visually inspect regenerated Draw.io files before deleting the remaining unreachable legacy utility code.

