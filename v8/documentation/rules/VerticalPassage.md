# Vertical passages

[Catalogue](readme.md)

## Condition

Deep connections have a clear descent corridor through intervening rows.

## Allowed changes

Obstructing branch positions and explicit centring exemptions.

## Applicability and precedence

Use the actual graph and direction of travel; do not remove dependencies.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[VerticalPassageLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/VerticalPassageLayoutRuleProcessingService.cs)
