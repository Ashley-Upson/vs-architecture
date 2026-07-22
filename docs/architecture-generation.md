# Architecture diagram generation

This is the authoritative description of the current Architecture diagram pipeline. Historical routing experiments and tranche reports are not production specifications.

## Production authority

Both the CLI and Visual Studio extension call the typed generation services. The production flow is:

```text
Roslyn analysis
-> ArchitectureDiagram
-> ArchitectureTopologyProjector
-> ArchitectureRenderGraph
-> DrawioArchitectureRenderer
-> project-region placement
-> topology-family selection
-> terminal allocation
-> horizontal-slot and vertical-column compilation
-> route normalization and validation
-> coordinate-ownership compilation
-> Draw.io serialization
```

`DrawioArchitectureRenderer` and `ProjectRegionLayoutBuilder` are the production rendering authority. `DrawioDiagramRenderer`, the `IDiagramRenderer` registry, the old `RenderLayout.Build` path, and the corridor candidate/observer/allocator pipeline exist only for supported legacy compatibility.

## Semantic analysis and interface resolution

Roslyn analysis produces an `ArchitectureDiagram` containing projects, semantic nodes, dependencies, registrations, and selection metadata. DI registrations resolve interface dependencies to concrete implementations where the resolution is unique. Unresolved and multiply resolved registrations remain diagnosable rather than being silently rewritten.

## Topology projection and duplication

`ArchitectureTopologyProjector` converts semantic identities into render identities. With duplication enabled, a semantic node may have a physical render instance under each relevant branch. With duplication disabled, the first deterministic occurrence is canonical and later relationships route to that existing render node. Exception-authorised duplication is recorded separately. Projection ordering and IDs must not depend on source collection enumeration.

## Project-local coordinate frames and placement

Nodes are assigned project-local layers and depths, then each project region receives a global placement offset. Equal local depths in different projects express the same local hierarchy only; they do not share a physical routing band. Project containers include their owned vertices and owned route geometry. Placement reserves vertical link aisles where required and prevents project-container overlap.

## Current routing vocabulary

- **Layer**: project-local node rank.
- **Band**: the project-owned vertical interval between layers.
- **Horizontal slot**: an allocated Y coordinate for a horizontal route segment in a band.
- **Vertical column**: an allocated X coordinate for a vertical route segment.
- **Return column**: a vertical column used by a return or same-layer topology.
- **Terminal**: the node attachment and its fixed protected stem.
- **Transition**: geometry between a terminal stem and an allocated slot or column.
- **Reserved aisle**: placement space held for vertical link geometry.
- **Project-local coordinate frame**: coordinates owned by a movable project container.
- **Root/inter-project coordinate space**: global coordinates not owned by one project.

“Corridor” is not a production routing abstraction. Types retaining that word belong to the compatibility pipeline and must not be cited as typed production evidence.

## Topology families and allocation

The canonical selector classifies supported relationships into adjacent downward, general downward, upward return, same-layer return, and explicitly diagnosed fallback families. Terminal allocation preserves bottom-edge source exits and top-edge target entries where the family requires them. Same-side fan-out is ordered monotonically.

Project-local routes are compiled by `ProjectInterLayerSlotCompiler`. Horizontal candidates are scoped by project and layout revision, checked over their actual X span against owned node geometry and protected spacing, and allocated deterministically. Vertical and return columns are allocated separately with stable route identities and spacing constraints. Normalization removes redundant geometry without changing ownership or obstacle-clearance semantics.

## Cross-project status

An explicit root/inter-project routing authority is not yet implemented. Cross-project, internal-to-external, and external-to-internal links are separated from ordinary project-local demand, but their root transitions can still cross unrelated project regions or create long stems. This is the next known production routing boundary. It must be corrected in the typed slot/column compilation path, not in the legacy corridor pipeline.

## Coordinate ownership and Draw.io serialization

A validated logical route is authoritative. The ownership compiler splits it into physical Draw.io edge cells when its coordinate frame changes:

```text
project-owned source section -> root-owned section -> project-owned target section
```

Invisible deterministic boundary anchors join the cells. Reconstructing the physical segments in absolute coordinates must reproduce the logical route exactly. Only the first segment carries a source marker and only the last carries the target arrow. Project-owned nodes, external diamonds, anchors, and edge sections use project-relative geometry and move with the container.

The accepted manual-editing limitation is that diagrams.net selects or deletes one physical segment at a time; metadata (`logicalEdgeId`, semantic endpoints, segment index, role, and owner) permits logical reconstruction but does not make multi-parent cells one native selection.

## Validation, evidence, and determinism

Logical validation runs before ownership compilation; physical validation checks the serialized representation. Findings remain diagnostic unless input cannot be loaded or generation throws. Clean perpendicular crossings are not strict failures. Geometry findings do not suppress diagram output.

Supported generated evidence belongs under `artifacts/`:

```text
artifacts/current/         latest local evaluation
artifacts/baselines/       explicitly preserved comparison baselines
artifacts/investigations/  defect-specific evidence
```

The Architecture evidence document groups provenance and normalized settings, semantic analysis, topology projection, project placement, terminal allocation, horizontal slots, vertical/return columns, logical routes, physical ownership, validation, determinism hashes, and performance. Checked-in fixtures stay under the test tree and are never removed by artifact cleanup.

## Legacy compatibility boundary

The legacy renderer/exporter contracts remain because repository consumers and compatibility tests still exercise them. Their `LegacyRoutingPipeline`, corridor observations, candidate reduction, lane allocation, route repair, and global/regional selectors are isolated compatibility implementation. New Architecture work must not resolve the legacy renderer registry or add dependencies on legacy result fields.

Future removal requires proving there are no supported consumers of the public compatibility APIs. Until then, legacy tests verify only compatibility behavior and are not evidence for the typed production pipeline.

## Future routing defect workflow

Preserve the exact diagram and settings, identify the involved logical routes, reduce the topology to a deterministic fixture, classify the failure against the current production stages, add a test at the earliest failing transformation, and make the smallest correction. Rebuild the CLI before using `--no-build`, regenerate the real project, and verify that higher-priority traceability and deterministic-output rules remain intact.
