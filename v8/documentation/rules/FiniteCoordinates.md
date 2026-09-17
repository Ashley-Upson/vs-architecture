# Finite coordinates

[Catalogue](readme.md)

## Condition

Node positions must be finite. Invalid numeric input is reported; the rule does not invent a replacement position.

## Verification

Test an unmet condition, its adjustment or diagnostic, and an unchanged second application. Test applicability separately from other diagram types.

## Implementation

[FiniteCoordinatesLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/FiniteCoordinatesLayoutRuleProcessingService.cs)

## Applicability

All diagram types.

## Allowed changes

No adjustment; report invalid coordinates.
