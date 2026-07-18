# Layout hierarchy and placement architecture

Stage A separates deterministic node placement from semantic analysis, Draw.io ownership, and route generation. The active generation path is:

```text
RenderGraph
-> HierarchyAnalyzer
-> LayoutHierarchy
-> PlacementPipeline
-> immutable PlacedGraph
-> LegacyRoutingPipeline
```

The legacy router remains the only route authority. Placement does not construct candidates, corridors, lanes, traversals, validation findings, or repairs.

## Three different parent relationships

Semantic parents are dependency sources and a node may have several. A layout parent is the single deterministic relationship used for hierarchy and placement. A Draw.io parent is coordinate ownership used when moving project containers. These relationships are deliberately independent.

An ordinary node uses its deterministic dependency parent. A canonical multi-parent node retains the first-placement parent recorded by `RenderGraph`; later semantic parents do not reparent or recenter it. A duplicated exposure clone uses its cloned exposure-path parent. Roots have no layout parent. External nodes retain their semantic project ownership without becoming hierarchy parents.

## SCC and layer analysis

`HierarchyAnalyzer` computes strongly connected components, their stable order, visual layers, roots, and edge direction. Semantic cycles are condensed for analysis. No parent link is created between members of the same component, so `LayoutHierarchy` remains acyclic even when `RenderGraph` is cyclic.

Stable ordering uses render-node order followed by ordinal IDs. Explicit first-placement relationships take precedence over inferred incoming dependencies. Provenance distinguishes ordinary nodes, canonical first placements, duplicated exposure clones, and external dependencies.

## Base placement and translations

`PlacementPipeline` preserves the existing ordinary and exposure-tree placement algorithms, including their current spacing and alignment behaviour. It records the first rectangle assigned to every node as `NodeBasePlacement`, then records the difference between that rectangle and the final materialized rectangle in `LayoutTranslations`.

For Stage A, each node has one composed X/Y translation. Materialization is exact:

```text
materialized rectangle = base rectangle + node translation
```

This representation exposes placement movement without changing generated coordinates. Later stages may replace the single composed offset with attributable subtree and layer-boundary offsets while preserving the same immutable boundary.

Exposure-tree measurement, duplicated path placement, canonical reuse, regex duplication exceptions, and external dependency positioning are a coherent section of `PlacementPipeline`. They do not copy route-generation logic.

## Projects and ownership

`ProjectPlacementResult` contains the initial project rectangles, stable project order, node membership, root-owned nodes, and project-owned external nodes. These are initial placement bounds. The existing post-routing ownership compiler may still enlarge project bounds to include owned route geometry.

Draw.io ownership segmentation and relative-coordinate serialization remain downstream and do not affect `LayoutHierarchy`.

## Immutable placement revisions

`PlacedGraph` snapshots:

- the `RenderGraph` and `LayoutHierarchy`;
- node base placements and translations;
- materialized node layouts;
- typed project placement and node ownership;
- one `LayoutRevision`.

The initial placement is revision 0. When the legacy capacity adapter expands layers, `PlacedGraph.Revise` creates a new snapshot, increments the revision, updates the hierarchy revision, recomputes translations from the stable bases, and replaces project rectangles. It does not mutate the earlier placement.

Generated, normalized, and validated logical routes retain the layout revision they were built against. Compatibility checks reject stale route state. Diagnostics expose the final layout revision and the number of revisions created.

## Legacy-router boundary

`LegacyRoutingPipeline` consumes one coherent `PlacedGraph`. Its internal algorithms may use the materialized node and project dictionaries, but revised placement can only return through `PlacedGraph.Revise`. The adapter contains the transitional mutable operations; placement components remain unaware of routing.

Stage B may observe inter-layer bands from this boundary. It must not move hierarchy, ownership, or legacy route responsibilities back into placement.
