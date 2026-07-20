# Typed diagram-pipeline separation evidence

This tranche separates Architecture and Data Model analysis/rendering and composes their independently rendered Draw.io pages. It does not add a code-path diagram type and does not package a VSIX.

## Active pipeline

```text
selected Roslyn projects
|- IArchitectureAnalyser -> ArchitectureDiagram -> IArchitectureRenderer<DrawioPage>
|- IDataModelAnalyser     -> DataModelDiagram     -> IDataModelRenderer<DrawioPage>
`- requested DrawioPage[] -> IDrawioDocumentComposer -> mxfile
```

The normal CLI and the existing VSIX-compatible generation exposure request Architecture and Data Model as two explicit jobs. `--diagram-types architecture`, `--diagram-types data-model`, and `--diagram-types architecture,data-model` select the CLI jobs. Page order is request order.

## Core semantic evidence

Source: `src/StandardIo.ArchitectureDiagram.Core/StandardIo.ArchitectureDiagram.Core.csproj`.

| Result | Current independent model | Historical embedded baseline |
|---|---:|---:|
| Data Model entities | 23 | 18 |
| Data Model properties | 151 | 116 |
| Data Model relationships | 22 | 5 |
| Collection relationships | 4 | not retained reliably |
| Nullable relationships | 1 | not retained |
| Architecture nodes | 318 | n/a |
| Architecture links | 398 | n/a |
| Architecture omitted nodes/links (full input) | 0 / 0 | n/a |

The Data Model increase is intentional. The analyser now builds the complete entity lookup before resolving property targets, retains collection element and nullable information, and includes independently eligible settings/domain types added by this tranche. It no longer inherits architecture roots or reachability. Relationship kinds remain honest property-reference or collection-property-reference evidence; no database cardinality, key, or ownership semantics are inferred.

Deterministic snapshot:

- `artifacts/typed-pipeline/core-data-model.json`
- SHA-256 `d6f40d6c07a436178d51b0d4e4984ac2c5b9ea801bad28795adea93f7b72202e`

Full-input architecture snapshot:

- `artifacts/typed-pipeline/core-architecture-full.json`
- SHA-256 `510ff0afac19529f838cc5c22c442b88db60d87919caad1fe09e0583fd58eb37`

## Independent rendering and composition evidence

Generated files:

- `artifacts/typed-pipeline/architecture-only.drawio`
- `artifacts/typed-pipeline/data-model-only.drawio`
- `artifacts/typed-pipeline/combined.drawio`

The Architecture graph-model SHA is identical in architecture-only and combined output:

`8786769b0d1565940bfe812706a2421391cc8cccf45fb20991ce8248c31a81b0`

The Data Model graph-model SHA is identical in data-model-only and combined output:

`595b09361fa788f2572da97d00213c72b94bc36765dc335824ff11beff7c3302`

The combined document SHA-256 is:

`5f1fcffdece4c512a057c08fbbc4458913c5a5f39b6402c4e9834a3829ad77ef`

The typed Architecture renderer preserves the existing architecture layout, routing, ownership compilation, and physical serialization. The independent Data Model renderer intentionally uses deterministic grid placement and direct property-reference routing instead of the removed radial/table algorithm. Table style and semantic content remain, but Data Model visual byte parity is not claimed.

## Settings ownership and compatibility

`LegacyDiagramSettingsAdapter` retains existing JSON and VSIX option loading and maps fields as follows:

- `ExcludedNamespaces`, `ExcludedNames`, `RootDiscoveryPatternsText`, `ExternalDependencyTag` -> Architecture analysis.
- `Canvas`, `Layout`, `StyleRules`, `Overrides`, project-container styles, connector style, and node duplication -> Architecture rendering.
- `ExcludedNamespaces`, `ExcludedNames` -> Data Model analysis.
- Canvas, connector style, and all `Layout.DataModel*` values -> Data Model rendering.
- Draw.io host and empty-document policy -> document composition defaults.

Architecture root regexes and graph-size/layout policy are not read by Data Model analysis. Data Model eligibility does not retain or remove Architecture nodes.

## Failure and compatibility behaviour

- `FailAll` remains the compatibility default.
- `KeepSuccessfulPages` is represented and tested; a failed job yields a job diagnostic and successful pages can still compose.
- An empty Data Model is successful and emits no page by default.
- The normal CLI and VSIX-compatible generation path use typed jobs.
- CLI strict-validation, diagnostic-export, development-project-region, serialization-repeat, and manifest modes temporarily retain the legacy Architecture exporter result because their richer route-diagnostic result has not yet been lifted into `TypedDiagramGenerationResult`. That exporter now emits one composed Architecture page and contains no Data Model implementation.
- JSON output remains an Architecture compatibility snapshot.

## Deleted combined responsibility

The embedded Data Model classifier, forced semantic retention, `RenderGraph.DataModels`, endpoint filtering, hard-coded active two-page path, and approximately 1,100 lines of combined Data Model layout/routing/serialization were removed. Pipeline dependency guards prevent analysers and renderers from invoking their peer diagram-type contracts and prevent the document composer from referencing semantic models.

Across the tranche from the accepted audit baseline, 45 files changed with 2,360 insertions and 1,433 deletions (net +927). The new semantic domains, typed jobs, adapters, tests, and evidence account for the net growth; the combined renderer itself shrank materially.

## Final verification

- Focused analyser, renderer, composer, orchestration, settings, and dependency-guard suites passed throughout the staged commits.
- `dotnet build StandardIo.ArchitectureDiagram.sln -c Release --no-restore` succeeded with 0 errors. Six pre-existing VS SDK assembly/analyser warnings remain (`StreamJsonRpc`, Visual Studio RPC/ServiceHub version resolution, `VSSDK007`, and `VSTHRD010`).
- `dotnet test StandardIo.ArchitectureDiagram.sln -c Release --no-build` passed 397/397 tests.
- Repeating the normal combined typed CLI generation produced byte-identical SHA-256 `5f1fcffdece4c512a057c08fbbc4458913c5a5f39b6402c4e9834a3829ad77ef`.
- Core remains `netstandard2.0`.
- No VSIX was packaged.
