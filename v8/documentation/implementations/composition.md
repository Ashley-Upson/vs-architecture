# Composition

[Implementations](readme.md)

## Dependency path

ContextualRenderModelBuilder -> ContextualLayoutOrchestrationService -> ContextualModelService / CompositionTreeService -> CompositionTreeLayoutService

## Conditions

One expanded type tree with constructors, methods, references and lambda labels. Define each method body once; calls refer to it. Stop expansion at another type. Group containers by project and namespace, trees horizontally and containers vertically. No cross-tree link lines. Initial geometry is built by the existing direct algorithm; the resulting RenderModel then passes through IProjectModelLayoutService and its applicable flat conditions. Detailed initial arrangement is not yet a decomposed iterative rule collection.
