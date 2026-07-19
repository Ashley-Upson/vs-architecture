# Positional hierarchy and subtree envelopes

This development-only tranche adds one authoritative positional hierarchy to each immutable `PlacedGraph`. It does not change normal node placement or link-path authority.

## Parent selection

Eligible positional parents are incoming semantic parents in the same project and outside the node's strongly connected component. Candidates are ordered deterministically by:

1. clear direct downward path;
2. least horizontal movement;
3. shortest connection distance;
4. leftmost parent;
5. stable node ID.

Each selection records all candidate scores and the reason that distinguished the winner. A node has at most one positional parent; semantic links to every other parent remain intact.

## Envelopes

`PositionalSubtreeEnvelope` records overall bounds, bounds and left/right boundaries per layer, minimum/maximum layer, project, and positional revision. Envelopes are built bottom-up once per placement revision, avoiding repeated subtree scans. Natural node rectangles are used, so measured text width and connection-span width are retained.

## Movement and compaction

Horizontal persistent constraints support leaf nodes, complete positional subtrees, ordered sibling-subtree suffixes, projects, and ordered project suffixes. A node with descendants is rejected as an individual movement scope. Materialisation starts from immutable placement, creates a new revision, and invalidates incident links.

The development compactor compares the closest occupied per-layer envelope boundaries, preserves subtree order, and shifts complete root subtrees to configured spacing. Reserved vertical-column ownership is intentionally deferred to the vertical-column tranche; the compactor is not connected to normal production before that input exists.

## Verification

- 14 focused positional hierarchy, coherent movement, and compaction tests passed at the final focused checkpoint.
- Full solution tests: 359 passed.
- Release solution build passed with the six existing VSIX warnings.
- Fresh normal cCoder generation: 14,723ms.
- Normal output SHA-256: `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.

No VSIX was packaged.
