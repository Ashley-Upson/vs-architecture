# Legacy routing compatibility

This folder contains the corridor observation, candidate selection, lane compilation, regional optimisation, and route-repair implementation used by the legacy exporter contract.

It is not reached by `DrawioArchitectureRenderer` or `ProjectRegionLayoutBuilder`. New Architecture generation work belongs in the typed project-region topology, terminal, horizontal-slot, vertical-column, normalization, ownership, and serialization stages.

Namespaces remain unchanged to preserve the compatibility surface. Removal requires proof that the public legacy renderer/exporter contracts no longer have supported consumers.
