# Document compilation

[Gateway](../../README.md) | [Contract](../rule-contract.md)

Rendered tabs -> final HTML or DrawIO document bytes.

## Dependency tree

```text
AllDiagramRenderer
  IDiagramDocumentOrchestrationService
    IDiagramTabService
      IDiagramTabBroker
        IDiagramTabRendererFactory
    IDocumentCompilationService
```

## Simple conditions

Only All produces multiple tabs: Architecture, Composition, Call Chain and Entity Relationship. HTML embeds self-contained pages; DrawIO combines diagram pages. This is serialization, not iterative layout.

## Expanded injected dependency tree

Derived from the current constructors and DI registrations. Repeated services are references to the earlier subtree; named factory implementations are documented separately.

```text
AllDiagramRenderer
  IDiagramDocumentOrchestrationService -> DiagramDocumentOrchestrationService
    IDiagramTabService -> DiagramTabService
      IDiagramTabBroker -> DiagramTabBroker
        IDiagramTabRendererFactory -> DiagramTabRendererFactory
          IServiceProvider
          IServiceProvider - keyed registrations listed in implementations/readme.md
      IProjectModelTreeService -> ProjectModelTreeService
        IProjectModelSplitterBroker -> ProjectModelSplitterBroker
    IDocumentCompilationService -> DocumentCompilationService
```
