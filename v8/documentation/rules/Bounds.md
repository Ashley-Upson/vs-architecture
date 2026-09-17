# Project bounds

[Catalogue](readme.md)

## Condition

Each project encloses its nodes with padding.

## Allowed changes

Project dimensions and local origin.

## Applicability and precedence

Translate as a unit; do not change relative node positions.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[BoundsLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/BoundsLayoutRuleProcessingService.cs)
