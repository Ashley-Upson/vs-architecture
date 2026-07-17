# Draw.io routing authority experiment

Run on 2026-07-17 against `https://app.diagrams.net/` using
`routing-authority.drawio`.

The fixture compares identical translated waypoint geometry using:

1. `orthogonalEdgeStyle`
2. `segmentEdgeStyle`
3. `edgeStyle=none;noEdgeStyle=1;orthogonal=0`

On initial load, all three edges rendered and retained their supplied waypoint
geometry. After saving to a new `.drawio` file, every style string and waypoint
coordinate was preserved. The exporter-owned form displayed the prescribed
polyline without requesting automatic orthogonal routing.

Production output therefore uses the exporter-owned form with fixed entry and
exit ratios and `entryPerimeter=0;exitPerimeter=0`. The edge remains connected
to its source and target, so moving a terminal may change its terminal access
segment; the exporter owns the saved interior waypoint geometry.

The comparison fixture remains checked in so a Draw.io version upgrade can be
round-tripped again before changing the serialization contract.
