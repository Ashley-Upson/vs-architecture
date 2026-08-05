# Architecture Renderer V6

V6 is intentionally at a structural reset point. The active boundary is:

```text
Roslyn semantic analysis
    -> semantic/physical projection
    -> logical project grids
    -> logical grid node placement
    -> topology classification and abstract grid routes
    -> straight runs and collective structural demand
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
- sparse `ProjectRoutingGrid` instances containing logical anchors, odd
  footprints and nested subtree reservations, plus an empty diagram grid;
- deterministic positional ownership, depth metadata, baseline rows, external
  placement and compact standalone regions;
- topology-owned abstract routes, destination approaches, project transitions,
  provisional straight runs and collective endpoint/turn demand;
- immutable `PlannedArchitectureDiagram`, request/policy contracts and diagnostics.

The route-step, endpoint, transition, straight-run, lane and demand models are
populated structurally. They contain logical cells only; no lane ordinals,
pixel sizes or coordinates are assigned.

## Explicitly deferred

The following stages are not active and must not be inferred from metadata:

- final terminal allocation;
- lane allocation;
- row/column sizing;
- relative and absolute geometry compilation;
- physical-scene validation of a completed geometry;
- Draw.io node/container/edge emission.

Logical node placement, topology classification, abstract grid routes,
destination approaches, project transitions, straight-run compilation and
collective structural demand are active. Pixel sizing, lane allocation and
rendering are not.

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
The planner reports `V6SizingDeferred` and
`V6GeometryDeferred`; the renderer reports logical-placement completion, the
abstract-route completion, the minimal-page state and deferred link emission.

## Next implementation boundary

The next tranche should allocate lanes and size logical rows/columns against the
retained demand and route structures. It should not introduce coordinate-first
route candidates or physical geometry into the route planner.
