# Architecture Renderer V6

V6 establishes the renderer-independent boundary for Architecture diagrams:

```text
Roslyn semantic analysis
    -> ArchitecturePlanningRequest
    -> IArchitectureDiagramPlanner
    -> PlannedArchitectureDiagram
    -> V6 orthogonal route planning
    -> V6 Draw.io vertex and edge projection
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
- Abstract routes retain physical endpoints, route steps, topology family and grid transitions. `ArchitectureV6RouteBuilder` now compiles deterministic orthogonal physical segments from the completed node geometry, choosing local lane candidates and rejecting candidates that pass through unrelated node rectangles.
- Track constraints represent single-track, span, lane, clearance, node and project requirements. Solving is deferred.
- `ArchitectureV6GeometryBuilder` converts logical rows and columns into deterministic track extents and offsets. Node rectangles are sized from the node label, minimum node policy and grid sizing inputs; project regions include padding and reserved header space.
- Logical columns inside a node footprint are contiguous. Empty logical separator columns carry the normal horizontal spacing policy; spacing is not added after every footprint column. A final same-row reconciliation preserves the explicit normal gap between adjacent visible node rectangles while retaining larger project-section gaps.
- `PlannedPhysicalNodeGeometry`, `PlannedProjectGeometry`, `PlannedGridGeometry` and `PlannedSubtreeGeometry` preserve physical projection identity, positional ownership and project ownership through local and absolute bounds. The project grids are laid out in selected-project order followed by remaining projects in discovery order.
- Geometry validation checks positive dimensions, node collision, project containment, page bounds and one geometry record per physical node. `DrawioArchitectureV6Renderer` projects each planned physical node exactly once into a deterministic vertex cell, using absolute geometry for page-level nodes and project-relative geometry for nodes under project containers.
- Vertex metadata retains physical and semantic identity, full name, projection mode, project ownership, positional ownership and duplicate provenance. Labels are XML-escaped by `XElement` serialization. Project geometry is emitted as a swimlane-style boundary cell when containers are enabled.
- Renderer diagnostics report style-rule usage, fallback count, unmatched selectors, unused exact overrides, final node gaps and project-section gaps. Route diagnostics report semantic/planned/emitted edge counts, topology families, length, bends, shared segments and node intersections. Style resolution is first-match for configured rules, after exact full-name overrides and before the deterministic fallback.

## Validation and diagnostics

`IPlannedArchitectureDiagramValidator` is the boundary for planning-model, physical-scene and renderer-reconstruction validation. The active validator checks node placement, footprint ownership, physical link endpoints, canonical cardinality, duplicate provenance, geometry dimensions, containment, bounds and collisions. Diagnostics and metrics are renderer-independent and can attribute future findings to semantic, physical, grid, cell, route, lane, constraint and segment identities.

The V6 renderer emits a valid Draw.io page containing planned project boundaries and physical node vertices. Route planning and edge emission remain deferred. Renderer diagnostics report emitted/skipped vertex counts, style fallbacks and output bounds.

## Deferred logic

Collective terminal/slot allocation, full lane optimisation, crossing elimination, route-aware reconstruction validation and advanced project-transition routing remain deferred. The active route stage classifies adjacent/long downward, same-layer, upward, external and cross-project links, emits orthogonal waypoints, records endpoint projection provenance and reports hard route conflicts. Logical node projection, layer assignment, positional ownership, anchor placement, nested subtree reservations, track sizing, relative-to-absolute geometry compilation and physical vertex/edge projection are active.

The generic `DiagramModel` Draw.io renderer remains for non-Architecture diagram workflows. Architecture generation resolves only `DrawioArchitectureV6Renderer`.

## Role-aware placement

V6 preserves the useful semantic behavior of the retired renderer without copying
its pixel coordinates. The semantic graph receives deterministic dependency depth,
then configured `NodeLayerGroups` are copied into immutable V6 role rules and
resolved first-match-wins. Specific `CoordinationService`, `OrchestrationService`
and `ProcessingService` rules therefore win over a broad `Service` rule. External
and unmatched nodes remain explicit roles.

Within a dependency depth, role groups become ordered visual sublayers. The
configured baseline pattern is a rigid group and all matching nodes share one row;
other role bands receive deterministic rows in configured order. Discovery order
continues to order nodes within a band, with IDs used only as a stable fallback.
Geometry applies named spacing tiers for sibling groups, ownership boundaries, role
bands, logical layers, brokers, external nodes and project boundaries. Sparse
logical columns do not multiply visible gaps.

Placement metadata is carried into planned geometry and emitted vertex attributes:
semantic depth, role selector, role band, ownership and sibling group, spacing
policy, and final logical row/column. Renderer diagnostics aggregate role-band and
spacing-policy usage, making the hierarchy inspectable before links or routing are
introduced.

Placement precedence is explicit: project/subsystem ownership, tree root and
branch, semantic depth, subdepth/role band, configured role order, then discovery
order with IDs only as a tie-break. A physical node records its tree root, parent
semantic identity, branch and sibling order, role order, final row/column and
placement group. Role selectors refine a branch; they do not replace branch
ownership.

## Fixed edge projection

Architecture links are planned after node geometry and are emitted as explicit
`mxCell` edges. The route planner records physical and semantic link identity,
endpoint projection mode, topology family, orthogonal segments, bends and route
conflicts. The XML emitter uses absolute waypoint geometry under the page parent,
fixed bottom-source/top-destination terminal constraints, `edgeStyle=none`, and
disables automatic orthogonal-loop/jetty behavior so Draw.io does not invent a
different primary path. Edge values are empty by default; relationship kinds stay
custom metadata and are never shown as labels unless an explicit label policy is
enabled.

Connector settings are copied through the V6 request into the final edge style,
including stroke, width, dash, arrow, arrow size, opacity, font colour, rounded
state and label policy. Route inspection reconstructs source terminal, emitted
waypoints and target terminal and checks axis alignment, missing endpoints, node
intersections and shared segments. These findings remain visible in diagnostics;
the current first router is intentionally not described as congestion-complete.
