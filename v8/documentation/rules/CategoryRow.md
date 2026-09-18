# Category rows

[Catalogue](readme.md)

## Condition

Each occupied row belongs to one category; repetitions follow dependencies.

## Allowed changes

Rows and node Y.

## Applicability and precedence

Preserve established gutter clearance when row membership is unchanged.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[CategoryRowLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/CategoryRowLayoutRuleProcessingService.cs)
