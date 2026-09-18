# Internal routes

[Catalogue](readme.md)

## Condition

Every internal edge has a route between its actual source and destination.

## Allowed changes

Connection points.

## Applicability and precedence

With cross-project architecture edges, unified routing owns all routes to prevent two writers competing.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[RoutingLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/RoutingLayoutRuleProcessingService.cs)
