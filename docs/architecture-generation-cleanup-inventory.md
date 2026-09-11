# Architecture generation cleanup inventory

Status: pre-cleanup review at `cac54a5`. This inventory is the decision record for the architecture-normalisation tranche. It does not describe a geometry change.

## Authority summary

The supported typed Architecture path is:

`ArchitectureGenerationService` -> `ArchitectureTopologyProjector` -> `DrawioArchitectureRenderer` -> `DeterministicDrawioExporter.GenerateArchitectureProjectRegionResult` -> `RenderLayout.BuildProjectRegion` -> project placement -> topology family selection -> terminal allocation -> project inter-layer slot/column compilation -> normalization -> validation -> coordinate ownership -> Draw.io serialization.

`DrawioDiagramRenderer`, `IDeterministicDrawioExporter.GenerateResult`, `RenderLayout.Build`, and `LegacyRoutingPipeline` form the compatibility path for the legacy `DiagramModel`. Corridor observation, allocation, traversal, candidate selection, regional selection, and repair are reachable only from that compatibility path or direct legacy tests.

## Source inventory

| Path/component | Category | Production reachable? | Typed Architecture? | Legacy/compatibility? | Tests? | Evidence only? | Current architecture? | Recommendation | Reason |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| `Services/Orchestrations/Diagrams/ArchitectureGenerationService.cs` | orchestration | Yes | Yes | No | Yes | No | Yes | Keep | Canonical typed entry point for CLI and VSIX jobs. |
| `Services/Processings/Architectures/ArchitectureTopologyProjector.cs` | topology projection | Yes | Yes | No | Yes | No | Yes | Keep | Converts semantic Architecture to render instances and applies duplication policy. |
| `Services/Foundations/Renderers/DrawioArchitectureRenderer.cs` | renderer | Yes | Yes | No | Yes | No | Yes | Keep | Canonical production Architecture renderer. |
| `Services/Foundations/Renderers/DrawioDiagramRenderer.cs` | renderer | Yes through legacy registry | No | Yes | Yes | No | No | Move to legacy/isolate | Public `DiagramModel` compatibility renderer; already documented compatibility-only. |
| `Services/Foundations/Renderers/IDiagramRenderer*.cs`, `DiagramRendererRegistry.cs` | legacy renderer registry | Yes through legacy orchestration | No | Yes | Yes | No | No | Move to legacy/isolate | Must not be confused with typed Architecture renderer resolution. |
| `Services/Foundations/Drawios/IDeterministicDrawioExporter.cs` | compatibility API | Yes | No | Yes | Yes | No | No | Retain compatibility, move to legacy namespace later | Public legacy contract still consumed by legacy renderer/orchestration tests. |
| `DeterministicDrawioExporter.GenerateArchitectureProjectRegionResult(ArchitectureRenderGraph, ...)` | typed exporter entry | Yes | Yes | No | Yes | No | Yes | Keep and rename internally when compatibility split permits | Typed renderer's only exporter entry. |
| `GenerateArchitectureProjectRegionResult(DiagramModel, ...)` | adapter entry | Tests/direct callers | Indirect adapter only | Yes | Yes | No | No | Isolate | Compatibility adapter into the project-region path. |
| `GenerateProjectRegion(...)` overloads | project-region exporter | Yes | Yes | Some overloads | Yes | No | Yes | Keep typed core; isolate legacy overloads | Private typed implementation owns validation, ownership and serialization. |
| `GenerateResult`, `Generate`, `ExportDiagnostic`, `ValidateStrict` | old exporter entries | Yes through legacy renderer | No | Yes | Yes | No | No | Move to legacy facade | Supported only for the old `DiagramModel` contract. |
| `DeterministicDrawioExporter.RenderGraph.cs` | render adapter/duplication compatibility | Yes | Yes via adapter | Yes | Yes | No | Mixed | Split later, keep now | Typed render graph is adapted into the historical internal `RenderGraph`; duplication rules remain durable. |
| `RenderLayout.BuildProjectRegion` | production layout/routing | Yes | Yes | No | Yes | No | Yes | Keep; extract from legacy partial | Canonical project-region production authority. |
| `RenderLayout.Build` and older private candidate methods | legacy layout/routing | Yes through compatibility exporter | No | Yes | Yes | No | No | Move to legacy type, then evaluate deletion | Invokes `LegacyRoutingPipeline`; co-location creates false authority. |
| `LegacyRoutingPipeline.cs` | legacy routing | Compatibility only | No | Yes | Yes | No | No | Move to `Legacy/` and namespace | Explicit old corridor routing entry. |
| `CorridorObserver.cs` | legacy routing | Compatibility only | No | Yes | Direct tests | No | No | Move to legacy or delete with compatibility path | Observes legacy spatial corridor graph. |
| `CorridorLaneAllocator.cs` | legacy routing | Compatibility only | No | Yes | Direct tests | No | No | Move to legacy or delete with compatibility path | Allocates legacy corridor lanes. |
| `CorridorLaneGeometryCompiler.cs` | legacy routing | Compatibility only | No | Yes | Direct tests | No | No | Move to legacy or delete with compatibility path | Rewrites legacy geometry from lane allocation. |
| `CorridorPathCandidateReducer.cs` | legacy candidate selection | Compatibility only | No | Yes | Direct tests | No | No | Move/delete after compatibility decision | Called by old candidate generation only. |
| `GlobalCorridorPathSelector.cs` | legacy global optimizer | Compatibility only | No | Yes | Direct tests | No | No | Move/delete after compatibility decision | Not reached by `BuildProjectRegion`. |
| `RegionalCorridorPathOptimizer.cs` | legacy regional optimizer | Compatibility only | No | Yes | Direct tests | No | No | Move/delete after compatibility decision | Not reached by typed generation. |
| `RouteRepairCoordinator.cs` | legacy repair | Compatibility only | No | Yes | Direct tests | No | No | Move/delete after compatibility decision | Called from `LegacyRoutingPipeline`, not typed production. |
| `EdgeTraversalCompiler.cs`, `LinkConnectionPathCompatibility.cs` | legacy traversal | Compatibility only | No | Yes | Direct tests | No | No | Move with legacy routing | Corridor traversal compilation is absent from typed production. |
| `Models/Drawios/CorridorModels.cs`, `CorridorPathSelectionModels.cs`, `RegionalPathSelectionModels.cs`, legacy portions of `EdgeTraversalModels.cs` | legacy models | Compatibility only | Empty values currently leak into typed result | Yes | Yes | No | No | Move to legacy and remove from canonical result | Historical model vocabulary. |
| `CanonicalTopologyFamilySelector.cs` | topology selection | Yes | Yes | No | Yes | No | Yes | Keep | Current topology-family authority. |
| `ProjectTerminalAllocator.cs` and connection demand calculators | terminal allocation | Yes | Yes | Some reusable legacy consumers | Yes | No | Yes | Keep | Current attachment authority; terminology is current. |
| `ProjectInterLayerSlotCompiler.cs` | horizontal/vertical routing | Yes | Yes | No | Yes | No | Yes | Keep | Current production route compilation authority. Future root routing belongs adjacent to this component. |
| `DeterministicSlotAllocator.cs` | horizontal slot allocation | Yes | Yes | Reusable old tests | Yes | No | Yes | Keep | Current deterministic slot primitive. |
| `VerticalLinkColumnAllocator.cs`, `ReturnColumnAllocator.cs` | column allocation | Yes | Yes | Some legacy helpers | Yes | No | Yes | Keep | Current vertical and return-column primitives. |
| `ProjectLayerBandPlacement.cs`, `ProjectRegionPlacement.cs`, `Layout/PlacementPipeline.cs` | placement | Yes | Yes | Some compatibility consumers | Yes | No | Yes | Keep; rename active corridor wording | Project-local placement and physical project positioning. |
| `CoordinateOwnershipCompiler.cs`, `ProjectOwnershipBoundsCompiler.cs`, `ProjectPhysicalGeometryValidator.cs` | ownership/validation | Yes | Yes | No | Yes | No | Yes | Keep | Current logical-to-physical segmentation and reconstruction authority. |
| `LogicalRouteNormalizer.cs`, `TraceabilityValidator.cs`, `NodeOverlapValidator.cs`, `OrthogonalGeometry.cs` | validation/geometry primitives | Yes | Yes | Shared | Yes | No | Yes | Keep | Durable current rules. |
| `ArchitectureGeometryAnalyser.cs` | evidence analyser | CLI diagnostics | Yes | No | Yes | Yes | Yes | Merge into supported evidence model | Reusable but currently accompanied by overlapping outputs. |
| `DrawioDiagnosticReportBuilder.cs` | legacy diagnostic serializer | Compatibility path | No | Yes | Yes | Yes | No | Move with legacy diagnostics | Accepts `DrawioGenerationResult` from old exporter. |
| `GenerationPerformanceSession` and telemetry models | performance evidence | CLI optional | Yes | Shared | Yes | Yes | Yes | Keep and integrate into evidence model | Durable performance section. |
| `DeterministicDrawioExporter.DiagramFileBuilder.cs`, `DrawioDocumentComposer.cs` | serialization | Yes | Yes | Shared | Yes | No | Yes | Keep | Current Draw.io serialization authority. |

## Dependency injection and entry-point inventory

| Path/component | Category | Typed production | Compatibility | Recommendation | Reason |
|---|---|---:|---:|---|---|
| `IServiceCollectionExtensions.FoundationServices.cs` typed registrations | DI | Yes | No | Keep and split visibly | `IArchitectureDiagnosticRenderer` resolves `DrawioArchitectureRenderer`. |
| Same file's `IDiagramRenderer` registrations | DI | No | Yes | Move into named legacy-registration method | Registry resolution looks equivalent but is not used by typed Architecture. |
| `TypedDiagramGenerationOrchestrator` | typed multi-page orchestration | Yes | No | Keep | Used for typed Data Model jobs and typed request orchestration. |
| `DiagramGenerationOrchestrationService`, processing services, exposure/coordination services | legacy orchestration | No | Yes | Isolate and retain while public compatibility is supported | Continue to serve `DiagramModel`/renderer-registry callers. |
| CLI Architecture branch in `Program.cs` | executable entry | Yes | No | Keep | Resolves `IArchitectureGenerationService`. |
| VSIX `DiagramCommands` Architecture generation | executable entry | Yes | No | Keep and add guard test | Resolves typed Architecture generation service through shared registration. |

## Test inventory

| Paths | Category | Production code? | Legacy code? | Recommendation | Reason |
|---|---|---:|---:|---|---|
| `ArchitectureAnalyserTests`, `InterfaceResolutionTests`, `SemanticScopeSelectorTests` | production analysis | Yes | No | Keep under Production Architecture | Durable semantic rules. |
| `ArchitectureTopologyProjectorTests`, `RenderGraphDuplicationTests`, `NodeDuplicationExporterTests` | production projection | Yes | Some adapter coverage | Merge naming around topology projection | Durable duplication/canonical placement rules; remove exporter-era duplicate assertions. |
| `ArchitectureGenerationServiceTests`, `DrawioArchitectureRendererTests`, `ProjectRegionRendererTests`, `FullPipelineRepeatTests` | production pipeline | Yes | Some direct exporter calls | Keep, migrate direct calls to typed renderer/service | Authoritative end-to-end and determinism coverage. |
| `ProjectRegionPlacementTests`, `PlacementPipelineTests`, `LayeredLayoutRegressionTests`, `ProjectLabelGeometryMeasurerTests` | production placement | Yes | Shared | Keep under Unit placement | Durable layout invariants. |
| `CanonicalTopologyFamilySelectorTests`, `ProjectTerminalAllocatorTests`, `ProjectInterLayerSlotCompilerTests`, `DeterministicSlotAllocatorTests`, `VerticalLinkColumnAllocatorTests` | production routing | Yes | No/shared primitives | Keep under Unit routing | Current topology, terminal, slot and column rules. |
| `CoordinateOwnership*Tests`, `ProjectOwnershipBoundsCompilerTests` | production ownership | Yes | No | Keep under Production ownership/regressions | Durable Draw.io movement and reconstruction contracts. |
| `CorridorObserverTests`, `CorridorLaneAllocatorTests`, `CorridorLaneGeometryCompilerTests`, `CorridorModelsTests` | legacy routing | No | Yes | Move to Legacy Compatibility or delete with implementation | Cannot be cited as production evidence. |
| `GlobalCorridorPathSelectorTests`, `RegionalCorridorPathOptimizerTests`, `RouteRepairCoordinatorTests`, corridor-focused `EdgeTraversalCompilerTests` | legacy optimization/repair | No | Yes | Move/delete with implementation | Chronological legacy tranche coverage. |
| `CanonicalSharedNodeCorridorPipelineTests`, `CanonicalSharedNodeRouteCandidateBuilderTests` | mixed legacy regression | No current routing authority | Yes | Review and retain only topology/duplication rules in typed fixtures | Names and implementation couple durable behavior to dead routing. |
| `AdjacentDownward*`, `CommonAuthorityFoundationTests`, `ConsolidatedRoutingFoundationTests`, `GeneralDownward*`, `InterLayerDemandDiscoveryTests`, `ReturnLinkCommonAllocatorTests`, movement/constraint tranche tests | superseded foundation experiments | Mostly no | Yes/experimental | Remove when call search proves no typed consumer | Tests preserve migration chronology rather than current product rules. |
| `DeterministicDrawioExporterTests`, registry/orchestration/exposure compatibility tests | compatibility contract | No | Yes | Retain in explicit Legacy Compatibility group | Public compatibility still exists. |
| `DrawioRoutingAuthorityFixtureTests` and checked-in small `.drawio` fixtures | serialization regression | Yes | Shared | Keep under Regression fixtures | Small intentional fixtures with durable save/reopen behavior. |

## Documentation and evidence inventory

| Path | Category | Current? | Recommendation | Reason |
|---|---|---:|---|---|
| `docs/routing-architecture.md` | routing architecture | No | Merge then delete | Presents corridor observation/allocation as production. |
| `docs/routing-terminology-and-deletion-inventory.md` | migration inventory | Partially | Merge disposition into this inventory, then archive/delete | Contains useful history but contradictory production language. |
| `docs/simplified-layer-band-routing-plan.md` | migration plan | Partially | Merge current decisions, then archive/delete | Plan is not an authority document. |
| `docs/layout-placement-architecture.md` | placement architecture | Mostly | Merge current material into `architecture-generation.md` | Avoid a second competing pipeline document. |
| `docs/settings-schema-migration.md` | compatibility documentation | Yes | Keep | Durable versioned settings contract, not routing architecture. |
| top-level audit/review/stage/tranche markdown files | historical investigations | No | Delete or archive only genuinely enduring decisions | Current-sounding filenames create authority ambiguity. |
| `docs/evidence/**/README.md`, review/completion reports | tranche evidence | No | Delete after inventory | Generated/investigation narrative belongs outside tracked docs. |
| `docs/evidence/**/*.drawio`, JSON, CSV, settings | generated/tranche evidence | No | Remove from Git; regenerate under ignored `artifacts/` if needed | Large stale evidence duplicates supported diagnostics. |
| `fixtures/project-region/**/manifest.json` | small deterministic fixtures | Yes | Keep | Intentional input fixtures, not generated output. |
| `tests/**/Fixtures/*.drawio` and results markdown | small regression fixtures | Yes | Keep | Checked-in serialization/manual-behavior contracts. |

## Script and generated-artifact inventory

| Path | Category | Production? | Reusable? | Recommendation | Reason |
|---|---|---:|---:|---|---|
| `scripts/benchmark-generation.ps1` | performance utility | No | Yes | Keep and point output at `artifacts/current/performance` | General benchmark workflow. |
| `scripts/compare-drawio-validation-diagnostics.mjs` | diagnostic comparison | No | Potentially | Merge capability into supported evidence comparison or keep as sole reusable comparison | Only broadly reusable diagnostic script. |
| `scripts/report-adjacent-downward-observation.ps1` | tranche report | No | No | Delete | Stage-specific evidence generator. |
| `scripts/report-common-rail-assignment-evidence.ps1` | tranche report | No | No | Delete | Superseded rail terminology/experiment. |
| `scripts/report-contact-policy-evidence.ps1` | tranche report | No | No | Delete | Useful rule now belongs in tests/evidence schema. |
| `scripts/report-remaining-authority-boundaries.ps1` | tranche report | No | No | Delete | Historical authority investigation. |
| `scripts/report-routing-consolidation-evidence.ps1` | tranche report | No | No | Delete | Superseded consolidation evidence. |
| `scripts/report-stage-b-diagonals.ps1` | tranche report | No | No | Delete | Stage-specific diagnostic. |
| repository-root `artifacts/` | ignored generated output | No | Clearable | Keep ignored, normalize to `current/`, `baselines/`, `investigations/` | Correct location but accumulated structure is inconsistent. |
| `artifacts/correction-tranche/diagnose-geometry.js` and copied variants | one-off reconciliation | No | No | Delete during artifact cleanup | Investigation schema should not become supported tooling. |
| settings under ignored `artifacts/**/settings` | generated investigation inputs | No | Sometimes | Move durable examples to `fixtures/settings`; delete the rest | Settings required for reproducible tests belong in fixtures. |

## Cleanup constraints and deletion proof

1. Typed generation must reach `ProjectRegionLayoutBuilder` and never call `RenderLayout.Build`, `LegacyRoutingPipeline`, a `Corridor*` allocator/selector, or `RouteRepairCoordinator`.
2. Compatibility APIs remain supported until repository consumers and tests are migrated or explicitly retained under Legacy Compatibility.
3. Geometry equality is assessed against the committed Phase 3 artifacts before and after each structural change.
4. Small checked-in fixtures remain; generated evidence under `docs/evidence` does not.
5. Root/cross-project routing remains a documented production limitation and is not corrected in this tranche.
