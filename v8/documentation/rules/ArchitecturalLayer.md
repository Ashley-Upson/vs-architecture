# Architectural order

[Catalogue](readme.md)

## Condition

The category ordering minimises backwards category transitions.

## Allowed changes

ArchitecturalLayers.

## Applicability and precedence

Infer ordering from graph evidence, not fixed suffix priority.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[ArchitecturalLayerRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/ArchitecturalLayerRuleProcessingService.cs)
