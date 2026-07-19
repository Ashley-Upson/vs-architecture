# Independent project-region evidence

This directory contains the first diagrams generated from semantic fixture manifests without constructing or adapting a legacy `RenderLayout`. The normal renderer remains unchanged and the new entry point is reachable only through the explicit `--development-project-region` option or the concrete exporter API.

## Commands

```powershell
dotnet build src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release

dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- . --diagram-manifest fixtures\project-region\synthetic\manifest.json --settings artifacts\node-duplication\real-project-deduplicated-settings.json --renderer drawio --development-project-region docs\evidence\project-region\synthetic

dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- . --diagram-manifest fixtures\project-region\standardio-extract\manifest.json --settings artifacts\node-duplication\real-project-deduplicated-settings.json --renderer drawio --development-project-region docs\evidence\project-region\standardio-extract
```

Each run writes `legacy-before.drawio`, `common-after.drawio`, and `invariants.json`. The legacy document is visual context only.

## Synthetic region

- Stable manifest: 1 project, 11 internal nodes, 1 external node, 15 semantic links.
- Families: adjacent and long downward, same-layer, upward, multi-parent canonical target, and internal-to-external boundary link.
- First independent output: 2 node collisions, 2 immediate reversals, and 1 spacing deficit (perpendicular crossings reported separately).
- Correction cycle 1: bounded canonical compilation/repair removed both node collisions.
- Correction cycle 2: one bounded lane-demand expansion reduced immediate reversals from 2 to 1 but left 3 shared-segment and 4 spacing findings.
- Legacy/common project dimensions: 1191x1142 / 1190x1138.
- Legacy/common physical waypoint counts: 55 / 53.
- Current disposition: whole-project fallback (`HardValidationFindings:8`). No internal route is selectively accepted.

## StandardIo extract

- Source: the renderer/analysis branch visible in the generated StandardIo.Core diagram.
- Stable manifest: 1 project, 9 real named types, 11 representative dependencies.
- Preserved legacy defect: shared route geometry around the processing/registry/exporter branch.
- First independent output: 2 shared segments and 4 immediate reversals.
- Correction cycle: canonical compilation/repair removed both shared segments; 3 spacing deficits remain.
- Legacy/common project dimensions: 2156x764 / 2156x750.
- Legacy/common physical waypoint counts: 37 / 38.
- Current disposition: whole-project fallback (`HardValidationFindings:7`, including four logical immediate-reversal findings which ownership serialization may normalize but are retained conservatively).

## Architectural disposition

The project renderer reuses permanent placement, topology candidate, corridor, traversal, repair, ownership, and serialization primitives. It bypasses:

- `LegacyRoutingPipeline` orchestration;
- legacy node/project coordinates;
- legacy selected paths and repairs;
- common reconstruction of legacy points;
- link-level component projection/classification;
- mixed common/legacy movement and rollback;
- component alternative search and revision-heavy coexistence.

The universal mixed-authority path remains development-only historical migration evidence. Its deletion gate is successful production approval of complete project-region fallback for all topology families it diagnoses.

## Known incomplete acceptance items

The output is intentionally checked in even though neither project is eligible yet. This prevents another architecture-only cycle and makes the remaining work concrete:

1. eliminate project-region spacing/shared-geometry findings without expanding the universal solver;
2. model rendered project-label text as an explicit measured obstacle while leaving unused title-bar space routable;
3. validate physical ownership-segment geometry separately from logical pre-serialization redundancy;
4. add a production selector only after one region passes every hard invariant.

No project-region output is enabled by default.
