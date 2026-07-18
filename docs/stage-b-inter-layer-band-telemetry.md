# Stage B: observational inter-layer band telemetry

Stage B observes the accepted Stage A placement and authoritative logical routes. It does not move a node, change a route, assign a live Y coordinate, or participate in route selection. Observation is materialized only with explicit diagnostics; normal generation retains a dormant lazy factory.

## Input and identity

The observer accepts a coherent `PlacedGraph` and `GeneratedLogicalRoutes`. `GeneratedLogicalRoutes.EnsureCompatible` rejects a different layout revision, and callers may require an exact route revision. A band is identified by `(upper visual layer, lower visual layer, layout revision)` and exists for each adjacent pair of populated layers.

Membership is extracted from the authoritative polyline rather than inferred only from semantic depths. Every segment intersecting a band's actual vertical range contributes to a contiguous membership region. Separate entries are retained when a route leaves and later re-enters the same band. Downward memberships are classified as source transition, target transition, or through; upward memberships are return traffic.

## Interval semantics and colouring

Every horizontal segment inside a band becomes a distinct demand containing edge identity, route revision, band, segment index, role, closed X interval, terminal order, and direction. Vertical perpendicular crossings create membership but no horizontal lane demand. Non-orthogonal segments are reported as unsupported observations.

Intervals conflict when their positive overlap or endpoint separation is less than the configured parallel clearance. Demands are partitioned by compatible role, then sorted by start, end, terminal order, logical edge ID, and segment index. An active-interval sweep releases a lane only when `previousEnd + clearance <= nextStart`; the lowest released lane is reused. Sorting and ordered active/free sets give `O(B log B)` colouring for `B` demands. Reversed input enumeration is byte-deterministic.

Return demands retain current left/right direction and interval. Their overlap with ordinary downward demand is reported. A conflicting return stack contributes separately to hypothetical extent; non-conflicting return and ordinary groups may reuse vertical space.

## Required extent

For each band telemetry uses the terms `current extent`, `required extent`, and `missing extent`. Current extent is the placed gap from the bottom of the upper layer to the top of the lower layer. Required extent is observational padding plus the compatible lane stack implied by interval overlap and clearance. Independent role groups use their maximum rather than being blindly summed; a return stack is added only where it conflicts with downward demand. There is no fixed capacity or allocation-failure concept.

## Finding correlation

Existing validator findings are associated with every band containing demand from either involved route. The report lists band IDs, demand IDs, current lane assignments, missing extent, and a nullable plausibility result. A finding is plausibly band-resolvable only when at least one associated band is under-sized. This is correlation, not proof that future expansion will repair it.

## Real-graph results

Release reports were generated on 18 July 2026 with the recorded duplicated and deduplicated settings. Observer time comes from the observer's own stopwatch; enabled totals include the existing diagnostic JSON, annotated diagram, focused artefacts, and writes.

| Graph | Nodes | Routes | Layers/bands | Memberships | Demands | Max bands/route | Max lanes | Missing extent | Return demands/conflicts | Unsupported | Observer |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| StandardIo duplicated | 22 | 21 | 7/6 | 21 | 9 | 1 | 2 | 0px | 0/0 | 0 | 13.5ms |
| StandardIo deduplicated | 11 | 12 | 7/6 | 18 | 14 | 4 | 5 | 0px | 0/0 | 0 | 15.8ms |
| cCoder duplicated | 1,094 | 1,072 | 9/8 | 660 | 290 | 1 | 4 | 0px | 0/0 | 0 | 29.0ms |
| cCoder deduplicated | 180 | 294 | 7/6 | 167 | 113 | 2 | 23 | 288px | 17/15 | 20 | 38.1ms |

The sweep performed 7, 11, 308, and 203 interval comparisons respectively. Deduplicated cCoder has 1,575 shared-segment/spacing findings; 1,070 (67.9%) correlate with one of the two under-sized bands.

Five greatest deduplicated cCoder bands:

| Band | Current | Required | Missing | Hypothetical lanes | Return lanes |
| --- | ---: | ---: | ---: | ---: | ---: |
| layer 4 to 5, layout revision 3 | 140 | 296 | 156 | 23 | 12 |
| layer 0 to 1, layout revision 3 | 140 | 272 | 132 | 21 | 1 |
| layer 1 to 2 | 0 | 0 | 0 | 0 | 0 |
| layer 2 to 3 | 0 | 0 | 0 | 0 | 0 |
| layer 3 to 4 | 0 | 0 | 0 | 0 | 0 |

The corresponding stable artefact is `artifacts/stage-b/ccoder-deduplicated-worst-bands.csv`.

## Performance and parity

Controlled Release medians use one warm-up and five fresh measured processes:

| Graph | Observation disabled | Observation enabled diagnostic bundle | XML hash |
| --- | ---: | ---: | --- |
| StandardIo duplicated | 3.141s | 3.225s | `26410DE...49E85E` |
| StandardIo deduplicated | 3.284s | 3.422s | `CDA2421E...CFD23` |
| cCoder duplicated | 31.776s | 32.032s | `8DD08EDC...3373C` |
| cCoder deduplicated | 14.156s | 16.500s | `8A7C26A1...D1DE` |

Every five-run group produced one XML hash. The cCoder hashes exactly match the accepted pre-Stage-B files. Enabled total is not observer overhead alone: deduplicated diagnostic mode already writes a much larger JSON and focused bundle. The observer itself remains below 40ms in all four reports. Normal diagnostic materialization remains lazy and normal generation performs no band observation.

## Visual Studio thread telemetry

Set `STANDARDIO_THREAD_TELEMETRY=1` before starting Visual Studio to record managed thread ID, main-thread state, UTC start, and UTC end in the extension diagnostic log. UI selection, settings, save dialog, file write, and completion are explicitly scoped. Semantic analysis and renderer execution are wrapped around their existing `Task.Run` calls; every instrumented core phase, including placement, legacy routing, validation, ownership, XML construction, and Stage B observation, is captured automatically through the ambient session.

The implementation deliberately does not change scheduling. Static command flow places analysis and rendering inside `Task.Run`, but no installed-VSIX live trace was produced because Stage B explicitly forbids packaging. Live Visual Studio evidence therefore remains a manual pre-Stage-C gate. If any heavy core phase reports `StartIsMainThread=true`, treat it as a responsiveness defect and stop before packaging.

## Unsupported observations and Stage C boundary

Deduplicated cCoder reports 20 non-orthogonal logical segments crossing bands. Stage B retains and reports them; it does not repair them. Return-side direction describes current geometry, not a future routing choice. Bands with zero current and required extent represent adjacent semantic layers with no observed horizontal demand, and are intentionally retained for deterministic layer identity.

Stage C must not begin until the two under-sized cCoder bands, return/downward conflicts, unsupported segments, and a live Visual Studio thread trace have been reviewed. Stage B contains no corridor, lane-allocation, traversal, junction, global-selector, regional-optimiser, or repair-coordinator dependency.
