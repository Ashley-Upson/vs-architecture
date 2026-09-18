# Shared project centring

[Catalogue](readme.md)

## Condition

Centre a shared child project group under its consumer group when the row has room; do not overlap neighbouring project containers.

## Verification

Test an unmet condition, its adjustment or diagnostic, and an unchanged second application. Test applicability separately from other diagram types.

## Implementation

[ProjectSharedCentringLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/ProjectSharedCentringLayoutRuleProcessingService.cs)

## Applicability

Architecture with cross-project connections.

## Allowed changes

Shared child project X coordinates where neighbouring boxes permit the move.
