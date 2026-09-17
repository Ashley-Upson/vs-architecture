# Branch compaction

[Catalogue](readme.md)

Propose packing completed branches, and accept only an improvement to the shared quality ordering for an initialized model: fewer clearance violations, fewer blocked passages, fewer crossings, shorter horizontal connections, then less width. Restore rejected proposals exactly.

Initial unprepared test models can be packed before that optimisation guard; production initialization precedes the rules. Shared-chain alignment is a separate rule.

## Implementation

[LayoutCleanupRuleProcessingService](../../StandardIo.ArchitectureDiagram.Core2/Services/Processings/Layout/LayoutCleanupRuleProcessingService.cs)
