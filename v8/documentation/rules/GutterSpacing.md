# Gutter capacity

[Catalogue](readme.md)

## Condition

Adjacent rows leave enough vertical room for their destination lanes.

## Allowed changes

Node Y below an insufficient gutter.

## Applicability and precedence

Do not shrink an already adequate gutter during the same layout.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[GutterSpacingLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/GutterSpacingLayoutRuleProcessingService.cs)
