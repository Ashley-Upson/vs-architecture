# Grouped spacing Stage C slice

The first Stage C slice replaces legacy route-by-route spacing correction only for a complete graph consisting exclusively of ordinary orthogonal dependencies from one layer to the immediately following layer, where an observed band is under-sized. Authority is selected once for the generation. Exposure trees, skipped-layer routes, upward routes, non-orthogonal routes and graphs with sufficient existing extent continue through the complete legacy pipeline.

## Implemented model

- Stable `SpacingConstraintKey` ordering is Y, X, scope and stable identity.
- `MonotonicSpacingConstraintStore` merges competing proposals through `max()` and never reduces a stored minimum.
- Inclusive interval classification distinguishes disjoint intervals, endpoint contact and positive overlap.
- Band demands form deterministic connected components, including transitive A-B-C conflicts.
- Each group receives a complete deterministic lane assignment and one required extent.
- A strict interior perpendicular intersection with no turn is a clean crossover; an intersection involving a turn is ambiguous.
- The full missing vertical delta moves the lower layer and every layer below it while leaving upper layers fixed.
- Routes incident to and crossing the changed supported band are invalidated and regenerated from semantic endpoints against the revised placement. Existing waypoints are not translated.
- Grouped routes bypass legacy corridors and route repair as one authority.
- Unsupported graphs fall back before any grouped placement or route mutation.

The supported compiler preserves bottom-edge source and top-edge target terminals, creates orthogonal source/target transitions and an assigned horizontal band lane, normalizes once, and validates the complete result.

## Telemetry

Diagnostic JSON exposes the supported subset, convergence iterations, groups, routes and segments, current/required lanes and extent, constraints, invalidated routes, group count, collapsed pairwise conflicts, proposal/increase counts and interval comparisons. Stage timings include grouped materialization and regeneration. Parallel execution is not enabled in this first slice; proposal models are immutable and deterministic so later independent-region parallelism does not require shared mutation.

## Fixtures

Focused tests cover inclusive endpoint classification, ambiguous bends, clean crossovers, transitive grouping, lane reuse, monotonic constraints, deterministic proposal merge, exact full vertical delta, lower-only movement, endpoint/band invalidation, route regeneration, orthogonality, unsupported fallback, reversed enumeration and a generation-level authority boundary.

Horizontal subtree/project constraints, project reflow, skipped layers, return routes, canonical cross-branch groups, X-local vertical lanes and parallel proposal analysis remain deliberately unsupported.

## Real-project fallback parity

The rebuilt Release CLI generated deduplicated cCoder with the recorded settings. The graph remained wholly on the legacy pipeline and produced the exact accepted Stage B SHA-256 hash `8A7C26A1FE8B4A7460996FDBA643DC7A0C3E785B746279E3677630D1A80BD1DE`.

Findings remained:

- node intersections: 10;
- shared segments: 45;
- spacing deficits: 20;
- reused bends: 3.

Stage B remained 167 memberships, 113 demands, 17 return demands and 20 unsupported shapes. Missing extent remained 132px for layer 0 to 1 and 156px for layer 4 to 5. This proves unsupported fallback parity; it is not evidence that the new slice improves cCoder yet.

No VSIX was packaged. The next supported subset should be chosen from a concrete compact fixture, with skipped-layer downward routes the smallest natural extension before canonical, return or cross-project groups.
