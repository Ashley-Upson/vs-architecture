# Architecture production-generation evaluation

This evidence was generated on the `feature/decuplicate-node-option` branch after production rendering was moved to the canonical project-region authority. Every real-project run used the typed production path once and reused its `TypedArchitectureGenerationResult` for `.drawio`, diagnostics, and geometry analysis.

## Active production path

1. `RoslynDependencyAnalyzer` implements `IArchitectureAnalyser` and produces the semantic `ArchitectureDiagram` and selection report.
2. `ArchitectureGenerationService` invokes `DrawioArchitectureRenderer` through `IArchitectureRenderer<DrawioPage>`.
3. `DrawioArchitectureRenderer.RenderWithDiagnostics` adapts the typed model and calls `DeterministicDrawioExporter.GenerateArchitectureProjectRegionResult` in both production and development modes.
4. `PlacementPipeline.Place` constructs the positional forest, assigns depths, sizes nodes, and places the initial graph.
5. `ProjectLayerBandPlacement.Align` establishes layer bands.
6. `ProjectTerminalAllocator.Allocate` assigns bottom exits and top entries, including opposing terminal separation on a shared InterLayer boundary.
7. `CanonicalTopologyFamilySelector.Select` selects AdjacentDownward, LongDownward, return, and boundary families.
8. `ProjectInterLayerSlotCompiler.Compile` discovers InterLayer demand, calls `DeterministicSlotAllocator` and `VerticalLinkColumnAllocator`, and materialises immutable logical routes.
9. `LogicalRouteNormalizer.Normalize` normalises without changing the allocated authority.
10. `TraceabilityValidator.Validate` performs logical hard validation.
11. `CoordinateOwnershipCompiler.Compile` produces project/root-owned Draw.io segments and anchors.
12. `ProjectPhysicalGeometryValidator.Validate` reconstructs absolute routes and performs physical validation. Project bounds and label geometry are then recalculated.
13. `DiagramFileBuilder` creates the `DrawioPage`; `DrawioDocumentComposer.Compose` creates the `.drawio` document.
14. `ArchitectureGeometryAnalyser.Analyse` consumes the same typed result and graph model. It does not rerun Roslyn analysis or rendering.

The CLI and VSIX use `IArchitectureGenerationService`/typed orchestration. Legacy exporter entry points remain for compatibility and older focused tests, but normal typed Architecture generation no longer dispatches to them. Data Model generation was not changed.

## Reproducible command

```powershell
dotnet build src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release

dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- `
  <project-or-solution> `
  --settings <settings.json> `
  --renderer drawio `
  --diagram-types architecture `
  --output <evidence-directory>\architecture.drawio `
  --architecture-analysis-output <evidence-directory> `
  --diagnostics-output <evidence-directory>\diagnostics.json `
  --serialization-repeat 2
```

The analysis directory contains `architecture-analysis.json`, `architecture-analysis.md`, logical and physical findings, placement analysis, and routing analysis. Diagnostics additionally contain logical point arrays, topology counts, slot demand/assignment authority, and vertical column assignments.

## Final matrix

| Input | Semantic nodes/links | Render nodes/routes | Hard | Page | Route length | Bends |
|---|---:|---:|---:|---:|---:|---:|
| Synthetic fixture | 12 / 15 | 12 / 15 | 0 | 1166 x 1100 | 6,601 | 32 |
| StandardIo extract | 9 / 11 | 9 / 11 | 0 | 2206 x 753 | 9,822 | 30 |
| StandardIo CLI | 2 / 0 | 2 / 0 | 0 | 588 x 194 | 0 | 0 |
| StandardIo VSIX | 14 / 3 | 14 / 3 | 0 | 964 x 834 | 496 | 4 |
| StandardIo Core.Tests | 102 / 15 | 106 / 15 | 0 | 8030 x 1794 | 5,494 | 24 |
| StandardIo Core configured | 21 / 24 | 20 / 24 | 0 | 5890 x 1226 | 31,703 | 58 |
| StandardIo Core full | 392 / 437 | 469 / 436 | 0 | 113600 x 6430 | 4,362,644 | 908 |
| cCoder TemplateController | 31 / 45 | 32 / 44 | 0 | 5590 x 1386 | 52,708 | 92 |
| cCoder SubmissionController | 13 / 13 | 13 / 13 | 0 | 1661 x 994 | 4,014 | 24 |
| cCoder ContentController | 14 / 15 | 14 / 15 | 0 | 2071 x 994 | 5,301 | 28 |
| cCoder five-controller region | 116 / 196 | 121 / 194 | 0 after correction | 17732 x 2854 | 904,863 | 458 |
| cCoder full project | 393 / 340 | 315 / 337 | 0 | 42439 x 4310 | 1,632,878 | 634 |
| StandardIo solution | 499 / 455 | 579 / 454 | 14 | 115988 x 7118 | 4,496,748 | 930 |

The largest hard-green result is StandardIo Core: 469 rendered nodes and 436 routes. Its recorded pipeline timing was 3,086 ms, including 1,379 ms project-region generation and 755 ms logical/physical validation. The cCoder full-project run was 1,898 ms total.

The solution-level result is intentionally retained as failing evidence. Its current 14 findings include six overlapping project-container pairs; the overlapping regions also produce project-label, route-node, and derived logical/physical findings. Its nodes are placed as one global positional region and project bounds are derived afterwards, allowing different project containers to overlap. This is owned by multi-project placement; route nudging or validation weakening is not an acceptable correction.

## Defects found and corrected

### Production dispatched to the legacy renderer

Normal typed Architecture generation produced 18 hard findings for the accepted cCoder TemplateController input, while development project-region generation produced zero. Production now calls the same canonical authority. The resulting production `.drawio` was byte-identical to the accepted canonical artifact before subsequent terminal corrections.

### Destination column occupied another route's fixed target entry

StandardIo Core contained a 300 px shared segment into `PipelineStageMetric`. `ProjectInterLayerSlotCompiler.FixedColumnExclusions` omitted fixed arrival intervals belonging to other destination/return-column routes. Adding those fixed intervals reduced Core hard findings from 10 to 7 without changing the cCoder accepted baseline.

### Endpoint precedence was lost during lane reuse

Two routes from `GeneralDownwardLinkAssignment` shared 132 px. The required departure-before-arrival edge existed and was acyclic, but `DeterministicSlotAllocator` used precedence only for processing order; a later demand could reuse a lower-numbered lane. Enforcing predecessor slot minima reduced Core findings from 7 to 4 without changing page dimensions, route length, or bends.

### Opposing fixed terminal stems were too close

The remaining Core findings were fixed top-entry and bottom-exit stems separated by 11 px and 2 px. Boundary-aware terminal allocation moved the affected target attachments by only 1 px and 10 px. Core became hard-green with unchanged dimensions and bend count. Generalising the same rule to return arrivals removed 36 px and 360 px shared segments from the five-controller cCoder region.

## Validator coverage

`TraceabilityValidator` remains the canonical logical check for node collision, shared segments, parallel spacing, reused bends, reversals, and perpendicular contacts. `ProjectPhysicalGeometryValidator` reconstructs ownership-segmented routes, verifies exact logical geometry, repeats traceability checks, and checks project-label intersections. `NodeOverlapValidator` is the canonical placement gate.

`ArchitectureGeometryAnalyser` independently checks structured generated nodes/routes for node overlap, invalid dimensions, containment, link-node intersection, shared segments, diagonals, zero-length segments, deterministic hashes, route length, detour, bends, terminal stubs, page bounds, and project-container overlap. It also incorporates enforced typed logical/physical findings instead of replacing the canonical validators.

Every selected semantic link is reconciled from the typed diagram to physical route metadata. The evaluated inputs contain no `Unsupported` links: differences between semantic and rendered counts are explicitly classified as interface-endpoint omissions (one in StandardIo Core/solution, one in TemplateController, two in the five-controller region, and three in the cCoder full project).

Current evidence-only gaps include comprehensive compactness attribution, authorised boundary-transition verification, slot/column mismatch classification, clean-crossing counts, and project-label geometry as a first-class analyser model. These remain diagnostic work; validation was not weakened.

## Settings

- `ccoder-submission-region.settings.json`
- `ccoder-content-region.settings.json`
- `ccoder-controller-region.settings.json`
- Existing `../semantic-scope/ccoder-template-region.settings.json`
- Existing `../semantic-scope/full-input-scope.settings.json`

No VSIX was packaged for this tranche.
