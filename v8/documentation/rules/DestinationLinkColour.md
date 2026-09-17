# Ordinary link colour

[Catalogue](readme.md)

## Condition

Non-violating links use destination fill when ColourLines is enabled, otherwise the default stroke.

## Allowed changes

Connection Stroke.

## Applicability and precedence

Does not recolour architecture violations.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[DestinationLinkColourLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/DestinationLinkColourLayoutRuleProcessingService.cs)
