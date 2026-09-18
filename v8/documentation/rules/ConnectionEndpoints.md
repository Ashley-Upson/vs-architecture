# Connection endpoints

[Catalogue](readme.md)

## Condition

Every connection must identify two existing nodes. Missing identities are reported, never silently pruned.

## Verification

Test an unmet condition, its adjustment or diagnostic, and an unchanged second application. Test applicability separately from other diagram types.

## Implementation

[ConnectionEndpointsLayoutRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/ConnectionEndpointsLayoutRuleProcessingService.cs)

## Applicability

All diagram types.

## Allowed changes

No adjustment; report missing source or destination IDs.
