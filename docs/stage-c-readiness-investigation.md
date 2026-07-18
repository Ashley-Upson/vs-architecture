# Stage C readiness investigation

Stage C is paused. The deduplicated cCoder output contains 20 authoritative non-orthogonal segments crossing inter-layer bands. They are not observation, terminal, ownership, or XML reconstruction artefacts.

## Evidence

`scripts/report-stage-b-diagonals.ps1` extracts the 20 band-crossing segments from the stable deduplicated diagnostic bundle into `docs/evidence/stage-b-non-orthogonal-segments.json`. The report records route and semantic identities, route revision, segment coordinates and deltas, band memberships, producer and stage, traversal fallback diagnostics, terminal and ownership flags, traceability findings, route history, logical points, physical ownership segments, reconstructed absolute XML points, and whether the diagonal survives serialization.

All 20 segments:

- use traversal fallback;
- are interior rather than terminal segments;
- are not ownership-boundary artefacts;
- reconstruct exactly from the physical Draw.io segments;
- remain diagonal in the emitted XML;
- first appear in revision 0 geometry recorded by `CorridorLaneGeometryCompiler`.

Nineteen routes report `UNSUPPORTED_CORRIDOR_TRAVERSAL` twice. One reports `UNSUPPORTED_CORRIDOR_TRAVERSAL` and `UNSUPPORTED_JUNCTION_TOPOLOGY`. Seventeen are associated with shared-segment findings, thirteen with perpendicular crossings, and one with parallel-spacing findings. None is associated with a node collision or immediate reversal.

## Earliest producer

The route constructors in `BuildRouteCandidates` and `BuildOutsideRoute` emit orthogonal point sequences. `BuildRoute` then calls `SeparateOverlappingCorners`. That method resolves a repeated bend by changing only the bend's Y coordinate. Its adjacent points are not moved, so an originally horizontal or vertical segment can become diagonal. Candidate construction subsequently retains the accepted route as a fallback candidate. Corridor traversal cannot represent the diagonal, and lane compilation preserves the fallback instead of inventing different geometry.

The selected candidate, logical route, ownership compilation, reconstructed absolute physical geometry, and emitted XML consequently agree. Draw.io displays the diagonal and preserves it on save/reopen. Container movement applies the existing piecewise ownership delta to the relevant physical segment; it does not make the segment orthogonal.

## Band-demand treatment

The 20 segments are invalid legacy fallback geometry, not valid horizontal lane demand. Projecting their X spans into band lanes would assign capacity to geometry that the traversal model explicitly cannot represent. They therefore contribute no lane demand until their producer is corrected. The provisional missing extents remain:

- layer 0 to 1: `132px`;
- layer 4 to 5: `156px`.

This is not evidence that Stage C may safely regenerate route geometry. The opposite applies: because the final authoritative XML contains unsupported diagonals, Stage C must not move layer positions until a focused defect tranche corrects corner separation and proves route parity.

## Visual Studio responsiveness audit

The command handler is scheduled with `JoinableTaskFactory.RunAsync`. It switches to the UI thread for selection, settings, save dialogs, progress updates, and completion UI. Semantic analysis and rendering run inside awaited `Task.Run` operations. File writing is asynchronous. The progress window is modeless, uses a marquee progress bar, exposes cancellation, and updates at analysis, layout/routing, and writing boundaries. No blocking `.Wait()` or `.Result` call exists in the generation path.

`STANDARDIO_THREAD_TELEMETRY=1` records phase thread identity after generation completes. The existing local diagnostic log predates the gated telemetry run and contains no qualifying generation trace. A live duplicated and deduplicated Visual Studio exercise is therefore still required before Stage C. This repository-side audit does not claim that unmeasured UI responsiveness has been verified.

## Decision

The observer and the provisional extent calculations remain valid, but Stage C is not ready. Correct the concrete `SeparateOverlappingCorners` defect in a separate regression-led tranche, regenerate the deduplicated cCoder output, require zero unsupported final segments, and then repeat the live Visual Studio responsiveness gate. No routing or placement production logic was changed during this investigation.
