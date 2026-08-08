# Architecture Renderer V6 Contract

This is the normative source of truth for the Architecture V6 planner and
Draw.io renderer. Normative terms are intentional: **MUST** and **MUST NOT**
are requirements, **SHOULD** is a strong preference, and **MAY** is optional.

## Authority Model

The required pipeline is:

```text
semantic projection
-> positional ownership
-> reserved layer planning
-> ordinary bottom-up layer solving
-> terminal-capacity node sizing
-> local tree construction
-> whole-tree packing
-> final logical grid
-> authoritative abstract routing
-> lane allocation
-> footprint convergence
-> final route rebuild where required
-> boundary components
-> physical track sizing
-> physical scene
-> final validation
-> mechanical renderer
```

Each decision has one authority. Placement owns node positions and footprints;
routing owns cell topology; lane allocation owns lane identity and demand;
sizing owns track extents; materialisation owns physical geometry; rendering
only projects the completed scene.

## Placement

Configured layer rules MUST run first and MUST use first-match semantics.
Reserved categories are ordered, and ordinary nodes MUST NOT occupy reserved
layers.

Ordinary layers MUST be solved bottom-up from dependencies. External is the
bottom anchor. A root is not inherently the top layer. Ordinary nodes use the
lowest valid non-reserved layer, which MAY be above, between or below reserved
bands. Stale analysed depth MUST NOT override final structural placement.

The authoritative positional parent is deterministic. Multiple semantic parents
MUST NOT imply averaged placement. The parent MUST be above its child.

Tree membership MUST be fixed before X placement. Each tree is built in local
coordinates, child subtrees are recursively constructed, and a single child
MUST share the parent centre X. A multi-child parent SHOULD be centred over its
immediate child group. Descendant contours control spacing, not parent-centre
semantics. Completed trees are atomic: global packing MAY translate whole trees
only. Per-layer contours determine tree spacing and unrelated nodes MUST NOT be
inserted into another tree's internal gaps.

Final node X MUST equal `LocalTreeX + WholeTreeTranslationX`; unexplained
per-node deltas are invalid.

## Sizing And Terminals

Visible node width MUST be the maximum of label need and terminal-capacity need.
Terminal-capacity expansion MUST affect visible width. Logical horizontal spans
MUST remain odd; an even required span MUST round up to the next odd span, with
symmetrical expansion around the centre. Sizing MUST complete before final tree
packing.

Terminal groups MUST be centred and evenly spaced. Route geometry determines
slot ordering, not slot-group position. Sources attach to bottom edges and
destinations to top edges. Ports MUST be unique unless an explicit shared-port
semantic model exists. A terminal slot owns its endpoint-local vertical X.

## Routing

There MUST be one topology authority. Sources exit down and targets enter from
above with a final downward segment. Routes MUST be orthogonal, retain all
traversed cells, avoid unrelated node interiors and contain no implicit jumps.

There MUST NOT be a second router after topology selection. Boundary
reconciliation MUST represent the selected topology and MUST NOT choose an
alternate corridor, overshoot or return. Endpoint handoffs MUST be explicit
planned components. Point reduction MUST NOT conceal invalid topology.

## Lanes And Corridors

Lane demand MUST precede final track sizing, and lane coordinates and sizing
MUST use the same formula. Lane spacing comes from preserved configuration.
Unrelated overlapping collinear intervals MUST use distinct lanes. Exact shared
non-zero intervals and under-spaced parallel intervals are invalid. Dense cells
MUST retain crossing information; crossings are point intersections, not shared
segments.

## Backtracking

Immediate retracing is invalid, including `R -> L -> R`, `L -> R -> L`,
`U -> D -> U`, and `D -> U -> D`. A route is also invalid when an intermediate
point overshoots a required coordinate and immediately returns across that
interval. Detection applies within components, across component boundaries,
through endpoint handoffs and across the final physical polyline.

Legitimate obstacle detours MAY remain, provided they do not immediately retrace
an interval and remain within their authorised corridor.

## Connector Style Authority

Style precedence is resolved once during planning:

1. exact configured override;
2. first matching configured style rule in configuration order;
3. external style for external nodes;
4. deterministic fallback style.

Connector style is taken from the configured connector settings and is resolved
onto each planned physical relationship. The renderer MUST NOT derive connector
colour, width, arrows, opacity, dash settings or extra style from node
appearance. In particular, destination fill colour MUST NOT replace connector
stroke colour.

The renderer MUST project every planned connector property mechanically:
stroke colour, width, opacity, dash state and pattern, start/end arrows,
arrow size, fill flags, font colour, labels and raw extra style.

## Renderer Contract

The renderer MUST be mechanical. It MAY create Draw.io cells, transform final
coordinates, emit final node geometry, relationships, terminal ratios,
waypoints, styles and metadata.

It MUST NOT choose placement, route, repair geometry, choose connector colours,
derive semantic style from node appearance, or omit planned relationships
without a hard fidelity finding.

## Validation And Output Policy

Final validation MUST inspect the exact physical geometry handed to the
renderer. It covers node placement, lowest-valid layers, tree atomicity,
orthogonality, continuity, corridor and lane containment, endpoint direction,
terminal spacing and centring, backtracking, shared segments, parallel
clearance, route-node intersections and renderer fidelity.

Strict mode MUST reject hard findings. Normal mode MAY emit best-effort output,
but MUST expose all findings and retain represented invalid relationships.

## Configuration Authority

Integration generation uses:

`C:\Users\Ash\Documents\codex-artifacts\architecture-user-settings.json`

unless an explicit settings argument overrides it. An explicit settings file
has precedence over the preserved integration configuration, which has
precedence over repository defaults. The generator MUST NOT silently switch to
repository defaults while an explicit readable source is available.

## Implementation Notes

The current implementation represents logical grids, route components, lanes,
track sizing, absolute scene geometry and Draw.io projection as separate
records. These types MAY be refactored, but the authority boundaries and
observable rules above MUST remain intact.

## Decision History

- 2026-08-08 (`bd80b10` plus routing diagnostics): connector styles remain planner-owned and are emitted mechanically; physical evidence now records a first-invalid-stage classification, and same-grid immediate reversals are rejected before lane allocation. The active same-project topology authority remains the contiguous grid search; the legacy `CompleteTurnCorridor` implementation has no production caller and is not part of route construction.

| Date / commit | Decision | Reason | Authority affected |
|---|---|---|---|
| 2026-08-08 / `c54cac8` | Planner findings feed final validation and strict eligibility; normal mode remains best effort. | Keep output available while making strict validation meaningful. | Validation/output policy |
| 2026-08-08 / `9d961f7`, `8aa1ddb` | Destination approach and final-polyline backtracking remain validated; endpoint materialisation may not silently hide reversals. | Preserve physical correctness while correcting endpoint boundary ownership. | Routing/materialisation |
