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
    -> frozen structural grid audit (sizing deferred)
    -> absolute geometry and renderer (deferred)
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
  footprints, nested subtree reservations and shared inter-layer routing rows;
  the diagram grid is created by placement before routing;
- deterministic positional ownership, depth metadata, baseline rows, external
  placement and compact standalone regions;
- topology-owned abstract routes, destination approaches, project transitions,
  provisional straight runs and collective endpoint/turn demand;
- domain-local horizontal/vertical lane allocations, endpoint and approach
  ordering, turn/crossing records and provenance-bearing capacity constraints;
- authoritative logical grid rows/columns, odd node footprints and nested
  subtree reservations;
- explicit row/column track roles and creation provenance;
- immutable `PlannedArchitectureDiagram`, request/policy contracts and diagnostics.

The route-step, endpoint, transition, straight-run, lane and demand models are
populated structurally. Route planning may add sparse cells only at the
intersection of an existing row and column. It cannot create rows, columns,
structural regions, footprints or reservations. Structural row and column
cardinality is frozen before the first route is planned.

## Explicitly deferred

The following stages are not active and must not be inferred from metadata:

- physical track sizing and relative geometry;
- absolute geometry compilation;
- physical-scene validation of a completed geometry;
- Draw.io node/container/edge emission.

Logical node placement, topology classification, abstract grid routes,
destination approaches, project transitions, straight-run compilation,
collective demand, logical lane allocation, routing-capacity constraints,
physical track sizing and relative geometry are deferred until frozen-grid
cardinality is accepted. Absolute route geometry and rendering are not active.

The previous coordinate-first route builder, candidate scoring, rectangle-derived
lane selection, sequential route reservation and degraded fallback route have been
removed from the active path. The active route planner consumes only the frozen
logical grid. A route cell is usage metadata, not a new structural track.

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
for completed plans. Relative geometry validation now checks track positivity,
node accounting, footprint-envelope equality and relative node overlap; absolute
geometry validation remains deferred.
The planner reports `V6PhysicalSizingDeferred` and
`V6AbsoluteGeometryDeferred`; logical placement, abstract routing and lane
allocation are complete while physical sizing and geometry remain deferred.
The renderer reports logical-placement and abstract-route completion, the
minimal-page state and deferred link emission.

## Next implementation boundary

The next tranche should review and accept frozen-grid cardinality, then compile
track sizing and relative geometry. It should not introduce route-owned tracks
or coordinate-first route candidates.
