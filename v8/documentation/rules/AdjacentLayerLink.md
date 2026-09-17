# Adjacent-category calls

[Catalogue](readme.md)

## Condition

A classified call to a non-exposure category is a violation if it skips the next architectural category. Physical row distance is not the criterion.

## Verification

Test an unmet condition, its adjustment or diagnostic, and an unchanged second application. Test applicability separately from other diagram types.

## Implementation

[AdjacentLayerLinkLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/AdjacentLayerLinkLayoutRuleProcessingService.cs)

## Applicability

Architecture, classified calls to a non-exposure category, excluding local same-category calls.

## Allowed changes

IsLayerViolation only.
