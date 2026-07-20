# Typed pipeline options and migration map

## Option 1: diagram-specific interfaces (recommended)

```csharp
interface IArchitectureAnalyser { Task<ArchitectureModel> AnalyseAsync(SelectedSource source, ArchitectureSettings settings, CancellationToken ct); }
interface IDataModelAnalyser { Task<DataModelModel> AnalyseAsync(SelectedSource source, DataModelSettings settings, CancellationToken ct); }
interface IArchitectureRenderer<TPage> { TPage Render(ArchitectureModel model, ArchitectureRenderSettings settings); }
interface IDataModelRenderer<TPage> { TPage Render(DataModelModel model, DataModelRenderSettings settings); }
```

DI registers concrete analyser per semantic type and renderer per model/output page type. The orchestrator expands the request into independent jobs, invokes each analyser/renderer, then passes pages to the selected format composer.

Benefits: strongest compile-time separation; clear settings ownership; no runtime casts; repository language remains explicit. Risks: more interfaces as diagram types/formats grow. Migration cost: medium, but supports vertical extraction and preserves existing public facades as adapters.

## Option 2: generic pipeline contracts

```csharp
interface IDiagramAnalyser<TRequest, TModel> { Task<TModel> AnalyseAsync(SelectedSource source, TRequest settings, CancellationToken ct); }
interface IDiagramRenderer<TModel, TPage> { TPage Render(TModel model, CancellationToken ct); }
```

DI resolves closed generic types. Orchestration uses typed job handlers so it never stores models as `object`.

Benefits: regular extension model and less repeated infrastructure. Risks: a generic runtime job catalog can drift into reflection, casts, or a service locator; diagram-specific capabilities become awkward. Migration cost: medium-high because current public contracts and registry are non-generic.

## Option 3: vertical job handlers

```csharp
interface IDiagramGenerationJob<TRequest, TPage> { Task<TPage?> ExecuteAsync(TRequest request, CancellationToken ct); }
```

Each handler privately owns its analyser and renderer. The orchestrator resolves handlers from explicit selected job descriptors and composes returned pages.

Benefits: easiest failure isolation and vertical-slice testing. Risks: analyser/renderer separation is less visible, format growth may duplicate handlers, and reusable non-Draw.io rendering is less direct. Migration cost: lowest initially, potentially higher later.

## Recommendation

Use option 1, with format-typed page results. Diagram-specific semantic interfaces express product concepts; `TPage` separates those concepts from output formats without forcing all models through a generic registry.

Initial Draw.io shape:

```csharp
sealed record DrawioPage(string SuggestedName, XElement GraphModel, IReadOnlyList<DiagramDiagnostic> Diagnostics);
interface IDrawioDocumentComposer { DrawioDocument Compose(IReadOnlyList<DrawioPage> pages, DrawioDocumentSettings settings); }
```

No analyser references another analyser. No renderer references another renderer. The composer knows requested order, unique page naming/IDs, document metadata, and serialization only.

Code-path interfaces should not be registered until a real code-path request/model exists; reserving the diagram-type value in the request is sufficient.

## Future request model

```text
DiagramGenerationRequest
  SourceSelection: Solution | Project | SelectedProjects | FolderSubtree
  Jobs[]:
    DiagramType
    InstanceName / page-name hint
    type-owned settings
    optional type-owned input (required for code paths)
  OutputFormat: Drawio initially
  Grouping: OneFileSeparatePages | SeparateFiles
  OutputPath
  FailurePolicy: FailAll | KeepSuccessfulPages
```

Expected behavior:

- Architecture succeeds / data model fails: `KeepSuccessfulPages` writes architecture plus a diagnostic result; `FailAll` writes nothing atomically.
- Two architecture jobs: two independently named pages in request order.
- No data entities: successful empty/no-page result according to explicit job policy, not an architecture mutation.
- Code path lacks required endpoints: that job fails validation before analysis.
- Same source type in multiple models: allowed; identities are local to each page/model.
- Duplicate names: composer deterministically appends ` (2)`, ` (3)` or uses explicit unique names.

## Semantic-scope isolation

- `RootDiscoveryPatternsText` is currently consumed by the combined analyser and therefore accidentally affects table availability. It must move into `ArchitectureSettings`.
- A future data-model analyser must select entities independently from the complete selected source universe. It must not inherit exposure roots or architecture graph thresholds.
- Code-path scope must be driven by its own requested start/end symbols and traversal policy.
- Source loading, compilation access, canonical symbol identity, cancellation, diagnostics, and caching remain shared infrastructure.
- Draw.io continues to perform no semantic root discovery.

## Current-to-target migration map

| Current subsystem | Destination | Action |
| --- | --- | --- |
| `WorkspacePathBroker`, workspace models | SharedSourceInfrastructure | retain |
| Roslyn compilation/symbol enumeration helpers | SharedLowLevelSemanticUtility | extract without selection policy |
| constructor dependency discovery/root reachability | ArchitectureAnalysis | move behind architecture analyser |
| `DiagramModel`, `ProjectContainer`, `TypeNode`, `DependencyEdge`, semantic selection metadata | ArchitectureModel initially | narrow; do not expose table members |
| property/member discovery and relationship resolution | DataModelAnalysis | rebuild as independent complete-pass analyser |
| future `DataModelEntity/Property/Relationship` | DataModelModel | add independently |
| `RenderGraph.Nodes/Links`, `RenderLayout`, routing/ownership | ArchitectureRendering | retain |
| `RenderGraph.DataModels`, `IsModelType` | TemporaryCompatibility then Delete | deletion gate after independent page parity |
| table positioning/routing/style methods in `DiagramFileBuilder` | DataModelRendering for Draw.io | extract into page renderer |
| `ArchitectureGenerator` nested wrapper | Architecture Draw.io renderer | replace with real page result |
| `DiagramFileBuilder.Build` hard-coded `mxfile` | DrawioPageComposition | split page building from composition |
| `IDiagramRenderer`/registry | TemporaryCompatibility | adapt public calls, then replace with typed jobs/renderers |
| `DiagramSettings`/`LayoutSettings` | TemporaryCompatibility | split architecture, data-model, and output settings |
| generation exposure/orchestration/coordination | TemporaryCompatibility | facade over new request orchestrator |
| CLI/VSIX one generation command | NeedsProductDecision | retain compatibility; later add selection UI/actions |
| JSON renderer | Renderer-independent model evidence / future typed serialization | split by semantic model |
| code-path pipeline | CodePathAnalysis/Model/Rendering | not present; defer implementation |

## Deletion and retention list

- `SemanticScopeSelector.IsDataModel`: DeleteDuringTypedSeparation.
- `RenderGraph.IsModelType` and `DataModels`: RetainTemporarilyWithDeletionGate, then delete.
- Data-model methods/styles inside `DiagramFileBuilder`: move, then delete from combined builder.
- Combined two-page construction: replace with composer; compatibility facade may temporarily request both pages.
- Global data-model layout properties: move to data-model settings; retain JSON migration adapter temporarily.
- Architecture omission reason `RenderedDataModelTable`: not present.
- Architecture-to-table transformed links: not present; do not add.
- Workspace/file/geometry primitives: RetainAsSharedInfrastructure where they have no semantic policy.
- Existing table visual tests: move with renderer extraction; do not delete.

## Safest vertical tranches

1. Add independent `DataModelModel` and analyser using complete two-pass symbol/property resolution; snapshot parity without changing output.
2. Add Draw.io data-model page renderer returning `DrawioPage`; compare page XML/geometry byte-for-byte behind a development seam.
3. Add architecture-specific model/analyser boundary; stop architecture scope from retaining table types and stop `RenderGraph` classification.
4. Extract architecture Draw.io page result and introduce deterministic document composer; compatibility request still asks for both pages.
5. Add typed generation request/orchestrator and per-type settings with old-settings migration.
6. Add explicit CLI/VSIX diagram-type and grouping selection; preserve legacy one-click combined action until approved for deletion.
7. Add code-path contracts only with the first concrete code-path feature.

Approval decisions needed later: default diagram types for the legacy command, empty-data-model page behavior, partial-failure policy, naming collision convention, one-file versus separate-file default, and settings migration lifetime.
