# Architecture

[Implementations](readme.md)

## Dependency path

LayoutModelBuilder -> LayoutOrchestrationService -> RenderModelProcessingService / ProjectModelLayoutService

## Conditions

Runtime type dependencies, stopping at external boundaries. Data carriers and exception types are excluded. Base types and directly declared interfaces are labels. NoDuplicates changes graph construction only. Both modes use the same layout rules.
