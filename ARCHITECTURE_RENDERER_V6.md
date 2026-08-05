# Architecture Renderer V6

V6 is intentionally at a structural reset point. The active boundary is:

```text
Roslyn semantic analysis
    -> semantic/physical projection
    -> logical project grids
    -> logical grid node placement
    -> topology classification and abstract grid routes
    -> straight runs and collective structural demand
    -> logical lane allocation and routing-capacity constraints
    -> physical row/column sizing and relative/absolute geometry (deferred)
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
- domain-local horizontal/vertical lane allocations, endpoint and approach
  ordering, turn/crossing records and provenance-bearing capacity constraints;
- immutable `PlannedArchitectureDiagram`, request/policy contracts and diagnostics.

The route-step, endpoint, transition, straight-run, lane and demand models are
populated structurally. Lane ordinals are allocated within explicit logical
domains; no pixel sizes or coordinates are assigned.

## Explicitly deferred

The following stages are not active and must not be inferred from metadata:

- physical terminal sizing;
- physical track sizing;
- row/column sizing;
- relative and absolute geometry compilation;
- physical-scene validation of a completed geometry;
- Draw.io node/container/edge emission.

Logical node placement, topology classification, abstract grid routes,
destination approaches, project transitions, straight-run compilation,
collective demand, logical lane allocation and routing-capacity constraints are
active. Physical sizing and rendering are not.

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
The planner reports `V6GeometryDeferred`; physical sizing is represented as
deferred in the stage status while logical capacity constraints are complete.
The renderer reports logical-placement and abstract-route completion, the
minimal-page state and deferred link emission.

## Next implementation boundary

The next tranche should reconcile node footprint expansion requirements with
physical row/column sizing and geometry. It should not introduce coordinate-first
route candidates or physical geometry into the lane allocator.
