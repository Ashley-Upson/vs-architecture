# Compiler model

[Gateway](../../README.md) | [Contract](../rule-contract.md)

Project paths -> ProjectModel arrays containing Roslyn evidence.

## Dependency tree

```text
ProjectModelBuilder
  IProjectModelOrchestrationService
    IProjectProcessingService
    IProjectTypesProcessingService
    IProjectDependenciesProcessingService
      foundation services
        IRoslynBroker / IProjectModelBroker
```

## Simple conditions

Retain compiler evidence needed by all diagram types. Diagram-specific exclusion belongs to projection, not global extraction. Distinguish injection, direct usage, composition references, inheritance and method calls.

## Expanded injected dependency tree

Derived from the current constructors and DI registrations. Repeated services are references to the earlier subtree; named factory implementations are documented separately.

```text
ProjectModelBuilder
  IProjectModelOrchestrationService -> ProjectModelOrchestrationService
    IProjectProcessingService -> ProjectProcessingService
      IProjectService -> ProjectService
        IFileBroker -> FileBroker
    IProjectTypesProcessingService -> ProjectTypesProcessingService
      IProjectTypesService -> ProjectTypesService
        IRoslynBroker -> RoslynBroker
    IProjectDependenciesProcessingService -> ProjectDependenciesProcessingService
      IProjectDependenciesService -> ProjectDependenciesService
        IRoslynBroker -> RoslynBroker
          (same dependency graph as above)
```
