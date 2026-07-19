# Core semantic exposure audit

This is a read-only audit. No semantic-selection rule changed.

## Selection pipeline

`DiagramAnalysisProcessingService` produces the semantic model. `RenderGraph.FromBaseDiagram` then excludes interfaces from architecture nodes and classifies property-only, zero-method types as data-model tables. When the graph exceeds `ExposureTreeLayoutThreshold`, `RenderGraph.From(..., settings)` invokes `BuildCanonicalExposureGraph`. `IsExposureNode` selects names under `.Exposures.` or ending in `Controller`; Core has one qualifying root, `DiagramGenerationExposure`. Only dependency-reachable nodes and links from that root are cloned into the routed graph.

Node duplication affects clone identity, but not which semantic region is reachable. Routing-family support, corridor capability, and legacy layout eligibility do not participate in exposure selection.

## Exact node reconciliation

| Category | Count | Examples | Owner/rule |
|---|---:|---|---|
| RoutedArchitectureNode | 11 | `DiagramGenerationExposure`, `DiagramPathGenerationCoordinationService`, `DiagramGenerationOrchestrationService`, `DeterministicDrawioExporter` | reachability from preferred exposure root |
| RenderedDataModelTable | 18 | `WorkspacePathLoadOptions`, `CanvasSettings`, `ConnectorStyle` | `IsModelType`: properties and zero methods |
| CollapsedOrCanonicalized | 0 | - | no additional original node collapsed in this region |
| FilteredByConfiguredExposure | 276 | `DiagramFileBroker`, `DiagramFileSystem`, `LayoutRevision`, `AxisInterval` | 231 internal and 45 external nodes not reachable from preferred root |
| FilteredByNodeKind | 13 | `IDiagramFileBroker`, `IRoslynBroker`, `IDiagramGenerationExposure` | interfaces excluded from render nodes |
| FilteredAsUnconnected | 0 | - | included in exposure-filter count because preferred-root reachability occurs first |
| RepresentedThroughAnotherNode | 0 | - | no additional semantic node representation |
| OutsideSelectedProjectRegion | 0 | - | exact Core project filter |
| UnsupportedOrDropped | 0 | - | no node is lost after applying the recorded selection rules |
| **Total** | **318** | | |

The 18 data-model nodes are rendered as tables outside the routed architecture project region. Their properties are preserved inside those tables. The 231 filtered architecture classes and 45 external types are not represented elsewhere in the routed region.

## Exact link reconciliation

| Category | Count | Rule |
|---|---:|---|
| RoutedArchitectureLink | 12 | both endpoints reached from `DiagramGenerationExposure` |
| RenderedInsideDataModelRepresentation | 0 | data tables render properties, not dependency edges |
| CollapsedIntoCanonicalLink | 0 | all twelve selected semantic links remain distinct |
| FilteredByExposure | 347 | at least one endpoint is outside preferred-root reachability |
| FilteredWithNode | 0 | accounted under exposure filtering |
| OutsideRegion | 0 | exact project selection |
| UnsupportedOrDropped | 0 | none after exposure selection |
| **Total** | **359** | |

## Questionable assumptions

1. Preferred exposure roots completely replace zero-incoming graph roots once any exposure exists. This is high product impact: 231 architecture classes disappear because one exposure class is present.
2. The graph-size threshold changes the semantic view, not just its layout. Small and large versions of the same project can therefore show different content.
3. `IsModelType` treats any property-bearing zero-method class as a data table. That is convenient but can misclassify architectural configuration or immutable value types.
4. External types are retained only when reached through selected internal sources. This is consistent, but makes the 45 external omissions invisible without diagnostics.
5. Namespace/name conventions (`.Exposures.`, `*Controller`) define product exposure implicitly and are not explicit user settings.

No exclusion is caused by legacy routing capability. The principal product mismatch risk is that “large project” silently means “preferred exposure slice,” rather than “the architecture requested by the user.”

## Recommended later work

1. Make exposure scope explicit in settings and diagnostics: `FullProject`, `ExposureRoots`, or named roots.
2. Separate semantic selection from the layout-size threshold.
3. Report omitted node/link counts in generated metadata.
4. Review data-model classification with explicit type-role metadata before changing it.

## Next larger targets

1. **cCoder ContentManagement, bounded exposure region** — expected 30-100 routed nodes with multi-parent fan-out and external boundaries; reproducible through the existing CLI/settings. Recommended next because prior real evidence already exposes dense fan-out and cross-boundary behaviour.
2. **StandardIo Core full architecture mode** — potentially 200+ architecture nodes, initially too large; useful only after an explicit semantic-scope product decision. Main challenge is multiple-root placement rather than new routing authority.
3. **cCoder ContentManagement complete project** — known to contain hundreds of fan-out routes and is reproducible, but is a later stress target rather than the next controlled visual tranche.

Recommended target: the bounded cCoder ContentManagement exposure region, preserving its real semantic input and explicit settings.
