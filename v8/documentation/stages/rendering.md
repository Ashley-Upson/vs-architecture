# Format rendering

[Gateway](../../README.md) | [Contract](../rule-contract.md)

Positioned RenderModel -> bytes for one diagram tab.

## Dependency tree

```text
HtmlDiagramRenderer
  IHtmlModelPreparationService
    IHtmlModelPreparationBroker
      IRenderModelBuilderFactory
  IHtmlDocumentService
DrawIODiagramRenderer
  IDrawIOModelPreparationService
    IDrawIOModelPreparationBroker
      IRenderModelBuilderFactory
  IDrawIODocumentService
```

## Simple conditions

Writers consume prepared geometry. HTML and DrawIO must agree about nodes and connections. The format must not invent its own positioning or dependency filters.

## Expanded injected dependency tree

Derived from the current constructors and DI registrations. Repeated services are references to the earlier subtree; named factory implementations are documented separately.

```text
HtmlDiagramRenderer
  IHtmlModelPreparationService -> HtmlModelPreparationService
    IHtmlModelPreparationBroker -> HtmlModelPreparationBroker
      IRenderModelBuilderFactory -> RenderModelBuilderFactory
        IServiceProvider
        IServiceProvider - keyed registrations listed in implementations/readme.md
  IHtmlDocumentService -> HtmlDocumentService
DrawIODiagramRenderer
  IDrawIOModelPreparationService -> DrawIOModelPreparationService
    IDrawIOModelPreparationBroker -> DrawIOModelPreparationBroker
      IRenderModelBuilderFactory -> RenderModelBuilderFactory
        (same dependency graph as above)
  IDrawIODocumentService -> DrawIODocumentService
```
