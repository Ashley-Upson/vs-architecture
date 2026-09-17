# Entity Relationship

[Implementations](readme.md)

## Dependency path

ContextualRenderModelBuilder -> ContextualLayoutOrchestrationService -> ContextualModelService -> ContextualLayoutService -> DataModelArrangementService / DataModelRoutingService

## Conditions

Internal data carriers only; omit anonymous and architecture types. Type header followed by Type : Name fields. Group related namespaces; arrange related entities together and isolated entities separately. Routes use facing edges, few bends and avoid node interiors. Initial geometry is built by the existing direct algorithm; the resulting RenderModel then passes through IProjectModelLayoutService and its applicable flat conditions. Detailed initial arrangement is not yet a decomposed iterative rule collection.
