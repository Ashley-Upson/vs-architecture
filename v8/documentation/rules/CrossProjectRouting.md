# Cross-project routes

[Catalogue](readme.md)

## Condition

Cross-project edges and local edges share compatible routing channels.

## Allowed changes

Connection points.

## Applicability and precedence

Local points use local coordinates; cross-project points use canvas coordinates.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[CrossProjectRoutingLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/CrossProjectRoutingLayoutRuleProcessingService.cs)
