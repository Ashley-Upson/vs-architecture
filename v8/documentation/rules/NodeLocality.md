# Connection locality

[Catalogue](readme.md)

## Condition

Accept feasible branch moves that reduce crossings, then horizontal connection length.

## Allowed changes

Horizontal node and branch positions.

## Applicability and precedence

Never worsen hard clearance constraints. This is a bounded local optimum, not a global minimum.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[NodeLocalityLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/NodeLocalityLayoutRuleProcessingService.cs)
