# Completion report

## Current data-model pipeline

Selection entry point: combined `RoslynDependencyAnalyzer` and `SemanticScopeSelector`. Classification: property count > 0, public instance ordinary method count == 0, non-interface at render split. Model: combined `DiagramModel` -> `RenderGraph.DataModels`. Layout/rendering: Draw.io-only methods in `DiagramFileBuilder`. Serialization: hard-coded second page in the same `mxfile`. Invocation: always through the architecture command/CLI pipeline. Independent execution: no.

Core accounting: 18 entities; 116 fields/property rows; 5 property relationships; 24 incident semantic links. Configured architecture links retained/removed: 0/0 because all 24 are scope-filtered first. Full-input architecture would remove all 24 because of table endpoints. Data relationships produced: 5. Unsupported/dropped/unaccounted: 0 in the reconciled ledger.

Current independence: analyser no; model no; renderer no; command no; settings no; page partial; architecture without classification no; table generation without architecture no.

Coupling: source infrastructure is safely shared. Architecture depends on table classification in scope retention and render filtering. Tables depend on architecture `TypeNode`, `RenderNode`, and `RenderGraph`. The semantic model, settings, renderer/file builder, and file failure boundary are combined. Layout and graph roots are separate inside the combined renderer.

Heuristic meaning: property-summary/class tables, not an ERD. It has no reliable cardinality, keys, foreign keys, inheritance, composition, or database ownership. C# collection cardinality is discarded and relationship resolution is discovery-order-sensitive. Draw.io assumptions begin at table positioning/cell construction, not member extraction.

Commands/UI: one solution/project command, with menu placements for solution folders/folders that resolve only to solution or containing project. CLI supports solution/project/folder plus one project filter. One global options/settings object. No independent table action or multi-output selector. Output is `<target>.architecture.<format>` and atomically overwrites. Errors fail the whole request.

Draw.io composition: no page model/composer. Multi-page XML exists as exactly two hard-coded sibling diagrams named Architecture and Data Model. IDs are page-local in practice; order is fixed; no page IDs, collision handling, multiple architecture pages, or partial failure.

Existing interfaces are non-generic combined-model contracts. `IDiagramRenderer` selects Draw.io/JSON at runtime; the CLI also branches for Draw.io diagnostics. Typed suitability is limited to source/file brokers. Three designs and the recommended diagram-specific/format-typed design are in `typed-pipeline-options.md`.

Semantic-scope isolation: architecture root patterns currently affect the combined analyser and therefore table availability; this is a current violation. They must become architecture settings. Future data-model/code-path analysers must independently select from shared source/compilation infrastructure. Draw.io contains no root discovery.

Safe cleanup: no types/files added to production; no types/files deleted; 8 dead lines removed (two empty loops); no architecture assertions were needed beyond existing two-page and table geometry tests. Documentation files added: 6.

Verification: focused tests 36/36; full tests 379/379; release build passed with six existing VSIX warnings; normal configured Core SHA `5D31D345FF91B8168DFD94ECE115531B75FC0D6D51D6CDF9F9C174495B6D27B7` repeated exactly; project-region SHA `BF7E520AED1914E64D82F9FEE5B573D8069774F4B2AE39359BF82023750B49FA` unchanged by cleanup; VSIX packaged: no.

Main mismatch: a table view is embedded inside the architecture semantic/render pipeline. Main semantic risk: table eligibility changes architecture retention and link membership. Main renderer risk: a monolithic Draw.io builder owns both pages. Main UI limitation: one command/settings set always requests the combined output. Shortest safe path: independent data-model model/analyser, independent Draw.io page renderer, remove architecture classification, then introduce page composition and typed orchestration. Approval decisions are listed at the end of `typed-pipeline-options.md`.
