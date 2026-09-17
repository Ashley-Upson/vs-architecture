# Contextual diagram model

[Gateway](../../README.md) | [Contract](../rule-contract.md)

Compiler evidence -> the slice selected for Architecture, Composition, CallChain or DataModel.

## Dependency tree

```text
IRenderModelBuilderFactory (diagram type key)
  LayoutModelBuilder -> ILayoutOrchestrationService
    IRenderModelProcessingService
      IProjectModelCompositionProcessingService
  ContextualRenderModelBuilder -> IContextualLayoutOrchestrationService
    IContextualModelService
```

## Simple conditions

Architecture shows runtime type dependencies; composition shows the contents of types; call chains show public method calls; data model shows data carriers. Each implementation owns its filters. See the implementation pages.

## Expanded injected dependency tree

Derived from the current constructors and DI registrations. Repeated services are references to the earlier subtree; named factory implementations are documented separately.

```text
LayoutModelBuilder
  ILayoutOrchestrationService -> LayoutOrchestrationService
    IProjectModelLayoutService -> ProjectModelLayoutService
      IProjectModelLayoutBroker -> ProjectModelLayoutBroker
        ILayoutRuleFactory -> LayoutRuleFactory
          IEnumerable<ILayoutRuleProcessingService> - see the complete rule catalogue
      ILayoutInitializationService -> LayoutInitializationService
        DepthLayoutRuleProcessingService
        ArchitecturalLayerRuleProcessingService
        CategoryRowLayoutRuleProcessingService
        TreeSpacingLayoutRuleProcessingService
        SharedParentCentringLayoutRuleProcessingService
    ILayoutInitializationService -> LayoutInitializationService
      (same dependency graph as above)
    IRenderModelProcessingService -> RenderModelProcessingService
      IProjectModelCompositionProcessingService -> ProjectModelCompositionProcessingService
        IProjectModelPresentationService -> ProjectModelPresentationService
    IRenderConfigurationService -> RenderConfigurationService
      IRenderConfigurationBroker -> RenderConfigurationBroker
ContextualRenderModelBuilder
  IContextualLayoutOrchestrationService -> ContextualLayoutOrchestrationService
    IContextualModelService -> ContextualModelService
      ICompositionTreeService -> CompositionTreeService
      ICallChainModelService -> CallChainModelService
    IContextualLayoutService -> ContextualLayoutService
      ICompositionTreeLayoutService -> CompositionTreeLayoutService
      ICallChainLayoutService -> CallChainLayoutService
        ICallChainRegionService -> CallChainRegionService
      IDataModelRoutingService -> DataModelRoutingService
      IDataModelArrangementService -> DataModelArrangementService
    IProjectModelLayoutService -> ProjectModelLayoutService
      (same dependency graph as above)
```
