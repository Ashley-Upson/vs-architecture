# Current routing audit evidence catalogue

This catalogue is deliberately small. It points to deterministic artefacts already generated from the accepted settings rather than creating hundreds of extracts. Coordinates and counts are diagnostic evidence, not claims that the corresponding visibility rule is satisfied.

## Stage C activation fixture

`GroupedVerticalBandPlannerTests.Eligible_under_sized_adjacent_graph_uses_grouped_pipeline_as_one_authority` is the smallest checked fixture. It contains one source and two targets in the immediately lower layer, with an intentionally under-sized band. The grouped path moves the lower layer, allocates a complete group, regenerates both routes from their semantic endpoints, returns `GroupedVerticalBandConverged`, and supplies empty legacy corridor/lane/traversal compatibility models. The paired skipped-layer test is the closest legacy-path comparison; production has no runtime switch for forcing both authorities over precisely the same graph.

```text
before                             grouped result

          source                           source
          /    \\                           |   |
   insufficient band              expanded band with lanes
        /          \\                     /     \
     target       target                target   target
```

The exact changed region is the first deficient inter-layer band plus the lower layer and every layer below it. The upper layer is fixed. Only routes incident to or crossing that band are invalidated by `GroupedVerticalBandPlanner.Apply`.

## cCoder fallback map

The accepted deduplicated graph has six observed bands, 167 memberships and 113 demands. It has 17 return demands and 20 unsupported/non-orthogonal shapes. Missing extent is 132 px in band 0-1 and 156 px in band 4-5.

```text
graph (180 placed nodes, 294 routes)
  |
  +-- Stage B observes 6 bands
  |
  +-- Supports rejects before Plan
        +-- UnsupportedShapeCount = 20
        +-- exposure-tree node IDs exist
        +-- route set is not exclusively depth + 1 / one-band

groups discovered: 0
grouped routes regenerated: 0
constraints applied: 0
legacy routes generated: 294
```

There is currently no reason code identifying which failed predicate was decisive. The map therefore reports all predicates known to reject this graph; it does not invent a single fallback reason.

## Accepted cCoder evidence

- Full diagram: `artifacts/stage-b/ccoder-deduplicated.drawio`
- Current production fallback diagram: `artifacts/stage-c-grouped/ccoder-deduplicated.drawio`
- Node-intersection focus: `artifacts/stage-b/ccoder-deduplicated-focused/node-intersections.drawio`
- Shared/ambiguous geometry focus: `artifacts/stage-b/ccoder-deduplicated-focused/shared-or-ambiguous-geometry.drawio`
- Spacing focus: `artifacts/stage-b/ccoder-deduplicated-focused/spacing-problems.drawio`
- Unsupported diagonal trace: `docs/evidence/stage-b-non-orthogonal-segments.json`

The diagnostic counts provide at least one deterministic example of every requested retained defect class except a clean crossover, which is an accepted contact class rather than a defect:

| Requested example | Evidence |
|---|---|
| route through node | 10 `NodeCollision` findings and the node-intersection focus file |
| shared collinear segment | 45 `SharedSegment` findings and the shared-geometry focus file |
| ambiguous shared bend | 3 `ReusedBend` findings and the shared-geometry focus file |
| clean crossover | `GroupedSpacingTests.Classifies_strict_interior_crossing_as_clean_crossover`; the classifier requires a strict interior intersection and neither route turning there |
| spacing deficit | 20 spacing findings and the spacing focus file |
| current diagonal | 20 unsupported band-crossing shapes in `stage-b-non-orthogonal-segments.json` |

The 20 diagonals are not corrected by Stage C because `UnsupportedShapeCount > 0` rejects the whole graph before grouping. Production can still create them in `RenderLayout.BuildRoute` when `SeparateOverlappingCorners` moves a reused corner without regenerating a coherent orthogonal dogleg.

## Reproducibility

The Stage B and current production cCoder files are both 12,455,618 bytes and both hash to:

```text
8A7C26A1FE8B4A7460996FDBA643DBDFDC603F80CCE215A34715988E0E72D1DE
```

The source revision used for the controlled audit was `459dffa707954152713f473d9c18c57f6efc4da4`; the effective settings file was `artifacts/node-duplication/real-project-deduplicated-settings.json`.
