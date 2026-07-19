# Retained legacy routing boundaries

Normal production remains byte-authoritative and continues to enter the retained implementation through `LegacyRoutingPipeline`. The following subsystems are therefore retained deliberately, not treated as common architecture:

| Boundary | Active production responsibility | Common replacement/deletion gate |
|---|---|---|
| Corridor observation and lane allocation | Observe accepted link paths, separate parallel geometry, compile shifted coordinates | Delete after common segment/slot/column authority receives production approval. |
| Corridor path selectors and regional optimiser | Bounded candidate comparison for normal production | Delete after the common link-path selector covers all production topologies and passes visual review. |
| Edge traversal projection | Projects legacy path segments into corridor traversal metadata | Delete after common path regeneration emits authoritative paths directly. |
| Junction and shared-turn legacy integration | Preserves accepted production junction geometry | Delete after common transition ownership covers supported junctions. |
| Route repair coordinator | Applies legacy heuristic correction under a bounded budget | Forbidden for common-owned links; delete when no normal-production link requires repair. |
| Canonical shared-node candidate builder | Supplies normal-production candidates for canonical targets | Delete after common positional hierarchy and topology producers own those links. |
| Ownership segmentation and project bounds | Serializes logical paths into coordinate-owned Draw.io cells | Shared serializer boundary; retained, not legacy routing. |

## Dependency rule

Common-authority producers depend only on canonical link, segment, slot, constraint, and validation models. They may read retained production output through observation/projection adapters for parity evidence, but must not invoke legacy repair, waypoint nudging, traversal fallback, corridor capacity mutation, or lane compilation for common-owned output.

## Structural-isolation decision

The execution boundary is explicit in `LegacyRoutingPipeline`. A physical namespace move is deferred because the retained files are tightly coupled to exporter partials and internal models; moving them without changing behaviour would create a large review surface. The deletion inventory records the production-approval gate for each retained subsystem. New common code must not be added behind the legacy pipeline except as an explicit adapter.
