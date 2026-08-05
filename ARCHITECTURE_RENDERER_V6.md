# Architecture Renderer V6

V6 is intentionally at a structural reset point. The active boundary is:

```text
Roslyn semantic analysis
    -> semantic/physical projection
    -> logical project grids
    -> node placement (deferred)
    -> abstract grid routes (deferred)
    -> straight runs and collective lane demand (deferred)
    -> row/column sizing and relative/absolute geometry (deferred)
    -> PlannedArchitectureDiagram
    -> IArchitectureDiagramRenderer<DrawioPage>
    -> minimal Draw.io document shell
```

## Active structural work

`ArchitectureDiagramV6Planner` retains semantic-to-physical projection. It preserves:

- canonical physical identity and semantic mappings;
- configured duplicate policy types at the request boundary;
- project ownership, roots, externals, standalones and cycle detection;
- physical links and semantic-link mappings;
- empty `ProjectRoutingGrid` and `DiagramRoutingGrid` shells;
- immutable `PlannedArchitectureDiagram`, request/policy contracts and diagnostics.

The abstract grid route, route-step, endpoint, transition, straight-run, lane,
track-constraint, reservation and relative/absolute geometry models remain
renderer-independent contracts for the next planner. They are not populated by
the current structural planner.

## Explicitly deferred

The following stages are not active and must not be inferred from metadata:

- node placement, depth/subdepth coordinates and role bands;
- subtree reservation calculation and footprint allocation;
- topology classification;
- terminal allocation;
- abstract routes through grid cells;
- straight-run compilation and collective lane demand;
- row/column sizing;
- relative and absolute geometry compilation;
- physical-scene validation of a completed geometry;
- Draw.io node/container/edge emission.

The previous coordinate-first route builder, candidate scoring, rectangle-derived
lane selection, sequential route reservation and degraded fallback route have been
removed from the active path. No route builder or geometry builder is currently
reachable from `ArchitectureDiagramV6Planner`.

## Renderer boundary

`DrawioArchitectureV6Renderer` accepts only `PlannedArchitectureDiagram` and emits
the minimal valid Draw.io page shell: root cells `0` and `1`, with no project
containers, vertices, edges or geometry. It records explicit deferred-stage
diagnostics. It performs no placement, sizing, routing, style resolution or XML
layout calculation.

Generic Draw.io document composition and non-Architecture renderers remain active.
CLI and VSIX continue to resolve the V6 Architecture planner/renderer boundary.

## Validation and diagnostics

`IPlannedArchitectureDiagramValidator` and immutable diagnostics remain available
for completed plans. The intentionally incomplete structural plan is not passed
through completed-geometry validation because it has no placements or geometry.
The planner reports `V6PlacementDeferred`, `V6RoutePlanningDeferred`,
`V6SizingDeferred` and `V6GeometryDeferred`; the renderer reports the minimal-page
and deferred-emission state.

## Next implementation boundary

The next tranche should implement logical grid construction and node placement
against the retained grid/cell/reservation contracts. It should not revive the
deleted rectangle router or introduce coordinate-first route candidates.
