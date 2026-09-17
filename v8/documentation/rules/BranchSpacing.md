# Branch clearance

[Catalogue](readme.md)

## Condition

Owned branches have adequate clearance in the rows they occupy.

## Allowed changes

Horizontal branch positions.

## Applicability and precedence

Do not expand a graph that already meets the condition.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[BranchSpacingLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/BranchSpacingLayoutRuleProcessingService.cs)
