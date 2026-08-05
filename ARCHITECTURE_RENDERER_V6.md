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
    -> frozen structural grid audit
    -> grid-authoritative track sizing and relative geometry
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
- deterministic positive track sizing from existing rows, columns, footprints,
  route capacity and project constraints;
- cumulative relative offsets, complete node footprint envelopes, sparse
  subtree interval geometry, project bounds and sizing provenance;
- immutable `PlannedArchitectureDiagram`, request/policy contracts and diagnostics.

The route-step, endpoint, transition, straight-run, lane and demand models are
populated structurally. Route planning may add sparse cells only at the
intersection of an existing row and column. It cannot create rows, columns,
structural regions, footprints or reservations. Structural row and column
cardinality is frozen before the first route is planned.

## Explicitly deferred

The following stages are not active and must not be inferred from metadata:

- absolute geometry compilation;
- physical-scene validation of a completed geometry;
- Draw.io node/container/edge emission.

Logical node placement, topology classification, abstract grid routes,
destination approaches, project transitions, straight-run compilation,
collective demand, logical lane allocation and routing-capacity constraints are
complete. Track sizing and relative geometry consume those existing structures
without creating tracks, routes, lanes or coordinates of their own. Absolute
route geometry and rendering are not active.

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
for completed plans. Relative sizing validation checks track positivity,
constraint satisfaction, structural cardinality, grid bounds, node accounting,
footprint-envelope equality, project containment, sparse reservation intervals
and relative node overlap. Missing measured project-label input is reported
explicitly; configured header height is not presented as a measured label
obstruction. Absolute geometry validation remains deferred.
The planner reports `V6AbsoluteGeometryDeferred`; logical placement, abstract
routing, lane allocation, physical track sizing and relative geometry are
complete.
The renderer reports logical-placement and abstract-route completion, the
minimal-page state and deferred link emission.

## Next implementation boundary

The next implementation boundary is absolute geometry compilation. It should
consume relative geometry and preserve grid ownership; it should not introduce
route-owned tracks, coordinate-first route candidates or renderer layout
decisions.
