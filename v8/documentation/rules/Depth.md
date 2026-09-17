# Dependency depth

[Catalogue](readme.md)

## Condition

Every non-cycle dependency points to a later row.

## Allowed changes

Node Y coordinates.

## Applicability and precedence

Cycle-closing edges are retained; they cannot all point down.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[DepthLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/DepthLayoutRuleProcessingService.cs)
