# Call Chain

[Implementations](readme.md)

## Dependency path

ContextualRenderModelBuilder -> ContextualLayoutOrchestrationService -> CallChainModelService -> CallChainLayoutService -> CallChainRegionService

## Conditions

Public method chains travel left to right. Include otherwise unrepresented public branches as roots. Expand only consumed portions of child trees. Omit Inputs when empty; show Outputs. Keep destination tracks separate, clear child nodes by positioning, and retain parent/type attachment. Initial geometry is built by the existing direct algorithm; the resulting RenderModel then passes through IProjectModelLayoutService and its applicable flat conditions. Detailed initial arrangement is not yet a decomposed iterative rule collection.
