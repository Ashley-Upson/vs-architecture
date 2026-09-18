# Request and configuration

[Gateway](../../README.md) | [Contract](../rule-contract.md)

Command arguments -> validated DiagramRenderRequest. Parse diagram type, format, project paths and optional JSON configuration. Preserve the supplied output path.

## Dependency tree

```text
DiagramRenderCommand
  IDiagramRenderOrchestrationService
    ICommandParserProcessingService
    IDiagramRequestProcessingService
      IDiagramRequestService
        IDiagramRequestBroker
          IDiagramRendererFactory
```

## Simple conditions

Enums and supported options must be valid. Configuration defaults fill omitted values. The broker selects the named generation graph; it does not own sanitisation.

## Expanded injected dependency tree

Derived from the current constructors and DI registrations. Repeated services are references to the earlier subtree; named factory implementations are documented separately.

```text
DiagramRenderCommand
  IDiagramRenderOrchestrationService -> DiagramRenderOrchestrationService
    ICommandParserProcessingService -> CommandParserProcessingService
      IRenderConfigurationService -> RenderConfigurationService
        IRenderConfigurationBroker -> RenderConfigurationBroker
    IDiagramRequestProcessingService -> DiagramRequestProcessingService
      IDiagramRequestService -> DiagramRequestService
        IDiagramRequestBroker -> DiagramRequestBroker
          IDiagramRendererFactory -> DiagramRendererFactory
            IServiceProvider
            IServiceProvider - keyed registrations listed in implementations/readme.md
```
