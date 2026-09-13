# Diagram views

The CLI accepts `Architecture`, `Composition`, `DataModel` (`Data` remains an alias), and `All`. `All` produces three tabs/pages in this order: Architecture, Composition, Entity Relationship. The existing CallChain selection remains separate; a method-level implementation is not part of this change.

```powershell
dotnet run --project v8/DiagramCLI -- All path/to/project.csproj --format Html --output diagram.html
```

Individual selections produce one diagram. The compiler model is fetched once per project for an All request. Each tab receives its own RenderModel; the source ProjectModels are retained. NoDuplicates controls the Architecture tree view; Composition and DataModel use the complete project set.

## Relationships

Architecture keeps constructor injection, construction, and operational calls. Reference-only edges from typeof and generic type arguments are marked IsComposition. DI/hosting registration and resolution calls are composition operations too. This is per relationship: a mixed-use class can appear in both views. Extension attachments and the existing data-carrier filter continue to apply to Architecture.

Composition shows reference-only relationships between non-data types. DataModel shows known data types, their extracted properties/fields and relationships to other known data types. Edge labels identify the member and single/collection shape. These are CLR relationships, not inferred database primary keys, foreign keys, requiredness or cascade rules. Metadata absent from the compiler model is not fabricated.

## Pipeline

The existing format preparation brokers select IRenderModelBuilder through RenderModelBuilderFactory using the diagram type key. Architecture uses LayoutModelBuilder. Composition and DataModel use ContextualRenderModelBuilder, ContextualLayoutOrchestrationService and its projection/layout foundations. Both format writers consume positioned RenderModels.

AllDiagramRenderer delegates to DiagramDocumentOrchestrationService. DiagramTabService and DiagramTabBroker select each format/type renderer, then DocumentCompilationService compiles the returned tab contents. Draw.io pages share one mxfile; HTML tabs host independent documents so zoom, pan, SVG identifiers and styles remain isolated.

Composition and DataModel each have NodeWidth, NodeSpacing, RowSpacing and ProjectSpacing configuration settings under their existing RenderConfiguration children. Defaults are 300, 80, 80 and 150 pixels respectively.

Composition rows follow reference depth within each project, with deterministic handling of cycles. Data rows size themselves from their own members. Downward contextual links reuse the shared obstacle-aware router; same-row and backward relationships remain explicit and retain their member labels.
