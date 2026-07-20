# Diagram-type architecture audit

Audit baseline: `2b222b3`; branch `feature/decuplicate-node-option`.

This is a state-discovery and design review. It does not introduce typed analysers/renderers or change user-visible generation. The only production cleanup is removal of two empty `foreach` statements in `RenderGraph`.

Detailed evidence:

- [Current pipeline and coupling](current-pipeline.md)
- [Exact StandardIo Core accounting](core-data-model-accounting.md)
- [Exact StandardIo Core property rows](core-data-model-properties.md)
- [Typed designs and migration map](typed-pipeline-options.md)
- [Required completion report](completion-report.md)

## Executive finding

The current “Data Model” feature is a property-summary/table view embedded in the architecture pipeline. It is not an independent ERD.

```text
Selected Roslyn projects
  -> RoslynDependencyAnalyzer
       discovers one combined TypeNode/DependencyEdge model
       extracts public instance properties for every type
  -> SemanticScopeSelector
       architecture root reachability
       plus special retention of property-only classes
  -> DiagramModel (combined architecture + table candidates)
  -> RenderGraph.From
       removes interfaces and table candidates from architecture Nodes
       stores table candidates in DataModels
       drops architecture links whose endpoint is not in Nodes
  -> DeterministicDrawioExporter
       architecture layout/routing
  -> DiagramFileBuilder
       ArchitectureGenerator -> Architecture tab
       DataModelGenerator -> Data Model tab
  -> one mxfile string
  -> atomic overwrite by DiagramFileBroker
```

The strongest violations of the approved direction are:

1. Architecture scope selection retains nodes solely because the table feature may render them.
2. Architecture rendering classifies and removes those nodes from the architecture graph.
3. One combined semantic model and settings object feed both outputs.
4. One Draw.io file builder owns both renderers and hard-codes both pages.
5. Property relationship resolution depends on discovery order because `TypeId` is resolved while the type dictionary is still being populated.

## Current independence answers

| Question | Answer | Coupled boundary |
| --- | --- | --- |
| Independent data-model analyser | No | Properties are extracted by `RoslynDependencyAnalyzer`; eligibility is split across `SemanticScopeSelector` and `RenderGraph` |
| Independent data-model semantic model | No | `TypeNode`, `TypeProperty`, and `RenderNode` are shared with architecture |
| Independent data-model renderer | No | Nested `DataModelGenerator` delegates back into `DiagramFileBuilder` and consumes `RenderNode` |
| Independent output page | Partial | A separate `<diagram name="Data Model">` exists, but it is always composed with Architecture by one builder |
| Independent command | No | One “Generate Draw.io Architecture Diagram” command always runs the combined pipeline |
| Independent settings | No | Data-model layout values live in global `LayoutSettings` |
| Data-model generation without architecture | No | It requires architecture analysis, `RenderGraph`, `RenderLayout`, and combined serialization |
| Architecture without data-model classification | No | Scope retention and `RenderGraph` architecture filtering both call the heuristic |

## Verification baseline

- Configured Core selection: 322 discovered nodes; 12 architecture-reachable nodes; 18 retained table nodes; 292 omitted nodes.
- Table output: 18 entities, 116 property rows, 5 generated property-reference edges.
- Full semantic graph: 24 constructor-dependency links incident to the 18 table nodes.
- Configured scope: all 24 incident links are filtered by explicit root scope.
- Full-input rendering: the same 24 links would be removed from the architecture render graph with their table-classified endpoints.
- Project-region production SHA before/after dead-loop removal: `BF7E520AED1914E64D82F9FEE5B573D8069774F4B2AE39359BF82023750B49FA`.
- No VSIX was packaged.
