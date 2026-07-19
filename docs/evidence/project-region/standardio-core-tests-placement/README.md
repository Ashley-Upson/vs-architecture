# Core.Tests placement and compactness evidence

The before artifact is `../standardio-core-tests/common-after.drawio` at commit `b5baf4d`. The files in this directory are the final project-region output after project-only disconnected-region placement and final external-node overlap resolution.

## Positional forest

The routed forest has six roots and twelve links:

| Root | Immediate children | Semantic parents |
|---|---|---:|
| `TestContext` | `ProjectLayout`, `NodeLayout`, `LinkLayout` | 0 |
| `StubDeterministicExporter` | `DrawioGenerationResult` | 0 |
| `FakeAnalysisProcessingService` | `DiagramModel` | 0 |
| `TurnContext` | `CorridorObservation`, `CorridorLaneAllocation` | 0 |
| `FixtureData` (`type_23695f409c5ddd8c`) | `PlacedGraph`, `GeneratedLogicalRoutes` | 0 |
| `FixtureData` (`type_65b16d407aa8e5fe`) | `PlacedGraph`, `GeneratedLogicalRoutes`, `DiagramSettings` | 0 |

`PlacedGraph` and `GeneratedLogicalRoutes` are the two multi-parent semantic targets. Positional parents remain deterministic and each child group is contiguous. Parent centring was already exact in the baseline and was not changed.

Nine nodes have no incident semantic link: `EdgeTraversalCompilerTests`, `GlobalCorridorPathSelectorTests`, `InterLayerDemandDiscoveryTests`, `ProjectOwnershipBoundsCompilerTests`, `RegionalCorridorPathOptimizerTests`, `ControlledFileSystem`, `FakeRenderingProcessingService`, `PathFixture`, and `StubRenderer`.

## Before and after

| Metric | Before | After |
|---|---:|---:|
| Project width | 5362 | 3670 |
| Project height | 300 | 660 |
| Aspect ratio | 17.87 | 5.56 |
| Visual rows | 2 | 5 |
| Maximum nodes in a row | 19 | 10 |
| Disconnected grid | interleaved/right-appended | centred 3 x 3 below forest |
| Same-row node overlaps | 7 | 0 |
| Maximum positive row gap | 304 | 268 |
| Total natural node widths | 9268 | 9268 |
| Total logical link length | 2912 | 2912 |
| Maximum logical link length | 508 | 508 |
| Total bends | 22 | 22 |
| Waypoints | 22 | 22 |
| Hard logical/physical findings | 0 / 0 | 0 / 0 |

The 1692 px width reduction is placement-owned. Routing geometry length and bend count are unchanged. The added 360 px height is disconnected-region ownership: three natural-height rows separated by configured vertical spacing. No unowned overlap or gap remains. The largest remaining horizontal gap belongs to centred parent/subtree envelopes, not route clearance.

## Alternatives considered

1. Keep one broad row and only reorder siblings: rejected because nine disconnected nodes still consume primary-row width.
2. Keep routed trees contiguous and move disconnected nodes to a dedicated grid: selected; it preserves every routed subtree and link while removing interleaving.
3. Wrap all roots and routed nodes: rejected because it changes routed hierarchy and creates additional routing pressure without evidence that the routed forest itself is too wide.
4. Order roots by subtree width: no material benefit; current groups are already contiguous and centred.

The explicit placement policy is `MixedWithDisconnected`; wholly disconnected projects use `DisconnectedOnly`. Both use `ceil(sqrt(N))` columns, natural node widths, configured spacing, stable node-ID ordering, and centred incomplete final rows.
