# Canonical project-region evidence

This directory contains whole-project output from the independent canonical project-region renderer. The renderer is available only through the explicit `--development-project-region` CLI option. Normal production remains on the legacy renderer and does not execute project-region planning or diagnostics.

## Reproduction

```powershell
dotnet build src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release

dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- . --diagram-manifest fixtures\project-region\synthetic\manifest.json --settings artifacts\node-duplication\real-project-deduplicated-settings.json --renderer drawio --development-project-region docs\evidence\project-region\synthetic

dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- . --diagram-manifest fixtures\project-region\standardio-extract\manifest.json --settings artifacts\node-duplication\real-project-deduplicated-settings.json --renderer drawio --development-project-region docs\evidence\project-region\standardio-extract
```

Each run writes the legacy visual reference, canonical `common-after.drawio`, combined and split logical/physical invariant reports, and an authority trace. Acceptance is whole-project: an ineligible project falls back outside the renderer; common and legacy routes are never mixed inside it.

## Final results

| Fixture | Nodes | Links | InterLayers | Slot demands | Destination columns | Return columns | Hard logical | Hard physical | Canvas extent | Waypoints |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Synthetic | 12 | 15 | 8 | 21 | 6 | 0 | 0 | 0 | 1359 x 1091 | 37 |
| StandardIo extract | 9 | 11 | 5 | 17 | 3 | 3 | 0 | 0 | 2326 x 744 | 32 |

Both fixtures are hard-green and byte-deterministic. The earlier project-region baselines had 8 hard findings for the synthetic fixture and 7 for the StandardIo extract. The canonical outputs contain no node or measured-label intersection, shared non-zero segment, spacing deficit, immediate reversal, ownership reconstruction failure, or serialization-created diagonal.

## Authority boundary

- Terminals: `ProjectTerminalAllocator`.
- Topology: `CanonicalTopologyFamilySelector`.
- Horizontal Y: `DeterministicSlotAllocator` over InterLayer demands.
- Vertical X and return side: `VerticalLinkColumnAllocator` plus ownership-local return selection.
- Constrained materialisation: `ProjectInterLayerSlotCompiler`.
- Normalization: `LogicalRouteNormalizer`.
- Physical geometry: `CoordinateOwnershipCompiler`.
- Acceptance: logical traceability followed by physical geometry validation.

The target path does not invoke generic candidate selection, corridor lane coordinate assignment, corridor lane geometry rewriting, traversal topology replacement, or topology-changing repair.

## Historical comparison

`legacy-before.drawio` remains a visual reference, not an authority input. The previous independent artifacts and their findings remain recoverable from Git history before this consolidation; the checked-in `common-after.drawio` files are now the canonical hard-green evidence.
