# Current pipeline, commands, composition, and coupling

## Concrete call graphs

### CLI/default orchestration

```text
Program.Main
  -> CliOptions.Parse
  -> SettingsSerializer.Import / DiagramSettings.CreateDefault
  -> DiagramGenerationExposure.GenerateAsync(path,...)
  -> DiagramPathGenerationCoordinationService.GenerateAsync
       -> WorkspacePathBroker.LoadAsync
       -> DiagramRendererRegistry.Resolve(OutputRenderer)
       -> DiagramGenerationOrchestrationService.GenerateAsync(projects,...)
            -> DiagramAnalysisProcessingService.AnalyzeAsync
            -> RoslynBroker.AnalyzeAsync
            -> RoslynDependencyAnalyzer.AnalyzeAsync
                 -> GetNamedTypes
                 -> CollectProperties
                 -> CountMethods
                 -> constructor dependency discovery
                 -> SemanticScopeSelector.Select
            -> DiagramRenderingProcessingService.Render
            -> selected IDiagramRenderer.Render(DiagramModel,...)
                 JSON: DiagramModelSerializer.Export
                 Draw.io: DrawioDiagramRenderer
                   -> DeterministicDrawioExporter.GenerateResult
                   -> RenderGraph.From
                   -> RenderLayout.Build
                   -> CoordinateOwnershipCompiler
                   -> ProjectOwnershipBoundsCompiler (architecture only)
                   -> DiagramFileBuilder.Build
                        -> ArchitectureGenerator.Generate
                        -> DataModelGenerator.Generate
                        -> hard-coded two-page mxfile
       -> DiagramFileBroker.WriteTextAsync (temporary file + replace/move)
```

The CLI contains a Draw.io-specific fast path for diagnostics/project-region evidence (`if OutputRenderer == drawio`) and otherwise uses the generic exposure. This is an output-format runtime switch, not a diagram-type switch.

### Visual Studio extension

```text
GenerateDiagram command
  -> GetSelectedDiagramTargetAsync
       solution node -> every C# project
       project/project-item -> one matched Roslyn project
  -> SettingsStore.Load
  -> DiagramRendererRegistry.Resolve
  -> PromptForSavePath: <target>.architecture.<extension>
  -> IDiagramAnalysisProcessingService.AnalyzeAsync(projects,...)
  -> renderer.Render(combined DiagramModel,...)
  -> IDiagramFileBroker.WriteTextAsync
```

Solution folder/folder menu placements exist, but target resolution only recognizes the selected solution or a containing project. There is no selected-project-set aggregation and no independent subtree target in the VSIX handler.

## Phase trace

| Phase | File/type/method | Input -> output | State/effect | Independent? |
| --- | --- | --- | --- | --- |
| Symbol discovery | `RoslynDependencyAnalyzer.AnalyzeAsync` | Roslyn `Project[]` -> combined nodes | Classes/interfaces only; exclusions/styles configuration consulted | No; shared architecture/table discovery |
| Member extraction | `CollectProperties` | `INamedTypeSymbol` -> `TypeProperty[]` | Public, declared, non-static properties; collection element unwrapping | No |
| Method heuristic input | `CountMethods` | symbol -> integer | Public, declared, instance ordinary methods only | No |
| Architecture dependencies | constructor scanning | declarations -> `DependencyEdge[]` | Primary/ordinary constructor dependencies; DI registration substitution | Architecture-oriented |
| Scope | `SemanticScopeSelector.Select` | discovered `DiagramModel` -> selected `DiagramModel` | Root reachability plus unconditional retention of `IsDataModel` types | No; architecture depends on table classification |
| Table eligibility | `SemanticScopeSelector.IsDataModel`, `RenderGraph.IsModelType` | `TypeNode` -> bool | Non-interface in selector; properties > 0 and methods == 0 in both | Duplicated policy |
| Architecture/table split | `RenderGraph.FromBaseDiagram` | `DiagramModel` -> `RenderGraph` | Eligible tables excluded from `Nodes`, copied into `DataModels`; incident links removed by endpoint filter | No; mutates architecture presentation membership |
| Architecture layout | `RenderLayout.Build` | `RenderGraph.Nodes/Links` -> geometry | Does not lay out `DataModels` | Architecture-specific |
| Table layout | `DiagramFileBuilder.PositionDataModelTables` | `RenderNode DataModels` -> rectangles | Separate radial/component/grid packing in root coordinates | Draw.io-specific implementation |
| Table relationships | `DataModelRoot` | property `TypeId` -> `data_model_edge_N` | Ordinary directed property references only; original dependency IDs/provenance discarded | No independent semantic relationship model |
| Table serialization | `AddDataModelTable`, `Edge(LinkLayout)` | render nodes/rectangles -> `mxCell` | Container/header/property-row cells and root-owned edge cells | Draw.io-only |
| Composition | `DiagramFileBuilder.Build` | architecture + tables -> string | Always creates Architecture and Data Model diagrams | Hard-coded shared composition |
| File output | `DiagramFileBroker.WriteTextAsync` | path/string -> file | Atomic replacement; never appends | Safe shared infrastructure |

Data-model tables are on a separate `mxGraphModel`, so they do not affect architecture project bounds, architecture placement, ownership segmentation, or architecture validation. They do affect the success, runtime, and size of the single combined serialization operation.

## Coupling matrix

| Coupling | Classification | Direction | Product-direction status |
| --- | --- | --- | --- |
| Workspace/project loading | SharedSourceInfrastructure | shared | Safe |
| Roslyn compilation/symbol traversal | SharedLowLevelSemanticUtility | shared | Extractable/safe below analyser boundary |
| `DiagramModel` holds table candidates and architecture edges | SharedCombinedModel | mutual | Violation |
| Scope selector retains property-only classes outside root reachability | ArchitectureDependsOnDataModel / SharedSelectionPolicy | table -> architecture | Violation |
| `RenderGraph` excludes table candidates from architecture nodes | ArchitectureDependsOnDataModel | table -> architecture | Violation |
| Table generator consumes `RenderNode` | DataModelDependsOnArchitecture | architecture presentation -> table | Violation |
| Table relationships use `TypeProperty.TypeId` from combined discovery | DataModelDependsOnArchitecture | shared analyser state -> table | Violation |
| Architecture layout ignores `DataModels` | DiagramTypeSpecific | separate within renderer | Good seam, not independent pipeline |
| Two page roots inside one builder | SharedRenderer / SharedDocumentComposition | mutual | Violation |
| One `LayoutSettings` contains both routing and table values | AccidentalCoupling | global | Violation |
| Both pages serialized into one `mxfile` | SharedSerializationOnly | shared | Composer behavior is valid, ownership is wrong |

Specific answers:

- Data-model classification removes eligible types and all incident links from the architecture render graph: yes.
- Architecture reachability controls table availability: partly; the selector overrides reachability to retain heuristic table types, but configured exclusions/source scope still control them.
- Table heuristic changes analyser output: yes, by retaining otherwise omitted nodes.
- Architecture and table candidates share `ProjectContainer.Types`; render preparation then splits them into `RenderGraph.Nodes` and `DataModels`.
- Tables are special uses of `RenderNode`, not independent entity models.
- Architecture bounds/validation do not include table geometry because pages have distinct graph roots.
- One page cannot fail independently: any exception prevents the combined string/file.
- There is no setting to disable one page without changing code.

## Current heuristic: what it actually means

The current feature is a property-summary/class-table view with best-effort property-reference links. It is not a database-schema analyser and should not yet be called an ERD.

Eligibility is exactly:

```text
TypeKind is Class (interfaces rejected during table split)
AND at least one public declared instance property was captured
AND zero public declared instance ordinary methods exist
```

Behavior by C# feature:

- Constructors: ignored by method count; constructor-heavy DTOs remain eligible.
- Records: class records can qualify; record structs cannot because structs are not discovered.
- Fields: never captured.
- Inheritance: base properties/methods are not enumerated by `GetMembers()`; inheritance relationships are absent.
- Interfaces: discovered into the combined model but never rendered as tables.
- Attributes/keys/foreign keys: ignored.
- Generic collections: only one-argument `IEnumerable`, `IReadOnlyList`, `IList`, `List`, `ICollection`, and `Collection` are unwrapped.
- Nullable value types: not specially unwrapped; nullable annotations do not create cardinality.
- Navigation-like properties: treated as ordinary directed type references.
- Primitive/external properties: displayed as rows but have no relationship edge.
- Nested classes: discovered and may qualify.
- Source-generated classes: may exist in a Visual Studio compilation, but the lightweight workspace loader only adds physical `.cs` documents and cannot guarantee generator output.
- Abstract classes: can qualify.
- Enums/value-type structs: excluded at discovery.
- Value objects implemented as classes: indistinguishable from entities.

Relationship meaning is “A has a displayed property whose currently resolved element/reference type is B.” It does not reliably express one-to-one, one-to-many, many-to-many, composition, inheritance, primary/foreign keys, or database ownership. Collection membership is visually indistinguishable from scalar reference because cardinality is not retained. Resolution is also order-sensitive: `CollectProperties` looks up `TypeId` while `typeByFullName` is still being populated, so references to types discovered later may remain unresolved.

## Commands and configuration

- Commands: one generation command plus open/export/import global settings.
- Menu contexts: solution, project, solution folder, folder; actual handler resolves solution or one containing project only.
- Multi-project: entire solution only; no arbitrary selected-project set in VSIX or CLI filter syntax.
- CLI: `.sln`, `.csproj`, or folder; optional single project filter; renderer `drawio|json`; no diagram-type selector.
- Background execution: VSIX analysis/rendering use `Task.Run`, cancellation dialog, stage text, message-box failure reporting.
- Settings: one `DiagramSettings`; 15 `DataModel*` layout values inside global `LayoutSettings` and the same options page/JSON.
- Independent table action/multi-output selection: absent.
- File names: `<target>.architecture.drawio` or `.json`; save dialog in VSIX, explicit/default path in CLI.
- Overwrite: atomic replace, no append/page merge.
- Page names: fixed “Architecture” and “Data Model”.

## Draw.io composition capability

There is no first-class `DrawioPage` or composer. `DiagramFileBuilder.Build` nevertheless proves basic multi-tab serialization by placing two `<diagram>` children in one `<mxfile>`.

- Page count: exactly two, always.
- Page IDs: absent.
- Page names: fixed strings.
- Cell ID scope: effectively page-local; both pages use `0` and `1`, and no cross-page references exist.
- Page ordering: deterministic but hard-coded Architecture then Data Model.
- Metadata/settings: host on `mxfile`; graph settings repeated per `mxGraphModel`; semantic metadata is not stored at page level.
- Independent graphs can technically be combined as sibling `<diagram>` elements, but no API accepts rendered pages.
- Failure isolation: absent.
- Multiple architecture pages: unsupported.
- Existing coverage: acceptance test asserts both page names; exporter tests inspect table placement and relationship geometry.

Smallest future boundary: `DrawioPage(Id?, SuggestedName, XElement GraphModel, Diagnostics)` returned independently, then `IDrawioDocumentComposer.Compose(IReadOnlyList<DrawioPage>)` assigns deterministic unique names/IDs and writes one `mxfile` without knowing semantic models.

## Interface and dependency inventory

| Interface | Shape | Implementation/lifetime | Assumptions | Typed suitability |
| --- | --- | --- | --- | --- |
| `IWorkspacePathBroker` | path/options -> Roslyn projects | `WorkspacePathBroker`, transient | Source only | Safe shared |
| `IRoslynBroker` | projects/settings -> `DiagramModel` | `RoslynBroker`, transient | Combined architecture semantics | Not suitable |
| `IRoslynDependencyAnalyzer` | project(s)/`DiagramSettings` -> `DiagramModel` | transient | Combined architecture/table model | Not suitable |
| `IDiagramAnalysisProcessingService` | projects/settings -> `DiagramModel` | transient | Non-generic combined model | Not suitable |
| `IDiagramRenderer` | `DiagramModel`, settings -> string | Draw.io + JSON, transient registry | Output format identified at runtime; combined semantics | Not suitable |
| `IDiagramRendererRegistry` | string ID -> renderer | transient | Runtime lookup/fallback | Useful only as temporary compatibility |
| `IDeterministicDrawioExporter` | combined model -> complete Draw.io document | transient | Architecture layout plus both pages | Draw.io and combined-feature specific |
| `IDiagramRenderingProcessingService` | combined model/settings -> string | transient | Runtime renderer registry | Not suitable |
| generation orchestration/coordination/exposure interfaces | source/projects -> string/path result | transient layers | One combined generation | Temporary compatibility |
| `IDiagramFileBroker` | path/string -> file | transient | Format-neutral | Safe shared |

Runtime selection occurs in `DiagramRendererRegistry.Resolve`, the CLI’s Draw.io diagnostic branch, and settings `OutputRenderer`. No service locator exists inside Core production classes; CLI/VSIX build a provider at entry points. Default constructors manually construct dependency graphs alongside DI registrations, which increases migration surface.
