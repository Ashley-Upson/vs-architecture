# StandardIo Core: exact table-node and link accounting

Inputs:

- Full snapshot: `artifacts/diagram-type-audit/core-full.json` using empty root patterns.
- Configured snapshot: `artifacts/diagram-type-audit/core-configured.json` using `\.Exposures\.` then `Controller$`.
- Both snapshots use the same Core project and rebuilt CLI.

Every entity below qualified because it is a non-interface class with at least one captured public instance property and zero captured public instance ordinary methods. All are in project `StandardIo.ArchitectureDiagram.Core`. None belongs to the 12-node configured architecture reachability set, so none would otherwise render as an architecture node in that configured result.

| Semantic ID | Type | Namespace | Rows | Generated table relationships |
| --- | --- | --- | ---: | --- |
| `type_ce4f2e0fa8ddb5d4` | WorkspacePathLoadOptions | `.Brokers.Workspaces` | 1 | none |
| `type_1c37fc0bcaa859a3` | WorkspacePathLoadResult | `.Brokers.Workspaces` | 2 | none |
| `type_cd2250dd734015c7` | CanvasSettings | `.Models` | 2 | none |
| `type_7192e68046045ba2` | ConnectorStyle | `.Models` | 3 | none |
| `type_eeb29e108d8c1ff8` | DiagramPathGenerationResult | `.Models` | 4 | none |
| `type_894ce352aa3e5451` | DiagramSettings | `.Models` | 15 | Canvas, Connector |
| `type_969a04da070f8d7c` | DrawioDiagnosticExportResult | `.Models` | 5 | none |
| `type_51b982a3aca5bde4` | DrawioGenerationResult | `.Models` | 13 | Diagnostics |
| `type_42d613792033fe43` | GenerationPerformanceSession.Frame | `.Models` | 11 | none |
| `type_b690273c0af33ff5` | LayoutSettings | `.Models` | 36 | none |
| `type_41ed9fa4485e8059` | NodeDuplicationSettings | `.Models` | 2 | none |
| `type_544ce2f80d9b446d` | NodeStyle | `.Models` | 6 | none |
| `type_770bda0521f14f6c` | StyleOverride | `.Models` | 2 | Style -> NodeStyle |
| `type_6c8a08eeb24a703f` | StyleRule | `.Models` | 2 | Style -> NodeStyle |
| `type_8f1de8527c4576a1` | NodeOwnership | `.Services.Foundations.Drawios` | 3 | none |
| `type_8ba5e139f4deaeaa` | RenderGraph | `.Services.Foundations.Drawios` | 5 | none |
| `type_e11c4ca329cf91dd` | RoutedEdgeGeometry | `.Services.Foundations.Drawios` | 2 | none |
| `type_9624f0784605b4ed` | TraceabilityValidationResult | `.Services.Foundations.Drawios` | 2 | none |

Total: 18 tables, 116 property rows, 5 generated property-reference relationships.

All tables appear as root-level cells on the separate `Data Model` page. They do not enlarge architecture project bounds or participate in architecture placement/validation. They do share the enclosing file-generation failure boundary.

## Incident semantic-link ledger

The full discovered graph contains 24 constructor-dependency links with at least one of these 18 types as an endpoint. Under configured root reachability, every one is `FilteredByScope`. With full-input scope, `RenderGraph.FromBaseDiagram` would classify each as `RemovedFromArchitectureBecauseDataModel` because the endpoint is absent from architecture `Nodes`. None becomes a table relationship; table relationships are separately inferred from properties.

| Edge | Source -> target | Full-input architecture | Configured result |
| --- | --- | --- | --- |
| `edge_75a084da4f3ca0ff` | LinkLayout -> RoutedEdgeGeometry | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_2d33472f401bd5f0` | RoutedEdgeGeometry -> Point | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_a82fe4682cff9386` | DrawioGenerationResult -> ValidationFinding | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_9533d826f8a9f0b2` | DrawioGenerationResult -> RouteRepairAttempt | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_0f9afef6b1582a92` | DrawioGenerationResult -> GeneratedRoute | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_6330859e69aba1a6` | DrawioGenerationResult -> DrawioDiagnosticExportResult | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_27a3acaf04ebfd5f` | DrawioGenerationResult -> PipelineStageMetric | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_ebcfdcc388d4ba1` | PlacedGraph -> RenderGraph | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_9a3684b44d42de7e` | ValidatedLogicalRoutes -> TraceabilityValidationResult | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_cd8a0a9ae17dc22c` | ProjectPlacementResult -> NodeOwnership | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_7491879fc41d940d` | GenerationPerformanceSession.Scope -> Frame | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_096df9739fd89053` | WorkspacePathLoadResult -> Microsoft.CodeAnalysis.Project | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_8b1376302238da3d` | DeterministicDrawioExporter.PreparedExport -> DiagramSettings | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_ed17c9c1663e0782` | StyleResolver -> DiagramSettings | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_cfa70e74d0f74c6a` | DiagramFileBuilder -> DiagramSettings | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_81a9f714e7980ea6` | RenderGraph -> RenderProject | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_d2d0538a5e41d706` | RenderGraph -> RenderNode | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_ffc27f06f9c031f8` | RenderGraph -> RenderLink | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_a0a481e246b587f5` | RenderLayout -> RenderGraph | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_eb159f941f0d2be8` | RenderLayout -> TraceabilityValidationResult | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_057024aab9c675b9` | RenderLayout.LegacyRoutingResult -> TraceabilityValidationResult | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_af5e9d2e70368e3c` | RouteRepairResult -> TraceabilityValidationResult | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_a7b223daf25e216d` | RouteRepairCoordinator.Pipeline -> TraceabilityValidationResult | RemovedFromArchitectureBecauseDataModel | FilteredByScope |
| `edge_9c57f5e4d4a5f7be` | TraceabilityValidationResult -> TraceabilityViolation | RemovedFromArchitectureBecauseDataModel | FilteredByScope |

Category totals for the configured result:

```text
RetainedArchitectureLink:            0
RemovedFromArchitectureBecauseDataModel: 0 (scope removed them earlier)
RenderedAsDataRelationship:          5 property relationships, none derived from incident edges
FilteredByScope:                     24
FilteredWithEndpoint:                0 additional
RepresentedElsewhere:                0 incident links
UnsupportedOrDropped:                0
Unaccounted:                         0
```

For a full-input render the same ledger becomes 24 `RemovedFromArchitectureBecauseDataModel`, while the five independently inferred property relationships remain.
