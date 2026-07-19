# Mixed-boundary attribution and multi-band downward integration

This evidence was generated from the deduplicated cCoder project using `artifacts/node-duplication/real-project-deduplicated-settings.json` and the CLI-only `--development-common-authority-trial` path. Normal production settings and extension authority remain unchanged.

## Unlock analysis

Semantic attribution found 189 adjacent-downward, 31 multi-band-downward, 32 same-layer and 42 upward/return routes. Secondary current-geometry reasons are retained independently, so `MultipleBand` and `NonOrthogonal` do not hide semantic topology.

| Candidate family | Newly covered | Fully supported components | Adjacent routes unlocked | Deficient bands unlocked | Remaining unsupported |
|---|---:|---:|---:|---:|---:|
| General one-or-more-band downward | 150 | 25 | 26 | 0 | 268 |
| Unsupported terminal topology | 25 | 5 | 5 | 0 | 289 |
| Upward/return | 42 | 2 | 2 | 0 | 292 |
| Same-layer | 32 | 2 | 2 | 0 | 292 |
| Non-orthogonal regeneration only | 7 | 2 | 2 | 0 | 292 |

General downward was therefore the highest-leverage coherent family and was implemented. Adjacent routes are the one-band case of the shared demand factory; both one- and multi-band routes use `DeterministicRailAllocator`, the same contact/component policy, the persistent constraint store/materializer and full traceability validation.

## Revised full trial

The trial now finds 201 locally supported routes and 28 fully supported route components. Twenty-six components containing 27 routes are accepted. One component is rejected as a mixed ordinary interaction component, one is rejected because its required movement scope is mixed, and one candidate is rolled back after full validation. The remaining 267 routes retain legacy geometry.

Accepted route geometry changes from 4,954px/58 bends to 5,286px/6 bends. Maximum individual length increase is 116px and maximum envelope expansion is 58px. No route is flagged as unreasonable. Accepted common/legacy interactions consist of 287 clean perpendicular crossovers; no boundary hard finding is introduced.

The full hard set changes only by removing one immediate reversal (34 to 33). Node collision, shared segment, parallel spacing, reused bend, non-orthogonal segment, bend-involved contact and endpoint-to-interior counts remain unchanged. This is expected because the accepted components were already isolated from the major unsupported legacy defects.

## Deficient bands and movement closure

| Band | Available | Required | Missing | Adjacent | Multi-band | Same-layer | Upward/return | Status |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| 0→1 | 140 | 272 | 132 | 49 | 11 | 3 | 1 | unsupported same-layer/upward boundary |
| 4→5 | 140 | 308 | 168 | 24 | 9 | 16 | 23 | unsupported same-layer/upward boundary |

The common allocator also proposes a 132px `MinimumY` increase for the depth-2 lower suffix. Immutable-base materialisation proves it would move five layers and 120 nodes and invalidate 249 routes. That invalidation closure includes unsupported routes, so the constraint is rejected and no node is moved in the accepted output.

Neither original deficient band becomes fully supported. Consequently no deficient-band before/after artefacts are produced.

## Artefacts

- `before.drawio`: production baseline, SHA-256 `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.
- `after.drawio`: accepted general-downward mixed trial.
- `trial-report.json`: route-level attribution, unlock table, component decisions, movement closure, validation, quality, boundary and timing data.

The main avoidable development-trial cost is repeated complete validation after each candidate component: component regeneration/validation took approximately 3.7 seconds. Production generation does not execute this path.

Normal production was measured with one excluded warm-up and five fresh Release processes, diagnostics and telemetry disabled. All outputs retained the accepted hash. Measured generation was 13,947–14,111ms with a 13,999ms median, remaining near the preceding 14.1-second baseline. Raw results are in `normal-generation-results.csv`.
