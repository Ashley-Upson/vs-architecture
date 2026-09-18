# Captured layout regressions

These JSON files are structural RenderProject snapshots captured from the local ContentManagement architecture documents on 2026-09-17. They do not require a checkout or build of ContentManagement.

- WebApplicationExtensions: 159 nodes, 253 links, from the duplicates document.
- ContentManagementCombined: 278 nodes, 367 links, from the combined document available at capture time.

Labels are normalized to type names; text runs are omitted. Node rectangles, category colours, identities and dependency endpoints retain the layout evidence. Initial coordinates and container width provide the observed geometry and a width ceiling. Tests rebuild initial placement, run to completion, and then lay out the same model again to prove stability. Node and edge counts must be preserved.

Do not refresh fixtures merely to accommodate a failing assertion. Review graph or requirement changes explicitly. These are intentionally larger integration regressions; use a test filter excluding LargeTreeConvergenceTests for a faster inner development loop, and run them before accepting layout changes.
