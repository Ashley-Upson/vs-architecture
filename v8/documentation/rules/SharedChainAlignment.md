# Shared chain alignment

[Catalogue](readme.md)

## Condition

Move an independent single-child chain nearer its shared child when the proposed translation respects clearance.

## Allowed changes

Horizontal chain positions.

## Applicability and precedence

Preserve peer clearance and clear vertical passages, including passages whose parents have not been explicitly offset. Reject a proposal that moves an obstacle into an existing corridor. Moving the source itself defines a new corridor; allow that move so the vertical-passage rule can clear its path.

## Verification

Exercise an unmet condition, assert the stated outcome, then apply the rule again and assert no further meaningful change. Integration checks must also assert that neighbouring rules do not undo the outcome.

## Implementation

[SharedChainAlignmentLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/SharedChainAlignmentLayoutRuleProcessingService.cs)
