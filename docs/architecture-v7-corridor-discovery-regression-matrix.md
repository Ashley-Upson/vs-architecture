# V7 Checkpoint A — capability-derived corridor discovery

This checkpoint adds analysis-only corridor discovery from the frozen common
grid. It does not assign routes, lanes, terminals, bends, crossings, physical
tracks, or renderer geometry.

| Invariant | Corridor discovery regression | Downstream preservation regression |
|---|---|---|
| Maximal horizontal/vertical straight intervals use production capability semantics | `Discovers_single_horizontal_and_vertical_corridors`; `Blocked_node_and_non_routing_separator_split_horizontal_and_vertical_corridors` | `Discovery_is_analysis_only_and_does_not_mutate_frozen_placement` |
| Empty node rows are vertical passthrough only | `Vertical_corridor_crosses_empty_node_rows_but_horizontal_does_not` | `Discovery_is_analysis_only_and_does_not_mutate_frozen_placement` |
| Straight-only boundary/header cells remain axis-straight only | `Straight_only_boundary_and_header_cells_are_discovered_only_on_straight_axis` | `Discovery_is_analysis_only_and_does_not_mutate_frozen_placement` |
| H/V intersection is legal and not allocated in this checkpoint | `Horizontal_and_vertical_corridors_intersect_without_conflict` | `Discovery_is_analysis_only_and_does_not_mutate_frozen_placement` |
| Disjoint intervals remain distinct | `Multiple_disjoint_corridors_on_one_row_and_column_remain_distinct` | `Discovery_is_analysis_only_and_does_not_mutate_frozen_placement` |
| Frozen-coordinate identities are enumeration-order independent | `Shuffled_grid_enumeration_has_the_same_coordinate_fingerprint` | `Discovery_is_analysis_only_and_does_not_mutate_frozen_placement` |

The full production preservation gate compares the logical-route,
allocation, physical-scene, renderer, and fidelity fingerprints for a run
with discovery evidence enabled against the existing production run. No
downstream stage consumes `ArchitectureV7CorridorDiscoveryResult`.
