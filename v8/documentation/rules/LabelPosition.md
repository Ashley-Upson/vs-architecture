# Label positions

[Catalogue](readme.md)

## Condition

Text anchors match the final node rectangles.

## Allowed changes

TextLines coordinates.

## Applicability and precedence

Preserve text, emphasis and font sizes.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[LabelPositionLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/LabelPositionLayoutRuleProcessingService.cs)
