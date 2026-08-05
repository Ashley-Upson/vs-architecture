# Architecture Renderer V6

V6 establishes the renderer-independent boundary for Architecture diagrams:

```text
Roslyn semantic analysis
    -> ArchitecturePlanningRequest
    -> IArchitectureDiagramPlanner
    -> PlannedArchitectureDiagram
    -> V6 Draw.io vertex projection
    -> IArchitectureDiagramRenderer<DrawioPage>
    -> Draw.io document composition
```

`ArchitectureDiagramV6Planner` now projects the semantic graph into canonical or configured duplicate physical instances, assigns deterministic logical layers and positional ownership, places every physical node into a sparse project grid, sizes logical tracks, and compiles immutable relative and absolute geometry. It intentionally does not classify final topology, allocate terminals or lanes, route links, or emit Architecture nodes and links.

## Structural model

- `ArchitecturePlanningRequest` owns the semantic model, selected scope, generation settings and immutable policy records.
- `ArchitectureDiagramPlanningState` is the mutable state reserved for a future planner; it is never passed to a renderer.
- `PlannedArchitectureDiagram` is the completed immutable renderer input.
- Canonical and duplicate-branch projection are represented by `NodeProjectionMode`; the existing `AllowDuplicateNodes` setting maps at the request boundary.
- Physical nodes and links retain semantic identity, project ownership and duplication provenance.
- The partial plan exposes projection mappings, roots, external and standalone identities, cycle diagnostics, positional metadata, raw link direction and explicit stage status.
- `DiagramRoutingGrid` owns project footprints and cross-project transitions. `ProjectRoutingGrid` owns project-local reservations and label reservations.
- Planning rows, columns and sparse cells are independent. Cell capability, occupancy and reservations are separate properties.
- A physical node has one anchor cell and an odd-width logical footprint. Initial spans are logical requirements derived from node text and link degree; final physical sizing is deferred.
- Abstract routes retain physical endpoints, route steps, topology family and grid transitions. Straight runs, turns, crossings and endpoint demands retain future lane continuity without assigning pixels.
- Track constraints represent single-track, span, lane, clearance, node and project requirements. Solving is deferred.
- `ArchitectureV6GeometryBuilder` converts logical rows and columns into deterministic track extents and offsets. Node rectangles are sized from the node label, minimum node policy and grid sizing inputs; project regions include padding and reserved header space.
- Logical columns inside a node footprint are contiguous. Empty logical separator columns carry the normal horizontal spacing policy; spacing is not added after every footprint column. A final same-row reconciliation preserves the explicit normal gap between adjacent visible node rectangles while retaining larger project-section gaps.
- `PlannedPhysicalNodeGeometry`, `PlannedProjectGeometry`, `PlannedGridGeometry` and `PlannedSubtreeGeometry` preserve physical projection identity, positional ownership and project ownership through local and absolute bounds. The project grids are laid out in selected-project order followed by remaining projects in discovery order.
- Geometry validation checks positive dimensions, node collision, project containment, page bounds and one geometry record per physical node. `DrawioArchitectureV6Renderer` projects each planned physical node exactly once into a deterministic vertex cell, using absolute geometry for page-level nodes and project-relative geometry for nodes under project containers.
- Vertex metadata retains physical and semantic identity, full name, projection mode, project ownership, positional ownership and duplicate provenance. Labels are XML-escaped by `XElement` serialization. Project geometry is emitted as a swimlane-style boundary cell when containers are enabled.
- Renderer diagnostics report style-rule usage, fallback count, unmatched selectors, unused exact overrides, final node gaps and project-section gaps. Style resolution is first-match for configured rules, after exact full-name overrides and before the deterministic fallback.

## Validation and diagnostics

`IPlannedArchitectureDiagramValidator` is the boundary for planning-model, physical-scene and renderer-reconstruction validation. The active validator checks node placement, footprint ownership, physical link endpoints, canonical cardinality, duplicate provenance, geometry dimensions, containment, bounds and collisions. Diagnostics and metrics are renderer-independent and can attribute future findings to semantic, physical, grid, cell, route, lane, constraint and segment identities.

The V6 renderer emits a valid Draw.io page containing planned project boundaries and physical node vertices. Route planning and edge emission remain deferred. Renderer diagnostics report emitted/skipped vertex counts, style fallbacks and output bounds.

## Deferred logic

Topology classification, terminal allocation, inter-layer slot allocation, destination/return column allocation, route materialisation, route-aware physical validation, Draw.io edge projection and reconstruction are intentionally not implemented. Logical node projection, layer assignment, positional ownership, anchor placement, nested subtree reservations, track sizing, relative-to-absolute geometry compilation and physical vertex projection are active.

The generic `DiagramModel` Draw.io renderer remains for non-Architecture diagram workflows. Architecture generation resolves only `DrawioArchitectureV6Renderer`.
