# Render model and layout

[Gateway](../../README.md) | [Contract](../rule-contract.md)

Selected diagram graph -> positioned nodes, containers, labels and routes.

## Dependency tree

```text
LayoutModelBuilder
  ILayoutOrchestrationService
    IRenderModelProcessingService (initial construction)
    ILayoutInitializationService (initial geometry)
    IProjectModelLayoutService (iteration)
      IProjectModelLayoutBroker
        ILayoutRuleFactory
          IEnumerable<ILayoutRuleProcessingService>
```

## Simple conditions

Apply the flat rule catalogue until stable and valid. The engine must not contain geometric decisions. Initialization, positioning, routing and styling have explicit responsibilities. Contextual implementations construct initial geometry with their separate layout services, then use this same engine. Their detailed initial algorithms remain documented as such; they are not yet decomposed into independently applied arrangement rules.

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
