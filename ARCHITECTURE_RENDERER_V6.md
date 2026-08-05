# Architecture Renderer V6

V6 establishes the renderer-independent boundary for Architecture diagrams:

```text
Roslyn semantic analysis
    -> ArchitecturePlanningRequest
    -> IArchitectureDiagramPlanner
    -> PlannedArchitectureDiagram
    -> IArchitectureDiagramRenderer<DrawioPage>
    -> Draw.io document composition
```

`ArchitectureDiagramV6Planner` currently builds the diagram and project grid shells and emits a `V6PlanningNotImplemented` diagnostic. It intentionally does not project nodes, classify topology, place nodes, allocate terminals or lanes, size tracks, route links, or calculate coordinates.

## Structural model

- `ArchitecturePlanningRequest` owns the semantic model, selected scope, generation settings and immutable policy records.
- `ArchitectureDiagramPlanningState` is the mutable state reserved for a future planner; it is never passed to a renderer.
- `PlannedArchitectureDiagram` is the completed immutable renderer input.
- Canonical and duplicate-branch projection are represented by `NodeProjectionMode`; the existing `AllowDuplicateNodes` setting maps at the request boundary.
- Physical nodes and links retain semantic identity, project ownership and duplication provenance.
- `DiagramRoutingGrid` owns project footprints and cross-project transitions. `ProjectRoutingGrid` owns project-local reservations and label reservations.
- Planning rows, columns and sparse cells are independent. Cell capability, occupancy and reservations are separate properties.
- A physical node has one anchor cell and an odd-width logical footprint. Footprint sizing is deferred.
- Abstract routes retain physical endpoints, route steps, topology family and grid transitions. Straight runs, turns, crossings and endpoint demands retain future lane continuity without assigning pixels.
- Track constraints represent single-track, span, lane, clearance, node and project requirements. Solving is deferred.
- Relative and absolute geometry types exist for future grid compilation. Draw.io XML emission owns no planning decisions.

## Validation and diagnostics

`IPlannedArchitectureDiagramValidator` is the boundary for planning-model, physical-scene and renderer-reconstruction validation. Diagnostics and metrics are renderer-independent and can attribute future findings to semantic, physical, grid, cell, route, lane, constraint and segment identities.

The V6 renderer emits a minimal valid Draw.io page while planning is structural. It carries the planner diagnostic into the page. This is an explicit deferred state, not a fallback architecture diagram.

## Deferred logic

Node projection, topology classification, terminal allocation, inter-layer slot allocation, destination/return column allocation, subtree and project placement, track sizing, route materialisation, physical validation, Draw.io projection and reconstruction are intentionally not implemented in this tranche.

The generic `DiagramModel` Draw.io renderer remains for non-Architecture diagram workflows. Architecture generation resolves only `DrawioArchitectureV6Renderer`.
